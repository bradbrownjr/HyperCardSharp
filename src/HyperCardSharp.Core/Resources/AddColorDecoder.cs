namespace HyperCardSharp.Core.Resources;

/// <summary>
/// Decodes AddColor (HCcd/HCbg) resources from HyperCard 2.x stacks.
/// AddColor was an XCMD extension that allowed color overlays to be stored in the
/// resource fork alongside standard B&W stacks.
///
/// Format reference: https://hypercard.org/addcolor_resource_format/
///
/// TODO: verify against real stacks with AddColor resources.
/// This is a stub — actual decoding is deferred pending resource fork extraction
/// and sample stacks with HCcd/HCbg data.
/// </summary>
public static class AddColorDecoder
{
    /// <summary>
    /// Represents a single color region from an HCcd or HCbg resource.
    /// </summary>
    public class ColorRegion
    {
        public short Top    { get; init; }
        public short Left   { get; init; }
        public short Bottom { get; init; }
        public short Right  { get; init; }

        /// <summary>ARGB color packed as 0xAARRGGBB.</summary>
        public uint Color { get; init; }
    }

    /// <summary>
    /// Attempts to decode an HCcd or HCbg resource blob.
    /// Returns an empty list if the format is unrecognized or not yet implemented.
    /// </summary>
    public static IReadOnlyList<ColorRegion> Decode(byte[] resourceData)
    {
        // TODO: verify against real stack — implement HCcd/HCbg record parsing
        return Array.Empty<ColorRegion>();
    }
}
