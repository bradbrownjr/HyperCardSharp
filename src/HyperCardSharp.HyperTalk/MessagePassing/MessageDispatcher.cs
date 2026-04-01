using HyperCardSharp.HyperTalk.Ast;
using HyperCardSharp.HyperTalk.Interpreter;
using HyperCardSharp.HyperTalk.Lexer;
using HyperCardSharp.HyperTalk.Parser;

namespace HyperCardSharp.HyperTalk.MessagePassing;

/// <summary>
/// Result returned by <see cref="MessageDispatcher.DispatchMessage"/>.
/// </summary>
public enum DispatchResult
{
    /// <summary>No matching handler was found.</summary>
    NotFound,
    /// <summary>A handler was found and ran to completion without calling <c>pass</c>.</summary>
    Handled,
    /// <summary>A handler was found but it ended with <c>pass</c>, signalling the caller to continue up the hierarchy.</summary>
    Passed,
}

/// <summary>
/// Parses HyperTalk scripts, caches the resulting ASTs, and dispatches messages (handler calls).
/// </summary>
public class MessageDispatcher
{
    private readonly HyperTalkParser _parser = new();
    private readonly HyperTalkLexer _lexer = new();
    private readonly Dictionary<string, ScriptNode> _scriptCache = new();

    private Action<string> Log { get; }

    public MessageDispatcher(Action<string>? log = null)
    {
        Log = log ?? (_ => {});
        _parser.OnWarning = w => Log($"[HyperTalk parse warning] {w}");
    }

    /// <summary>
    /// Dispatches a message to a script.
    /// Returns <see cref="DispatchResult.Handled"/> if a handler ran to completion,
    /// <see cref="DispatchResult.Passed"/> if the handler called <c>pass</c>,
    /// or <see cref="DispatchResult.NotFound"/> if no matching handler exists.
    /// The script text is lexed and parsed once, then cached by content hash.
    /// </summary>
    public DispatchResult DispatchMessage(
        string handlerName,
        string scriptText,
        HyperTalkInterpreter interpreter,
        HyperTalkValue[]? args = null)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            return DispatchResult.NotFound;

        ScriptNode script;
        try
        {
            script = GetOrParseScript(scriptText);
        }
        catch (Exception ex)
        {
            Log($"[HyperTalk] Parse error in script: {ex.Message}");
            return DispatchResult.NotFound;
        }

        // Check whether the handler exists
        bool found = Array.Exists(script.Handlers,
            h => string.Equals(h.Name, handlerName, StringComparison.OrdinalIgnoreCase));

        if (!found)
            return DispatchResult.NotFound;

        ExecutionResult execResult;
        try
        {
            execResult = interpreter.ExecuteHandler(script, handlerName, args ?? []);
        }
        catch (Exception ex)
        {
            Log($"[HyperTalk] Runtime error in '{handlerName}': {ex.Message}");
            return DispatchResult.Handled;
        }

        return execResult == ExecutionResult.Pass ? DispatchResult.Passed : DispatchResult.Handled;
    }

    private ScriptNode GetOrParseScript(string scriptText)
    {
        // Use the script text itself as the cache key (most scripts are short)
        // For large scripts a hash would be more efficient; keeping it simple for now.
        if (_scriptCache.TryGetValue(scriptText, out var cached))
            return cached;

        var tokens = _lexer.Tokenize(scriptText);
        var script = _parser.Parse(tokens);
        _scriptCache[scriptText] = script;
        return script;
    }

    /// <summary>Clears the script parse cache (call when scripts are reloaded).</summary>
    public void ClearCache() => _scriptCache.Clear();
}
