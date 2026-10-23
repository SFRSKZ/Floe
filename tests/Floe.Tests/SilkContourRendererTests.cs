using Floe.Core.Interop;
using Floe.Core.Visualization;

namespace Floe.Tests;

public sealed class SilkContourRendererTests
{
    [Fact]
    public void SilenceAndIsolatedOnsetFlagsDoNotCreateAStandaloneAnimation()
    {
        var renderer = Create();
        var silence = new SpectrumFrame(24) { Kick = 1, Snare = 1, Hat = 1 };
        Advance(renderer, silence, 90);
        Assert.True(renderer.IsEmpty);
        Assert.All(renderer.Pixels, pixel => Assert.Equal(0, pixel));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MusicReleasesIntoCompleteTransparencyWhenAudioOrActivationStops(bool active)
    {
        var renderer = Create();
        var music = Music();
        Advance(renderer, music, 60);
        long before = Brightness(renderer);
        Assert.True(before > 0);
        var lastFrame = active ? new SpectrumFrame(24) : music;
        Advance(renderer, lastFrame, 12, active);
        Assert.InRange(Brightness(renderer), 1, before - 1);
        Advance(renderer, lastFrame, 60, active);
        Assert.True(renderer.IsEmpty);
        Assert.All(renderer.Pixels, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void EqualLoudnessWithDifferentSpectraChangesTheSurface()
    {
        var low = Music();
        var high = Music();
        Array.Clear(low.Bands);
        Array.Clear(high.Bands);
        low.Bands[3] = high.Bands[20] = 1;
        var a = Create();
        var b = Create();
        Advance(a, low, 30);
        Advance(b, high, 30);
        Assert.False(a.Pixels.SequenceEqual(b.Pixels));
        Assert.True(a.Pixels.Zip(b.Pixels).Count(pair => pair.First != pair.Second) > a.Width);
    }

    [Theory]
    [InlineData(ContourQuality.Full)]
    [InlineData(ContourQuality.Balanced)]
    [InlineData(ContourQuality.Lite)]
    public void SurfaceIsContinuousInsideBoundsAndItsPixelsArePremultiplied(ContourQuality quality)
    {
        var renderer = Create(quality);
        Advance(renderer, Music(), 40);
        Assert.False(renderer.IsEmpty);
        Assert.All(renderer.Pixels.Take(renderer.Width), pixel => Assert.Equal(0, pixel));
        for (int x = 0; x < renderer.Width; x++)
        {
            bool lit = false;
            for (int y = 0; y < renderer.Height; y++)
            {
                uint p = (uint)renderer.Pixels[(y * renderer.Width) + x];
                uint alpha = p >> 24;
                Assert.True(((p >> 16) & 255) <= alpha && ((p >> 8) & 255) <= alpha && (p & 255) <= alpha);
                lit |= alpha > 0;
            }

            Assert.True(lit, $"No continuous surface at column {x}");
        }
    }

    [Fact]
    public void InvalidAudioCannotPoisonLaterFrames()
    {
        var renderer = Create();
        Advance(renderer, Music(), 10);
        var bad = new SpectrumFrame(3)
        {
            Level = float.NaN, Bass = float.PositiveInfinity, Mid = -1,
            Treble = float.NegativeInfinity, Kick = float.NaN, Snare = float.PositiveInfinity,
        };
        bad.Bands[0] = float.NaN;
        bad.Bands[1] = float.NegativeInfinity;
        bad.Bands[2] = -20;
        renderer.Step(bad, double.NaN);
        renderer.Step(bad, double.PositiveInfinity);
        renderer.Step(bad, -1);
        Advance(renderer, bad, 70);
        Assert.True(renderer.IsEmpty);
        Advance(renderer, Music(), 20);
        Assert.False(renderer.IsEmpty);
        Assert.True(Brightness(renderer) > 0);
    }

    [Fact]
    public void PaletteChangesApplyWithoutRestartingTheRenderer()
    {
        var renderer = Create();
        var music = Music();
        renderer.SetPalette(new[] { 0xFF2000, 0xFF2000 });
        Advance(renderer, music, 30);
        long red = ChannelTotal(renderer, 16), blue = ChannelTotal(renderer, 0);
        Assert.True(red > blue * 2);
        renderer.SetPalette(new[] { 0x0020FF, 0x0020FF });
        Advance(renderer, music, 15);
        Assert.True(ChannelTotal(renderer, 0) > ChannelTotal(renderer, 16) * 2);
    }

    [Fact]
    public void ResizingReusesStableBuffersAndCapsMalformedBounds()
    {
        var renderer = Create();
        var original = renderer.Pixels;
        renderer.Resize(640, 76);
        Assert.Same(original, renderer.Pixels);
        renderer.Resize(3840, 76, ContourQuality.Lite);
        Assert.Equal(1920, renderer.Width);
        Assert.Equal(38, renderer.Height);
        Advance(renderer, Music(), 5);
        Assert.False(renderer.IsEmpty);
        renderer.Resize(double.MaxValue, double.MaxValue);
        Assert.InRange(renderer.Pixels.Length, 1, 4096 * 256);
        renderer.Step(Music(), 1.0 / 30);
        renderer.Resize(double.NaN, 76);
        Assert.Empty(renderer.Pixels);
        renderer.Step(Music(), 1.0 / 30);
        renderer.Resize(640, 76);
        renderer.Step(Music(), 1.0 / 30);
        Assert.False(renderer.IsEmpty);
    }

    [Fact]
    public void ResetRemovesHistoryAndReplaysDeterministically()
    {
        var a = Create();
        var b = Create();
        var music = Music();
        Advance(a, music, 100);
        var pixels = a.Pixels;
        a.Reset();
        Assert.Same(pixels, a.Pixels);
        Advance(a, music, 40);
        Advance(b, music, 40);
        Assert.Equal(b.Pixels, a.Pixels);
    }

    [Fact]
    public void SteadyRenderingDoesNotAllocatePerFrame()
    {
        var renderer = Create();
        var music = Music();
        Advance(renderer, music, 40);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Advance(renderer, music, 20);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static SilkContourRenderer Create(ContourQuality quality = ContourQuality.Full)
    {
        var renderer = new SilkContourRenderer();
        renderer.Resize(640, 76, quality);
        return renderer;
    }

    private static SpectrumFrame Music()
    {
        var frame = new SpectrumFrame(24) { Level = .7f, Bass = .65f, Mid = .5f, Treble = .4f };
        for (int i = 0; i < frame.Bands.Length; i++) frame.Bands[i] = .2f + ((i % 5) * .15f);
        return frame;
    }

    private static void Advance(SilkContourRenderer renderer, SpectrumFrame frame, int count, bool active = true)
    {
        for (int i = 0; i < count; i++) renderer.Step(frame, 1.0 / 30, active);
    }

    private static long Brightness(SilkContourRenderer renderer) => renderer.Pixels.Sum(p => (long)((uint)p >> 24));

    private static long ChannelTotal(SilkContourRenderer renderer, int shift) => renderer.Pixels.Sum(p => (long)(((uint)p >> shift) & 255));
}
