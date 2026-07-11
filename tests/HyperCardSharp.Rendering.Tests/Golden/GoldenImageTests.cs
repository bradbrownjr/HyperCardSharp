using System.Runtime.InteropServices;
using HyperCardSharp.Rendering.Tests.Fixtures;
using SkiaSharp;

namespace HyperCardSharp.Rendering.Tests.Golden;

/// <summary>
/// Byte-exact golden-image regression tests (roadmap task B7). Locks in rendering
/// fidelity for synthetic in-memory fixtures so future rendering changes cannot
/// silently regress pixel output.
///
/// Canonical platform: Linux only. SkiaSharp's glyph rasterizer differs across host
/// OSes (FreeType on Linux vs. CoreText on macOS vs. DirectWrite on Windows), so a
/// golden generated on one OS will not byte-match a render on another even for
/// identical embedded font data. These tests are gated with [SkippableFact] +
/// Skip.IfNot(Linux) and are SKIPPED (not failed) on the windows-latest/macos-latest
/// CI jobs; every other test in the suite still builds and runs there. The
/// ubuntu-latest CI job is the canonical enforcement point for this suite.
///
/// Determinism within Linux is further guarded by forcing
/// <see cref="FontMapper.UseEmbeddedFontsOnly"/> for the duration of these tests, so
/// output does not depend on which fonts happen to be installed system-wide on the
/// machine that generated the goldens vs. the machine running the assertions.
///
/// Regeneration: run `UPDATE_GOLDENS=1 dotnet test` to (re)write every golden from a
/// fresh render, then re-run `dotnet test` normally to confirm they pass, and inspect
/// the resulting PNGs before committing.
/// </summary>
public class GoldenImageTests : IDisposable
{
    public GoldenImageTests()
    {
        FontMapper.UseEmbeddedFontsOnly = true;
    }

    public void Dispose()
    {
        FontMapper.UseEmbeddedFontsOnly = false;
    }

    [SkippableFact]
    public void PartShowcase_MatchesGolden()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Golden images are only byte-deterministic on Linux (see class remarks).");

        using var bitmap = PartShowcaseFixture.RenderShowcaseCard(RenderMode.BlackAndWhite);
        AssertNonTrivial(bitmap);
        GoldenImageAssert.AssertMatches("PartShowcase", bitmap);
    }

    [SkippableFact]
    public void StyledText_MatchesGolden()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Golden images are only byte-deterministic on Linux (see class remarks).");

        using var bitmap = StyledTextFixture.RenderStyledTextCard(RenderMode.BlackAndWhite);
        AssertNonTrivial(bitmap);
        GoldenImageAssert.AssertMatches("StyledText", bitmap);
    }

    /// <summary>
    /// Guards against a golden silently "matching" a blank or fully-filled canvas
    /// (e.g. a rendering regression that clips everything away but happens to still
    /// byte-match an equally-broken committed golden): a real card render must
    /// contain both black and white pixels.
    /// </summary>
    private static void AssertNonTrivial(SKBitmap bitmap)
    {
        bool sawBlack = false, sawWhite = false;
        for (int y = 0; y < bitmap.Height && !(sawBlack && sawWhite); y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red == 0 && p.Green == 0 && p.Blue == 0) sawBlack = true;
                else if (p.Red == 255 && p.Green == 255 && p.Blue == 255) sawWhite = true;
                if (sawBlack && sawWhite) break;
            }
        }

        Assert.True(sawBlack, "Rendered bitmap contains no black pixels (looks blank).");
        Assert.True(sawWhite, "Rendered bitmap contains no white pixels (looks fully filled).");
    }
}
