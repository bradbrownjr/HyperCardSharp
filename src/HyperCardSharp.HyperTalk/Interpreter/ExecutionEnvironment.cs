namespace HyperCardSharp.HyperTalk.Interpreter;

/// <summary>
/// Variable scoping for a single HyperTalk handler invocation.
/// Globals are shared across all invocations (static dictionary).
/// </summary>
public class ExecutionEnvironment
{
    private readonly Dictionary<string, HyperTalkValue> _locals =
        new(StringComparer.OrdinalIgnoreCase);

    // Names declared 'global' in this scope — reads/writes go to _globals
    private readonly HashSet<string> _declaredGlobals =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, HyperTalkValue> _globals =
        new(StringComparer.OrdinalIgnoreCase);

    public HyperTalkValue It { get; set; } = HyperTalkValue.Empty;

    /// <summary>Gets a variable, checking locals first then globals.</summary>
    public HyperTalkValue Get(string name)
    {
        if (_declaredGlobals.Contains(name))
            return _globals.TryGetValue(name, out var gv) ? gv : HyperTalkValue.Empty;
        if (_locals.TryGetValue(name, out var lv))
            return lv;
        // Fall through to globals even without declaration (read-only access)
        if (_globals.TryGetValue(name, out var fallback))
            return fallback;
        return HyperTalkValue.Empty;
    }

    /// <summary>Sets a local variable (or global if declared via 'global').</summary>
    public void SetLocal(string name, HyperTalkValue value)
    {
        if (_declaredGlobals.Contains(name))
            _globals[name] = value;
        else
            _locals[name] = value;
    }

    /// <summary>Unconditionally sets a global variable.</summary>
    public void SetGlobal(string name, HyperTalkValue value) =>
        _globals[name] = value;

    /// <summary>Marks a name as global in this scope, so reads/writes use _globals.</summary>
    public void DeclareGlobal(string name) =>
        _declaredGlobals.Add(name);

    /// <summary>Clears all global variables (useful for test isolation).</summary>
    public static void ClearGlobals() => _globals.Clear();
}
