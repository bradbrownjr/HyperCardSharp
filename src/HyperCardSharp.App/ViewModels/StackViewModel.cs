using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperCardSharp.Core.Parts;
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
            // Field text mutation deferred (model uses init-only properties)
            _interpreter.LogMessage($"[HyperTalk] put into field \"{fieldSpec}\" → deferred");
        };
        _interpreter.GetButtonHilite = _ => null;   // read-only for now
        _interpreter.SetButtonHilite = (_, _) => { };
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
            _interpreter.LogMessage($"[HyperTalk] set visible of '{partSpec}' to {visible} — deferred");

        _interpreter.GetScriptForTarget = (msg, targetStr) =>
        {
            var lower = targetStr.ToLowerInvariant().Trim();
            var card = CurrentCard();
            var bg   = card != null ? CurrentBackground(card) : null;
            if (lower == "this card" || lower == "card")
                return card?.Script;
            if (lower is "this background" or "this bkgd" or "background" or "bkgd")
                return bg?.Script;
            return null; // button/field by name not resolved yet
        };

        _interpreter.PlaySound = soundName =>
        {
            // TODO: look up "snd " resource by name and play it via a media service
            _interpreter.LogMessage($"[HyperTalk] play '{soundName}' — sound playback not yet implemented");
        };

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
        StatusText = $"{fileInfo}Card {CurrentCardIndex + 1}/{TotalCards}{nameInfo}{partInfo}";
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
