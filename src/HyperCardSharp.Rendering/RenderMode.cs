namespace HyperCardSharp.Rendering;

public enum RenderMode
{
    /// <summary>Authentic 1-bit black-and-white rendering from WOBA bitmaps.</summary>
    BlackAndWhite,

    /// <summary>Color rendering using AddColor (HCcd/HCbg) overlay data when available.</summary>
    Color
}
