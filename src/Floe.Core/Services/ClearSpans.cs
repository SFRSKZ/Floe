namespace Floe.Core.Services;

/// <summary>
/// The spans of the visualizer strip (0..1 pairs) kept clear for the icons and the bar's groups. They can overlap
/// when the bar is crowded; every consumer wants them clamped, sorted and merged.
/// </summary>
public static class ClearSpans
{
    /// <summary>Clamps to 0..1, drops empty pairs, sorts and merges overlapping or touching pairs.</summary>
    public static float[] Merge(float[] spans)
    {
        var pairs = new List<(float From, float To)>(spans.Length / 2);
        for (int i = 0; i + 1 < spans.Length; i += 2)
        {
            float from = Math.Clamp(spans[i], 0, 1), to = Math.Clamp(spans[i + 1], 0, 1);
            if (to > from) pairs.Add((from, to));
        }

        pairs.Sort((a, b) => a.From.CompareTo(b.From));
        var merged = new List<float>(pairs.Count * 2);
        foreach (var (from, to) in pairs)
        {
            if (merged.Count > 0 && from <= merged[^1])
            {
                merged[^1] = Math.Max(merged[^1], to);
            }
            else
            {
                merged.Add(from);
                merged.Add(to);
            }
        }

        return merged.ToArray();
    }

    /// <summary>How much of the strip is left free.</summary>
    public static float Free(float[] merged)
    {
        float cleared = 0;
        for (int i = 0; i + 1 < merged.Length; i += 2) cleared += merged[i + 1] - merged[i];
        return 1 - cleared;
    }
}
