using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Resources;
using HyperCardSharp.Core.Stack;
using HyperCardSharp.HyperTalk.Interpreter;
using HyperCardSharp.HyperTalk.MessagePassing;
using HyperCardSharp.Rendering;
using SkiaSharp;

namespace HyperCardSharp.App.ViewModels;

public partial class StackViewModel : ObservableObject
{
    private StackFile? _stack;
    private CardRenderer? _renderer;
    private List<int> _cardOrder = new();
    private readonly HyperTalkInterpreter _interpreter;
    private readonly MessageDispatcher _dispatcher;
    private readonly MediaService _media = new();

    // Tracks the background ID of the currently displayed card so we can detect
    // background changes during navigation and fire openBackground / closeBackground.
    private int _currentBackgroundId = -1;

    [ObservableProperty]
    private int _currentCardIndex;

    [ObservableProperty]
    private int _totalCards;

    [ObservableProperty]
    private string _title = "HyperCard#";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private SKBitmap? _currentBitmap;

    [ObservableProperty]
    private RenderMode _renderMode = RenderMode.BlackAndWhite;

    /// <summary>Menu label that shows the next mode to switch to.</summary>
    public string RenderModeLabel =>
        RenderMode == RenderMode.BlackAndWhite ? "Switch to Color Mode" : "Switch to Black & White Mode";

    partial void OnRenderModeChanged(RenderMode value)
        => OnPropertyChanged(nameof(RenderModeLabel));

    /// <summary>Raised when a HyperTalk script wants to show an answer dialog.</summary>
    public event Action<string>? ShowAnswerDialog;

    /// <summary>
    /// Raised when HyperTalk executes <c>go to stack "name"</c>.
    /// The MainWindow handles this by locating and loading the named stack file.
    /// Arguments: stackName, optional cardName, optional 1-based card number.
    /// </summary>
    public event Action<string, string?, int?>? CrossStackNavigationRequested;

    /// <summary>
    /// Raised when a HyperTalk <c>go home</c> command is executed.
    /// The App layer should handle this by opening a file picker.
    /// </summary>
    public event Action? GoHomeRequested;

    /// <summary>
    /// Raised when a HyperTalk <c>visual effect</c> / <c>go</c> pair fires a named transition.
    /// from: old card bitmap (caller must dispose); to: new card bitmap (same as CurrentBitmap).
    /// </summary>
    public event Action<SKBitmap, SKBitmap, string, string?, string?>? TransitionRequested;

    // Pending visual effect queued by the HyperTalk interpreter (cleared on next navigation).
    private (string Effect, string? Speed, string? Direction)? _pendingEffect;

    public bool IsLoaded => _stack != null;

    // Use the same fallbacks as CardRenderer: 512×342 (classic Mac screen).
    // A stored zero means the stack omitted the field; fall back to the standard size.
    public int CardWidth  { get { var w = _stack?.StackHeader.CardWidth  ?? 0; return w > 0 ? w : 512; } }
    public int CardHeight { get { var h = _stack?.StackHeader.CardHeight ?? 0; return h > 0 ? h : 342; } }

    public StackViewModel()
    {
        _interpreter = new HyperTalkInterpreter();
        _dispatcher  = new MessageDispatcher();
        WireInterpreterCallbacks();
    }

