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
    /// Raised when a HyperTalk <c>visual effect</c> / <c>go</c> pair fires a named transition.
    /// from: old card bitmap (caller must dispose); to: new card bitmap (same as CurrentBitmap).
    /// </summary>
    public event Action<SKBitmap, SKBitmap, string, string?, string?>? TransitionRequested;

    // Pending visual effect queued by the HyperTalk interpreter (cleared on next navigation).
    private (string Effect, string? Speed, string? Direction)? _pendingEffect;

    public bool IsLoaded => _stack != null;

    public int CardWidth => _stack?.StackHeader.CardWidth ?? 640;
    public int CardHeight => _stack?.StackHeader.CardHeight ?? 400;

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
    }

    public void HandleCardClick(float cardX, float cardY)
    {
        var card = CurrentCard();
        if (card == null) return;
        var bg = CurrentBackground(card);

        var part = HitTest(cardX, cardY, card, bg);

        // HyperCard message hierarchy: button → card → background → stack
        // If the button has a mouseUp handler, run it. Otherwise, climb the hierarchy.
        if (part != null && !string.IsNullOrWhiteSpace(part.Script))
        {
            if (_dispatcher.DispatchMessage("mouseUp", part.Script, _interpreter))
                return;
        }

        // Card script
        if (!string.IsNullOrWhiteSpace(card.Script))
        {
            if (_dispatcher.DispatchMessage("mouseUp", card.Script, _interpreter))
                return;
        }

        // Background script
        if (bg != null && !string.IsNullOrWhiteSpace(bg.Script))
        {
            if (_dispatcher.DispatchMessage("mouseUp", bg.Script, _interpreter))
                return;
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

    public void LoadStack(byte[] fileData, string fileName, string? stackName = null)
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
            _stack = parser.Parse(fileData);
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
    /// Core navigation primitive.  Captures any pending visual effect and fires
    /// <see cref="TransitionRequested"/> after switching cards so the UI can animate.
    /// </summary>
    private void NavigateTo(int newIndex)
    {
        if (_stack == null || _cardOrder.Count == 0) return;
        newIndex = Math.Clamp(newIndex, 0, _cardOrder.Count - 1);

        var effect = _pendingEffect;
        _pendingEffect = null;

        if (effect != null && CurrentBitmap != null && TransitionRequested != null)
        {
            // Clone the old bitmap — the renderer will dispose the original on the next render.
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
            _pendingEffect = null; // discard any stale effect if no subscriber
            CurrentCardIndex = newIndex;
            RenderCurrentCard();
        }
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

}
