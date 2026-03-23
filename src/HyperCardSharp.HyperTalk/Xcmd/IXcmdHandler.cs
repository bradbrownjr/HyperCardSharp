using HyperCardSharp.HyperTalk.Interpreter;

namespace HyperCardSharp.HyperTalk.Xcmd;

/// <summary>
/// Interface for external command (XCMD) and external function (XFCN) handlers.
/// </summary>
public interface IXcmdHandler
{
    string Name { get; }
    HyperTalkValue Execute(HyperTalkValue[] args, HyperTalkInterpreter interpreter);
}
