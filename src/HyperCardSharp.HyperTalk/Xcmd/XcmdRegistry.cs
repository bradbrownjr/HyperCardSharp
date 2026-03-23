using HyperCardSharp.HyperTalk.Interpreter;

namespace HyperCardSharp.HyperTalk.Xcmd;

/// <summary>
/// Registry for XCMD/XFCN handlers. Handlers are looked up by name (case-insensitive).
/// </summary>
public class XcmdRegistry
{
    private readonly Dictionary<string, IXcmdHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Action<string> _log;

    public XcmdRegistry(Action<string> log) => _log = log;

    public void Register(IXcmdHandler handler) =>
        _handlers[handler.Name] = handler;

    /// <summary>
    /// Tries to execute a named XCMD/XFCN.
    /// Returns the result value, or null if no handler is registered.
    /// </summary>
    public HyperTalkValue? TryExecute(string name, HyperTalkValue[] args, HyperTalkInterpreter interpreter)
    {
        if (!_handlers.TryGetValue(name, out var handler))
            return null;

        try
        {
            return handler.Execute(args, interpreter);
        }
        catch (Exception ex)
        {
            _log($"XCMD '{name}' threw: {ex.Message}");
            return HyperTalkValue.Empty;
        }
    }
}
