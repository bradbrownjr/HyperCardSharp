namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Thrown when stack data is malformed in a way that prevents parsing.
/// Carries a byte offset so the message points at the specific corruption,
/// instead of leaking a generic BCL exception (e.g. ArgumentOutOfRangeException)
/// with no context about where in the file parsing failed.
/// </summary>
public class StackFormatException : Exception
{
    public long Offset { get; }

    public StackFormatException(string message, long offset)
        : base($"{message} (at offset 0x{offset:X})")
    {
        Offset = offset;
    }

    public StackFormatException(string message, long offset, Exception innerException)
        : base($"{message} (at offset 0x{offset:X})", innerException)
    {
        Offset = offset;
    }
}
