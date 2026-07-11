using HyperCardSharp.Core.Parts;
using HyperCardSharp.Rendering.Tests.Fixtures;
using SkiaSharp;

namespace HyperCardSharp.Rendering.Tests;

/// <summary>
/// Smoke tests for <see cref="PartShowcaseFixture"/>: confirms the fixture builds a
/// well-formed stack and renders without throwing. Pixel-level golden-image comparison
/// is out of scope here (roadmap task B7 owns that harness) -- this only guards against
/// the fixture itself regressing (e.g. a future PartStyle addition not being wired in).
/// </summary>
public class PartShowcaseFixtureTests
{
    [Fact]
    public void BuildStack_ContainsEveryButtonStyleAsNormalAndHilitedPair()
    {
        var stack = PartShowcaseFixture.BuildStack();
        var card = Assert.Single(stack.Cards);

        var buttons = card.Parts.Where(p => p.IsButton).ToList();
        Assert.Equal(PartShowcaseFixture.AllButtonStyles.Length * 2, buttons.Count);

        foreach (var style in PartShowcaseFixture.AllButtonStyles)
        {
            var pair = buttons.Where(b => b.Style == style).ToList();
            Assert.Equal(2, pair.Count);
            Assert.Contains(pair, b => b.HiliteState);
            Assert.Contains(pair, b => !b.HiliteState);
        }
    }

    [Fact]
    public void BuildStack_ContainsRepresentativeFieldsWithContent()
    {
        var stack = PartShowcaseFixture.BuildStack();
        var card = Assert.Single(stack.Cards);

        var fields = card.Parts.Where(p => p.IsField).ToList();
        Assert.Equal(PartShowcaseFixture.FieldStyles.Length, fields.Count);

        foreach (var field in fields)
        {
            var content = card.PartContents.FirstOrDefault(c => c.PartId == (short)(-field.PartId));
            Assert.NotNull(content);
            Assert.False(string.IsNullOrEmpty(content!.Text));
        }
    }

    [Fact]
    public void RenderShowcaseCard_DoesNotThrow_AndProducesExpectedCardSize()
    {
        using var bitmap = PartShowcaseFixture.RenderShowcaseCard();

        Assert.Equal(PartShowcaseFixture.CardWidth, bitmap.Width);
        Assert.Equal(PartShowcaseFixture.CardHeight, bitmap.Height);
    }

    [Fact]
    public void RenderShowcaseCard_BlackAndWhiteMode_EnsuresPurity_OnlyPureBlackAndWhitePixels()
    {
        using var bitmap = PartShowcaseFixture.RenderShowcaseCard(RenderMode.BlackAndWhite);

        // Verify bitmap is valid and non-empty
        Assert.Equal(PartShowcaseFixture.CardWidth, bitmap.Width);
        Assert.Equal(PartShowcaseFixture.CardHeight, bitmap.Height);
        Assert.Equal(SKColorType.Bgra8888, bitmap.ColorType);

        // Every pixel must be pure black (R=G=B=0) or pure white (R=G=B=255).
        // A gray pixel means text/glyph antialiasing leaked into the 1-bit output.
        int purityViolations = 0;
        var samples = new List<string>();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                bool isPureBlack = pixel.Red == 0 && pixel.Green == 0 && pixel.Blue == 0;
                bool isPureWhite = pixel.Red == 255 && pixel.Green == 255 && pixel.Blue == 255;

                if (!isPureBlack && !isPureWhite)
                {
                    purityViolations++;
                    if (samples.Count < 5)
                        samples.Add($"({x},{y})=#{pixel.Red:X2}{pixel.Green:X2}{pixel.Blue:X2}");
                }
            }
        }

        Assert.True(purityViolations == 0,
            $"Found {purityViolations} non-pure pixel(s); first few: {string.Join(", ", samples)}");
    }
}