    private void WireInterpreterCallbacks()
    {
        _interpreter.GoNext     = () => NavigateTo((CurrentCardIndex + 1) % Math.Max(1, _cardOrder.Count));
        _interpreter.GoPrev     = () => NavigateTo((CurrentCardIndex - 1 + Math.Max(1, _cardOrder.Count)) % Math.Max(1, _cardOrder.Count));
        _interpreter.GoFirst    = () => NavigateTo(0);
        _interpreter.GoLast     = () => NavigateTo(Math.Max(0, _cardOrder.Count - 1));
        _interpreter.GoToCardByIndex = idx =>
        {
            if (_stack == null || _cardOrder.Count == 0) return;
            NavigateTo(Math.Clamp(idx - 1, 0, _cardOrder.Count - 1));
        };
        _interpreter.GoToCardByName = name =>
        {
            if (_stack == null || _cardOrder.Count == 0) return;
            for (int i = 0; i < _cardOrder.Count; i++)
            {
                var card = _stack.Cards.FirstOrDefault(c => c.Header.Id == _cardOrder[i]);
                if (card != null && string.Equals(card.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    NavigateTo(i);
                    return;
                }
            }
            _interpreter.LogMessage($"HyperTalk: card \"{name}\" not found");
        };
        _interpreter.GoToCardById = blockId =>
        {
            if (_stack == null || _cardOrder.Count == 0) return;
            int idx = _cardOrder.IndexOf(blockId);
            if (idx >= 0)
                NavigateTo(idx);
            else
                _interpreter.LogMessage($"HyperTalk: card id {blockId} not found");
        };
        _interpreter.GetFieldText = fieldSpec =>
        {
            var card = CurrentCard();
            if (card == null) return null;
            var bg = CurrentBackground(card);
            return FindFieldText(fieldSpec, card, bg);
        };
        _interpreter.SetFieldText = (fieldSpec, text) =>
        {
            var card = CurrentCard();
            if (card == null) return;
            var bg = CurrentBackground(card);
            if (MutateFieldText(fieldSpec, text, card, bg))
                RenderCurrentCard();
        };
        _interpreter.GetButtonHilite = partSpec =>
        {
            var card = CurrentCard();
            if (card == null) return null;
            var bg = CurrentBackground(card);
            var part = FindPartBySpec(partSpec, card, bg);
            return part?.HiliteState;
        };
        _interpreter.SetButtonHilite = (partSpec, hilite) =>
        {
            var card = CurrentCard();
            if (card == null) return;
            var bg = CurrentBackground(card);
            var part = FindPartBySpec(partSpec, card, bg);
            if (part != null)
            {
                part.HiliteState = hilite;
                RenderCurrentCard();
            }
        };
        _interpreter.QueueVisualEffect = (effect, speed, dir)
            => _pendingEffect = (effect, speed, dir);
        _interpreter.ShowDialog = msg => ShowAnswerDialog?.Invoke(msg);
        _interpreter.ShowAskDialog = (prompt, def) => null;  // non-interactive for now
        _interpreter.LogMessage = msg =>
        {
            // Only surface genuine runtime errors; suppress "not implemented" noise
            if (msg.StartsWith("HyperTalk runtime error:"))
                StatusText = msg;
        };

        // Card/stack context callbacks
        _interpreter.GetCurrentCardNumber = () => CurrentCardIndex + 1;
        _interpreter.GetTotalCards        = () => _cardOrder.Count;
        _interpreter.GetCurrentCardId     = () => CurrentCard()?.Header.Id ?? 0;
        _interpreter.GetCurrentCardName   = () => CurrentCard()?.Name ?? "";

        _interpreter.LockScreen   = () => { }; // no visual lock yet
        _interpreter.UnlockScreen = () => { };

        _interpreter.SetPartVisible = (partSpec, visible) =>
        {
            var card = CurrentCard();
            if (card == null) return;
            var bg = CurrentBackground(card);
            var part = FindPartBySpec(partSpec, card, bg);
            if (part != null)
            {
                part.VisibleOverride = visible;
                RenderCurrentCard();
            }
        };

        _interpreter.GetScriptForTarget = (msg, targetStr) =>
        {
            var lower = targetStr.ToLowerInvariant().Trim();
            var card = CurrentCard();
            var bg   = card != null ? CurrentBackground(card) : null;
            if (lower is "this card" or "card")
                return card?.Script;
            if (lower is "this background" or "this bkgd" or "background" or "bkgd")
                return bg?.Script;
            if (lower == "this stack" || lower == "stack")
                return null; // stack-level script not parsed yet
            // Resolve "button <name>" / "field <name>" by name
            if (card != null)
            {
                var part = FindPartBySpec(targetStr, card, bg);
                if (part != null && !string.IsNullOrWhiteSpace(part.Script))
                    return part.Script;
            }
            return null;
        };

        _interpreter.DispatchMessageInScript = (msg, scriptText) =>
        {
            var result = _dispatcher.DispatchMessage(msg, scriptText, _interpreter);
            return result == DispatchResult.Handled
                ? HyperCardSharp.HyperTalk.Interpreter.ExecutionResult.Normal
                : HyperCardSharp.HyperTalk.Interpreter.ExecutionResult.Normal;
        };

        _interpreter.SimulateClickAt = (x, y) => HandleCardClick(x, y);

        _interpreter.AppendToFocusedField = text =>
        {
            // Append to the last field that received a click, or the first visible field
            var card = CurrentCard();
            if (card == null) return;
            var bg = CurrentBackground(card);
            var field = card.Parts.FirstOrDefault(p => p.IsField && p.Visible)
                     ?? bg?.Parts.FirstOrDefault(p => p.IsField && p.Visible);
            if (field == null) return;
            var content = card.PartContents.FirstOrDefault(pc => pc.PartId == field.PartId);
            if (content != null)
            {
                content.Text += text;
                RenderCurrentCard();
            }
        };

        _interpreter.SetPartProperty = (partSpec, property, value) =>
        {
            var card = CurrentCard();
            if (card == null) return;
            var bg = CurrentBackground(card);
            var part = FindPartBySpec(partSpec, card, bg);
            if (part == null) return;
            switch (property)
            {
                case "name":      part.Name = value; break;
                case "enabled":
                    part.EnabledOverride = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "textfont":
                    // Accept a number (Mac font ID) or a font name string (map to ID if known)
                    if (short.TryParse(value, out short fontId))
                        part.TextFontId = fontId;
                    break;
                case "textsize":
                    if (ushort.TryParse(value, out ushort sz))
                        part.TextSize = sz;
                    break;
                case "textstyle":
                    // "bold", "italic", "plain" etc. — map to style-flag byte
                    part.TextStyle = ParseTextStyleFlags(value);
                    break;
                case "style":
                    if (Enum.TryParse<HyperCardSharp.Core.Parts.PartStyle>(value, true, out var ps))
                        part.Style = ps;
                    break;
                case "rect":
                case "rectangle":
                {
                    // "left,top,right,bottom" or "left, top, right, bottom"
                    var coords = value.Split(',');
                    if (coords.Length == 4 &&
                        ushort.TryParse(coords[0].Trim(), out ushort rl) &&
                        ushort.TryParse(coords[1].Trim(), out ushort rt) &&
                        ushort.TryParse(coords[2].Trim(), out ushort rr) &&
                        ushort.TryParse(coords[3].Trim(), out ushort rb))
                    {
                        part.Left = rl; part.Top = rt;
                        part.Right = rr; part.Bottom = rb;
                    }
                    break;
                }
                case "loc":
                case "location":
                {
                    // "h,v" — center point; adjust rect preserving size
                    var coords = value.Split(',');
                    if (coords.Length == 2 &&
                        int.TryParse(coords[0].Trim(), out int lh) &&
                        int.TryParse(coords[1].Trim(), out int lv))
                    {
                        int hw = part.Width / 2, hh = part.Height / 2;
                        part.Left   = (ushort)Math.Max(0, lh - hw);
                        part.Top    = (ushort)Math.Max(0, lv - hh);
                        part.Right  = (ushort)(part.Left + part.Width);
                        part.Bottom = (ushort)(part.Top  + part.Height);
                    }
                    break;
                }
                case "width":
                    if (ushort.TryParse(value, out ushort w))
                        part.Right = (ushort)(part.Left + w);
                    break;
                case "height":
                    if (ushort.TryParse(value, out ushort h))
                        part.Bottom = (ushort)(part.Top + h);
                    break;
                case "textcolor":
                    // Ignore for now — color rendering is AddColor-based; log silently
                    break;
            }
            RenderCurrentCard();
        };

        _interpreter.PlaySound = soundName =>
        {
            if (_stack == null) return;

            // Look up snd resource by name first, then fall back to first available
            byte[]? rawSnd = null;
            if (!_stack.SoundsByName.TryGetValue(soundName, out rawSnd) &&
                !string.IsNullOrEmpty(soundName) &&
                short.TryParse(soundName, out short sndId))
            {
                _stack.SoundsById.TryGetValue(sndId, out rawSnd);
            }

            if (rawSnd == null)
            {
                _interpreter.LogMessage($"[HyperTalk] play '{soundName}' — snd resource not found");
                return;
            }

            var wav = SoundDecoder.Decode(rawSnd);
            if (wav == null)
            {
                _interpreter.LogMessage($"[HyperTalk] play '{soundName}' — could not decode snd resource");
                return;
            }

            _media.PlayWav(wav);
        };

        _interpreter.StopSound = () => _media.Stop();

        _interpreter.ExecuteScriptText = scriptText =>
        {
            // Parse and execute the script text at runtime ('do' command)
            var card = CurrentCard();
            var bg   = card != null ? CurrentBackground(card) : null;
            // Wrap in a temporary mouseUp handler so dispatcher can run it
            var wrapped = $"on __do__\n{scriptText}\nend __do__";
            var result = _dispatcher.DispatchMessage("__do__", wrapped, _interpreter);
            return result == DispatchResult.Handled
                ? HyperCardSharp.HyperTalk.Interpreter.ExecutionResult.Normal
                : HyperCardSharp.HyperTalk.Interpreter.ExecutionResult.Normal;
        };

        _interpreter.FindInStack = (searchText, fieldName) =>
        {
            if (_stack == null || _cardOrder.Count == 0) return;
            // Search forward from current card (wrapping around)
            int start = CurrentCardIndex;
            for (int i = 1; i <= _cardOrder.Count; i++)
            {
                int idx = (start + i) % _cardOrder.Count;
                int cardId = _cardOrder[idx];
                var card = _stack.Cards.FirstOrDefault(c => c.Header.Id == cardId);
                if (card == null) continue;
                var bg = CurrentBackground(card);
                if (CardContainsText(searchText, card, bg, fieldName))
                {
                    NavigateTo(idx);
                    return;
                }
            }
            _interpreter.LogMessage($"[HyperTalk] find: '{searchText}' not found");
        };

        _interpreter.GoToStack = (stackName, cardName, cardNum) =>
            CrossStackNavigationRequested?.Invoke(stackName, cardName, cardNum);

        _interpreter.GoHome = () => GoHomeRequested?.Invoke();
    }

    public void HandleCardClick(float cardX, float cardY)
    {
        var card = CurrentCard();
        if (card == null) return;
        var bg = CurrentBackground(card);

        var part = HitTest(cardX, cardY, card, bg);

        // HyperCard message hierarchy: button → card → background
        // Stop on Handled; continue climbing on NotFound or Passed.
        if (part != null && !string.IsNullOrWhiteSpace(part.Script))
        {
            var r = _dispatcher.DispatchMessage("mouseUp", part.Script, _interpreter);
            if (r == DispatchResult.Handled) return;
        }

        if (!string.IsNullOrWhiteSpace(card.Script))
        {
            var r = _dispatcher.DispatchMessage("mouseUp", card.Script, _interpreter);
            if (r == DispatchResult.Handled) return;
        }

        if (bg != null && !string.IsNullOrWhiteSpace(bg.Script))
        {
            _dispatcher.DispatchMessage("mouseUp", bg.Script, _interpreter);
        }
    }

    /// <summary>
    /// Returns true if the given card coordinates are over a clickable button.
    /// In HyperCard, the browse tool always shows a hand cursor over any visible button.
    /// </summary>
    public bool IsOverClickableButton(float cardX, float cardY)
    {
        var card = CurrentCard();
        if (card == null) return false;
        var bg = CurrentBackground(card);

        var part = HitTest(cardX, cardY, card, bg);
        return part is { IsButton: true };
    }

    /// <summary>The file name of the currently opened file (e.g., "neuroblast.img").</summary>
    public string? CurrentFileName { get; private set; }

    /// <summary>The display name of the current stack within the file.</summary>
    public string? CurrentStackName { get; private set; }

    public void LoadStack(byte[] fileData, string fileName, string? stackName = null, byte[]? resourceFork = null)
    {
        // Quick STAK magic check: type field is at offset 4-7 in the first block header
        if (fileData.Length < 8 ||
            fileData[4] != 'S' || fileData[5] != 'T' || fileData[6] != 'A' || fileData[7] != 'K')
        {
            StatusText = $"Not a HyperCard stack: \"{fileName}\" (no STAK magic at offset 4).";
            return;
        }

        try
        {
            var parser = new StackParser();
            _stack = parser.Parse(fileData, resourceFork);
            _renderer?.ClearCache();
            _renderer = new CardRenderer(_stack);

            // Get card order from PAGE blocks
            _cardOrder = _stack.GetCardOrder().ToList();
            if (_cardOrder.Count == 0)
            {
                // Fallback: use block order
                _cardOrder = _stack.Cards.Select(c => c.Header.Id).ToList();
            }

            CurrentFileName = fileName;
            CurrentStackName = stackName;

            TotalCards = _cardOrder.Count;
            Title = stackName != null
                ? $"HyperCard# — {fileName} — {stackName}"
                : $"HyperCard# — {fileName}";
            CurrentCardIndex = 0;
            RenderCurrentCard();

            // Fire initial lifecycle events
            var firstCard = CurrentCard();
            var firstBg   = firstCard != null ? CurrentBackground(firstCard) : null;
            _currentBackgroundId = firstCard?.BackgroundId ?? -1;
            DispatchLifecycle("openStack", firstCard, firstBg);
            DispatchLifecycle("openCard",  firstCard, firstBg);
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading stack \"{stackName ?? fileName}\": {ex.Message}";
        }
    }

    [RelayCommand]
    public void NextCard()
    {
        if (_stack == null || _cardOrder.Count == 0) return;
        NavigateTo((CurrentCardIndex + 1) % _cardOrder.Count);
    }

    [RelayCommand]
    public void PreviousCard()
    {
        if (_stack == null || _cardOrder.Count == 0) return;
        NavigateTo((CurrentCardIndex - 1 + _cardOrder.Count) % _cardOrder.Count);
    }

    [RelayCommand]
    public void FirstCard()
    {
        if (_stack == null || _cardOrder.Count == 0) return;
        NavigateTo(0);
    }

    [RelayCommand]
    public void LastCard()
    {
        if (_stack == null || _cardOrder.Count == 0) return;
        NavigateTo(_cardOrder.Count - 1);
    }

    /// <summary>
    /// Core navigation primitive.  Fires lifecycle events (closeCard / openCard / openBackground)
    /// then captures any pending visual effect and raises <see cref="TransitionRequested"/>.
    /// </summary>
    private void NavigateTo(int newIndex)
    {
        if (_stack == null || _cardOrder.Count == 0) return;
        newIndex = Math.Clamp(newIndex, 0, _cardOrder.Count - 1);

        // Determine old/new backgrounds before we change anything
        var oldCard = CurrentCard();
        var oldBg   = oldCard != null ? CurrentBackground(oldCard) : null;
        int oldBgId = oldCard?.BackgroundId ?? -1;

        int newCardBlockId = _cardOrder[newIndex];
        var newCardObj = _stack.Cards.FirstOrDefault(c => c.Header.Id == newCardBlockId);
        int newBgId    = newCardObj?.BackgroundId ?? -1;
        var newBgObj   = newCardObj != null ? CurrentBackground(newCardObj) : null;

        bool bgChanged = newBgId != oldBgId;

        // ── 1. Close lifecycle ───────────────────────────────────────────────────
        DispatchLifecycle("closeCard", oldCard, oldBg);
        if (bgChanged)
            DispatchLifecycle("closeBackground", oldCard, oldBg);

        // ── 2. Capture pending effect (may have been queued by a closeCard handler)
        var effect = _pendingEffect;
        _pendingEffect = null;

        // ── 3. Switch card & render ──────────────────────────────────────────────
        if (effect != null && CurrentBitmap != null && TransitionRequested != null)
        {
            var fromBitmap = CurrentBitmap.Copy();
            CurrentCardIndex = newIndex;
            RenderCurrentCard();
            var toBitmap = CurrentBitmap;
            if (fromBitmap != null && toBitmap != null)
                TransitionRequested.Invoke(
                    fromBitmap, toBitmap,
                    effect.Value.Effect, effect.Value.Speed, effect.Value.Direction);
            else
                fromBitmap?.Dispose();
        }
        else
        {
            CurrentCardIndex = newIndex;
            RenderCurrentCard();
        }

        _currentBackgroundId = newBgId;

        // ── 4. Open lifecycle ────────────────────────────────────────────────────
        if (bgChanged)
            DispatchLifecycle("openBackground", newCardObj, newBgObj);
        DispatchLifecycle("openCard", newCardObj, newBgObj);
    }

    /// <summary>
    /// Dispatches a lifecycle message (e.g. openCard, closeCard) to the card script,
    /// then to the background script if the card script returned Passed or NotFound.
    /// </summary>
    private void DispatchLifecycle(string handlerName, CardBlock? card, BackgroundBlock? bg)
    {
        if (card != null && !string.IsNullOrWhiteSpace(card.Script))
        {
            var r = _dispatcher.DispatchMessage(handlerName, card.Script, _interpreter);
            if (r == DispatchResult.Handled) return;
        }
        if (bg != null && !string.IsNullOrWhiteSpace(bg.Script))
            _dispatcher.DispatchMessage(handlerName, bg.Script, _interpreter);
    }


    private void UpdateStatusText()
    {
        if (_stack == null || _cardOrder.Count == 0)
        {
            StatusText = "No stack loaded. Ctrl+O to open. Ctrl+1/2/3/4 to zoom.";
            return;
        }

        int cardId = _cardOrder[CurrentCardIndex];
        var card = _stack.Cards.FirstOrDefault(c => c.Header.Id == cardId);
        if (card == null) return;

        var partInfo = card.Parts.Count > 0 ? $", {card.Parts.Count} parts" : "";
        var nameInfo = !string.IsNullOrEmpty(card.Name) ? $" \"{card.Name}\"" : "";
        var fileInfo = CurrentFileName != null ? $"{CurrentFileName}: " : "";
        var warning = BuildStackWarning();
        StatusText = $"{fileInfo}Card {CurrentCardIndex + 1}/{TotalCards}{nameInfo}{partInfo}{warning}";
    }

    private string BuildStackWarning()
    {
        if (_stack == null) return "";
        var parts = new System.Text.StringBuilder();
        if (_stack.IsHyperCard1x)
            parts.Append(" [HyperCard 1.x — partial support]");
        if (_stack.IsPasswordProtected)
            parts.Append(" [Password protected]");
        return parts.ToString();
    }

    private void RenderCurrentCard()
    {
        if (_stack == null || _renderer == null || _cardOrder.Count == 0) return;

        int cardId = _cardOrder[CurrentCardIndex];
        var card = _stack.Cards.FirstOrDefault(c => c.Header.Id == cardId);
        if (card == null) return;

        var oldBitmap = CurrentBitmap;
        CurrentBitmap = _renderer.RenderCard(card, RenderMode);
        oldBitmap?.Dispose();

        UpdateStatusText();
    }

    [RelayCommand]
    public void ToggleRenderMode()
    {
        RenderMode = RenderMode == RenderMode.BlackAndWhite
            ? RenderMode.Color
            : RenderMode.BlackAndWhite;
        RenderCurrentCard();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private CardBlock? CurrentCard()
    {
        if (_stack == null || _cardOrder.Count == 0) return null;
        int cardId = _cardOrder[CurrentCardIndex];
        return _stack.Cards.FirstOrDefault(c => c.Header.Id == cardId);
    }

    private BackgroundBlock? CurrentBackground(CardBlock card)
        => _stack?.Backgrounds.FirstOrDefault(b => b.Header.Id == card.BackgroundId);

    private static Part? HitTest(float x, float y, CardBlock card, BackgroundBlock? bg)
    {
        // Card parts are on top
        foreach (var part in card.Parts)
            if (part.Visible && x >= part.Left && x < part.Right && y >= part.Top && y < part.Bottom)
                return part;

        // Background parts underneath
        if (bg != null)
            foreach (var part in bg.Parts)
                if (part.Visible && x >= part.Left && x < part.Right && y >= part.Top && y < part.Bottom)
                    return part;

        return null;
    }

    private static string? FindFieldText(string fieldSpec, CardBlock card, BackgroundBlock? bg)
    {
        // Look for a field by name or ID in card then background
        foreach (var part in card.Parts)
        {
            if (!part.IsField) continue;
            if (string.Equals(part.Name, fieldSpec, StringComparison.OrdinalIgnoreCase) ||
                part.PartId.ToString() == fieldSpec)
            {
                var content = card.PartContents.FirstOrDefault(pc => pc.PartId == part.PartId);
                return content?.Text ?? "";
            }
        }
        if (bg != null)
        {
            foreach (var part in bg.Parts)
            {
                if (!part.IsField) continue;
                if (string.Equals(part.Name, fieldSpec, StringComparison.OrdinalIgnoreCase) ||
                    part.PartId.ToString() == fieldSpec)
                {
                    var content = bg.PartContents.FirstOrDefault(pc => pc.PartId == part.PartId);
                    return content?.Text ?? "";
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves a HyperTalk part specifier (e.g. "button 1", "button \"Name\"", "field 3")
    /// to the matching <see cref="Part"/> on the current card or background, or null if not found.
    /// </summary>
    private static Part? FindPartBySpec(string spec, CardBlock card, BackgroundBlock? bg)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;

        // Strip optional type prefix: "button", "field", "btn"
        var s = spec.Trim();
        PartType? typeFilter = null;
        foreach (var (prefix, t) in new[] { ("button ", PartType.Button), ("btn ", PartType.Button), ("field ", PartType.Field) })
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                typeFilter = t;
                s = s[prefix.Length..].Trim().Trim('"');
                break;
            }
        }

        // Check card-layer parts first, then background
        IEnumerable<IEnumerable<Part>> layers = bg != null
            ? [card.Parts, bg.Parts]
            : [card.Parts];

        foreach (var layer in layers)
        {
            foreach (var part in layer)
            {
                if (typeFilter.HasValue && part.Type != typeFilter.Value) continue;
                if (string.Equals(part.Name, s, StringComparison.OrdinalIgnoreCase)) return part;
                if (short.TryParse(s, out short id) && part.PartId == id) return part;
            }
        }
        return null;
    }

    /// <summary>
    /// Sets the text content for a field identified by <paramref name="fieldSpec"/>.
    /// Returns true if a matching field was found and updated.
    /// </summary>
    private static bool MutateFieldText(string fieldSpec, string text, CardBlock card, BackgroundBlock? bg)
    {
        // Card-layer fields
        foreach (var part in card.Parts)
        {
            if (!part.IsField) continue;
            if (!string.Equals(part.Name, fieldSpec, StringComparison.OrdinalIgnoreCase) &&
                part.PartId.ToString() != fieldSpec) continue;

            var content = card.PartContents.FirstOrDefault(pc => pc.PartId == part.PartId);
            if (content != null)
            {
                content.Text = text;
            }
            else
            {
                // Create a new PartContent entry so the field has text
                card.PartContents.Add(new PartContent { PartId = part.PartId, Text = text });
            }
            return true;
        }

        // Background-layer fields
        if (bg != null)
        {
            foreach (var part in bg.Parts)
            {
                if (!part.IsField) continue;
                if (!string.Equals(part.Name, fieldSpec, StringComparison.OrdinalIgnoreCase) &&
                    part.PartId.ToString() != fieldSpec) continue;

                var content = bg.PartContents.FirstOrDefault(pc => pc.PartId == part.PartId);
                if (content != null)
                {
                    content.Text = text;
                }
                else
                {
                    bg.PartContents.Add(new PartContent { PartId = part.PartId, Text = text });
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Maps a HyperTalk text style string to the Mac-style style-flag byte.
    /// Handles "plain", "bold", "italic", "underline" and comma-separated combos.
    /// </summary>
    private static byte ParseTextStyleFlags(string styleStr)
    {
        if (string.IsNullOrWhiteSpace(styleStr)) return 0;
        if (string.Equals(styleStr.Trim(), "plain", StringComparison.OrdinalIgnoreCase)) return 0;
        byte flags = 0;
        foreach (var token in styleStr.Split(','))
        {
            flags |= token.Trim().ToLowerInvariant() switch
            {
                "bold"      => 0x01,
                "italic"    => 0x02,
                "underline" => 0x04,
                "outline"   => 0x08,
                "shadow"    => 0x10,
                "condense"  => 0x20,
                "extend"    => 0x40,
                _           => 0x00
            };
        }
        return flags;
    }

    /// <summary>
    /// Returns true if any field on the given card (or its background) contains
    /// <paramref name="searchText"/> (case-insensitive).
    /// If <paramref name="fieldName"/> is specified, only that field is searched.
    /// </summary>
    private static bool CardContainsText(
        string searchText, CardBlock card, BackgroundBlock? bg, string? fieldName)
    {
        bool Match(string? text) =>
            text != null && text.Contains(searchText, StringComparison.OrdinalIgnoreCase);

        // Card parts
        foreach (var part in card.Parts)
        {
            if (!part.IsField) continue;
            if (fieldName != null &&
                !string.Equals(part.Name, fieldName, StringComparison.OrdinalIgnoreCase)) continue;
            var content = card.PartContents.FirstOrDefault(pc => pc.PartId == part.PartId);
            if (Match(content?.Text)) return true;
        }

        if (bg != null)
        {
            // Card-specific bg content
            var cardBgContent = card.PartContents
                .Where(pc => pc.PartId < 0)
                .ToDictionary(pc => (short)(-pc.PartId), pc => pc.Text);

            foreach (var part in bg.Parts)
            {
                if (!part.IsField) continue;
                if (fieldName != null &&
                    !string.Equals(part.Name, fieldName, StringComparison.OrdinalIgnoreCase)) continue;
                string? text = cardBgContent.TryGetValue(part.PartId, out var ct)
                    ? ct
                    : bg.PartContents.FirstOrDefault(pc => pc.PartId == part.PartId)?.Text;
                if (Match(text)) return true;
            }
        }
        return false;
    }

    // ── Public navigation helpers (used by MainWindow for cross-stack navigation) ──

    /// <summary>Navigate to a 1-based card number.</summary>
    public void GoToCardNumber(int cardNumber)
    {
        if (_stack == null || _cardOrder.Count == 0) return;
        NavigateTo(Math.Clamp(cardNumber - 1, 0, _cardOrder.Count - 1));
    }

    /// <summary>Navigate to a card by name.</summary>
    public void GoToCardByName(string name)
    {
        if (_stack == null || _cardOrder.Count == 0) return;
        for (int i = 0; i < _cardOrder.Count; i++)
        {
            var card = _stack.Cards.FirstOrDefault(c => c.Header.Id == _cardOrder[i]);
            if (card != null && string.Equals(card.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                NavigateTo(i);
                return;
            }
        }
    }
}
