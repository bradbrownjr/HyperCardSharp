using System.Buffers.Binary;

namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Lightweight metadata about a HyperCard stack extracted from a container.
/// Card count and dimensions are read directly from the STAK header
/// without a full parse.
/// </summary>
public sealed class StackEntry
{
    public string Name { get; init; } = "";
    public byte[] Data { get; init; } = [];
    /// <summary>Raw Mac resource fork bytes for this stack file, if available.</summary>
    public byte[]? ResourceFork { get; init; }
    public long SizeBytes => Data.Length;
    public int CardCount { get; init; }
    public short CardWidth { get; init; }
    public short CardHeight { get; init; }

    /// <summary>
    /// Human-readable size (e.g. "126K", "2,457K").
    /// </summary>
    public string SizeText => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        _ => $"{(SizeBytes + 512) / 1024:N0}K"
    };

    /// <summary>
    /// Resolution string (e.g. "512×342").
    /// </summary>
    public string Resolution => CardWidth > 0 && CardHeight > 0
        ? $"{CardWidth}\u00d7{CardHeight}"
        : "";

    /// <summary>
    /// Create a StackEntry from raw stack bytes, reading STAK header metadata.
    /// </summary>
    public static StackEntry FromRaw(string name, byte[] data)
    {
        int cardCount = 0;
        short cardWidth = 512;
        short cardHeight = 342;

        // Read STAK header fields if data is large enough.
        // Card count at +0x28, card height at +0x1B8, card width at +0x1BA.
        if (data.Length > 0x1BC)
        {
            try
            {
                cardCount = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x28, 4));
                cardHeight = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(0x1B8, 2));
                cardWidth = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(0x1BA, 2));

                // Sanity checks — bad data should fall back to defaults
                if (cardCount < 0 || cardCount > 100_000) cardCount = 0;
                if (cardWidth <= 0 || cardWidth > 4096) cardWidth = 512;
                if (cardHeight <= 0 || cardHeight > 4096) cardHeight = 342;
            }
            catch
            {
                // Graceful degradation for malformed data
            }
        }

        return new StackEntry
        {
            Name = name,
            Data = data,
            CardCount = cardCount,
            CardWidth = cardWidth,
            CardHeight = cardHeight,
        };
    }

    /// <summary>
    /// Like <see cref="FromRaw(string, byte[])"/>, but also attaches a resource fork.
    /// </summary>
    public static StackEntry FromRaw(string name, byte[] data, byte[]? resourceFork)
    {
        int cardCount = 0;
        short cardWidth = 512;
        short cardHeight = 342;

        if (data.Length > 0x1BC)
        {
            try
            {
                cardCount = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x28, 4));
                cardHeight = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(0x1B8, 2));
                cardWidth = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(0x1BA, 2));
                if (cardCount < 0 || cardCount > 100_000) cardCount = 0;
                if (cardWidth <= 0 || cardWidth > 4096) cardWidth = 512;
                if (cardHeight <= 0 || cardHeight > 4096) cardHeight = 342;
            }
            catch { }
        }

        return new StackEntry
        {
            Name = name,
            Data = data,
            ResourceFork = resourceFork,
            CardCount = cardCount,
            CardWidth = cardWidth,
            CardHeight = cardHeight,
        };
    }
}
