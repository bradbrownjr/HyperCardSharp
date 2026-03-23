using HyperCardSharp.HyperTalk.Ast;
using HyperCardSharp.HyperTalk.Interpreter;
using HyperCardSharp.HyperTalk.Lexer;
using HyperCardSharp.HyperTalk.Parser;

namespace HyperCardSharp.HyperTalk.MessagePassing;

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
    /// Dispatches a message to a script. Returns true if a matching handler was found and executed.
    /// The script text is lexed and parsed once, then cached by content hash.
    /// </summary>
    public bool DispatchMessage(
        string handlerName,
        string scriptText,
        HyperTalkInterpreter interpreter,
        HyperTalkValue[]? args = null)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            return false;

        ScriptNode script;
        try
        {
            script = GetOrParseScript(scriptText);
        }
        catch (Exception ex)
        {
            Log($"[HyperTalk] Parse error in script: {ex.Message}");
            return false;
        }

        // Check whether the handler exists
        bool found = Array.Exists(script.Handlers,
            h => string.Equals(h.Name, handlerName, StringComparison.OrdinalIgnoreCase));

        if (!found)
            return false;

        try
        {
            interpreter.ExecuteHandler(script, handlerName, args ?? []);
        }
        catch (Exception ex)
        {
            Log($"[HyperTalk] Runtime error in '{handlerName}': {ex.Message}");
        }

        return true;
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
