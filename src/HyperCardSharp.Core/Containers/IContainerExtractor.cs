namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Extracts inner file data from a container format.
/// Returns null if the container doesn't contain a usable HyperCard stack.
/// </summary>
public interface IContainerExtractor
{
    bool CanHandle(ReadOnlySpan<byte> data);
    byte[]? Extract(byte[] data);  // Returns first extracted file that looks like a stack, or null
}
