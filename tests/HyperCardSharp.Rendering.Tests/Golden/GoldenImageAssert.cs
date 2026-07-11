using System.Runtime.CompilerServices;
using SkiaSharp;

namespace HyperCardSharp.Rendering.Tests.Golden;

/// <summary>
/// Byte-exact golden-image comparison helper (roadmap task B7).
///
/// Comparison method: goldens are committed as PNG files (so they are viewable/diffable
/// in a normal git tool), but the actual assertion decodes the golden PNG back to raw
/// BGRA pixel bytes and compares those against the freshly-rendered bitmap's raw pixel
/// bytes. This sidesteps any PNG re-encoding differences (encoder version, compression
/// level, filter choice) that could make byte-comparing the encoded PNG streams
/// themselves spuriously flaky -- raw decoded pixels are the actual thing rendering
/// fidelity cares about.
///
/// Regeneration: set the environment variable UPDATE_GOLDENS=1 before running
/// `dotnet test`. Every AssertMatches call then (over)writes its golden file from the
/// freshly-rendered bitmap instead of comparing, and always passes. Run once, inspect
/// the resulting PNGs, then commit them.
/// </summary>
public static class GoldenImageAssert
{
    private const string UpdateGoldensEnvVar = "UPDATE_GOLDENS";

    /// <summary>
    /// Asserts that <paramref name="actual"/> is pixel-identical (tolerance 0) to the
    /// committed golden image named <paramref name="fixtureName"/>.png in this Golden/
    /// directory. In regeneration mode (UPDATE_GOLDENS=1) writes the golden instead.
    /// </summary>
    public static void AssertMatches(string fixtureName, SKBitmap actual)
    {
        var path = Path.Combine(GoldenDirectory, fixtureName + ".png");

        if (Environment.GetEnvironmentVariable(UpdateGoldensEnvVar) == "1")
        {
            Directory.CreateDirectory(GoldenDirectory);
            using var encoded = actual.Encode(SKEncodedImageFormat.Png, 100);
            using var fs = File.Create(path);
            encoded.SaveTo(fs);
            return;
        }

        Assert.True(File.Exists(path),
            $"Golden image not found: {path}. Run with UPDATE_GOLDENS=1 to generate it.");

        using var golden = SKBitmap.Decode(path);
        Assert.True(golden != null, $"Failed to decode golden image: {path}");
        Assert.Equal(golden!.Width, actual.Width);
        Assert.Equal(golden.Height, actual.Height);
        Assert.Equal(golden.ColorType, actual.ColorType);

        var goldenPixels = golden.Bytes;
        var actualPixels = actual.Bytes;

        if (goldenPixels.AsSpan().SequenceEqual(actualPixels))
            return;

        int firstDiff = -1;
        int diffCount = 0;
        for (int i = 0; i < goldenPixels.Length; i++)
        {
            if (goldenPixels[i] != actualPixels[i])
            {
                if (firstDiff < 0) firstDiff = i;
                diffCount++;
            }
        }

        int bytesPerPixel = 4; // Bgra8888
        int firstPixelIndex = firstDiff / bytesPerPixel;
        int firstX = actual.Width > 0 ? firstPixelIndex % actual.Width : 0;
        int firstY = actual.Width > 0 ? firstPixelIndex / actual.Width : 0;

        Assert.Fail(
            $"Rendered bitmap does not match golden '{fixtureName}.png': " +
            $"{diffCount} differing byte(s) out of {goldenPixels.Length}, " +
            $"first difference at pixel ({firstX},{firstY}). " +
            "If this change is intentional, regenerate goldens with UPDATE_GOLDENS=1 and " +
            "visually verify the new image before committing.");
    }

    // The Golden/ directory is simply where this source file lives -- resolved via the
    // compiler-supplied absolute path of this file rather than AppContext.BaseDirectory
    // (which points at the build output dir and varies by configuration/RID), so golden
    // files are always found relative to the checked-out repo regardless of how/where
    // the test binaries were built.
    private static string GoldenDirectory => Path.GetDirectoryName(SourceFilePath())!;

    private static string SourceFilePath([CallerFilePath] string path = "") => path;
}
