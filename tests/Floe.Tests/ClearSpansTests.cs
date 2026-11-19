using Floe.Core.Services;

namespace Floe.Tests;

public class ClearSpansTests
{
    [Fact]
    public void OverlappingSpansBecomeOne()
    {
        // a crowded bar: the icon run reaches into the right hand group
        Assert.Equal(new[] { 0f, 1f }, ClearSpans.Merge(new[] { 0f, .7f, .6f, 1f }));
    }

    [Fact]
    public void UnsortedSpansAreSortedAndKeptApart()
    {
        Assert.Equal(new[] { 0f, .2f, .5f, .6f, .8f, 1f }, ClearSpans.Merge(new[] { .8f, 1f, 0f, .2f, .5f, .6f }));
    }

    [Fact]
    public void ContainedAndTouchingSpansMerge()
    {
        Assert.Equal(new[] { .1f, .9f }, ClearSpans.Merge(new[] { .1f, .5f, .2f, .3f, .5f, .9f }));
    }

    [Fact]
    public void EmptyAndOutOfRangeSpansAreDroppedOrClamped()
    {
        Assert.Equal(new[] { 0f, .3f }, ClearSpans.Merge(new[] { -.2f, .3f, .6f, .6f, 1.2f, 1.5f }));
        Assert.Empty(ClearSpans.Merge(Array.Empty<float>()));
    }

    [Fact]
    public void FreeIsWhatTheSpansLeave()
    {
        Assert.Equal(.4f, ClearSpans.Free(ClearSpans.Merge(new[] { 0f, .3f, .2f, .4f, .8f, 1f })), 4);
    }
}
