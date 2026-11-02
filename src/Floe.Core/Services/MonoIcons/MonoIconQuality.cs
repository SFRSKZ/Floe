namespace Floe.Core.Services.MonoIcons;

/// <summary>
/// A cheap sanity check of a converted icon's alpha. It catches an empty result, a solid block and a shattered one; it
/// says nothing about whether the brand is still recognisable.
/// </summary>
public static class MonoIconQuality
{
    private const int Grid = 128;
    private const double MinInk = 0.02;
    private const double SolidFill = 0.97;
    private const double Hollow = 0.02;
    private const int ManyParts = 120;
    private const double LargestShare = 0.2;

    /// <summary>Why the icon is not usable, or nothing when it passes. <paramref name="alpha"/> is row after row, one byte a pixel.</summary>
    public static string? Check(byte[] alpha, int width, int height)
    {
        if (width < 8 || height < 8 || alpha.Length < width * height) return "okunamadı";

        // 128 x 128 view: a cell is on when half of its pixels are
        var on = new bool[Grid * Grid];
        int visible = 0, left = Grid, top = Grid, right = -1, bottom = -1;
        for (var gy = 0; gy < Grid; gy++)
        {
            for (var gx = 0; gx < Grid; gx++)
            {
                int x0 = gx * width / Grid, x1 = Math.Max(x0 + 1, (gx + 1) * width / Grid);
                int y0 = gy * height / Grid, y1 = Math.Max(y0 + 1, (gy + 1) * height / Grid);
                int total = 0, lit = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        total++;
                        if (alpha[(y * width) + x] >= 128) lit++;
                    }
                }

                if (lit * 2 < total) continue;
                on[(gy * Grid) + gx] = true;
                visible++;
                left = Math.Min(left, gx);
                right = Math.Max(right, gx);
                top = Math.Min(top, gy);
                bottom = Math.Max(bottom, gy);
            }
        }

        if (visible < Grid * Grid * MinInk) return "boş";

        var width1 = right - left + 1;
        var height1 = bottom - top + 1;
        var box = width1 * height1;
        var parts = Components(on, out var largest);

        // a plate the converter filled in: a block or a disc with no drawing left inside it
        var span = Math.Max(width1, height1);
        if (span >= Grid * 0.5)
        {
            if (visible >= box * SolidFill || Empty(on, left, top, width1, height1, ellipse: false) < Hollow) return "dolu dikdörtgen";
            var squarish = Math.Min(width1, height1) >= span * 0.85;
            if (squarish && Empty(on, left, top, width1, height1, ellipse: true) < Hollow) return "dolu daire";
        }

        if (parts > ManyParts && largest < visible * LargestShare) return "parçalanmış";
        return null;
    }

    // the share of the inner part of the box (or of its inscribed ellipse) that is empty
    private static double Empty(bool[] on, int left, int top, int width, int height, bool ellipse)
    {
        double cx = left + (width / 2.0), cy = top + (height / 2.0);
        double rx = width / 2.0 * 0.9, ry = height / 2.0 * 0.9;
        int inside = 0, empty = 0;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                double dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry;
                if (ellipse ? (dx * dx) + (dy * dy) > 1 : Math.Abs(dx) > 1 || Math.Abs(dy) > 1) continue;
                inside++;
                if (!on[(y * Grid) + x]) empty++;
            }
        }

        return inside == 0 ? 0 : empty / (double)inside;
    }

    private static int Components(bool[] on, out int largest)
    {
        var seen = new bool[on.Length];
        var stack = new Stack<int>();
        int count = 0;
        largest = 0;
        for (var start = 0; start < on.Length; start++)
        {
            if (!on[start] || seen[start]) continue;
            count++;
            var size = 0;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                size++;
                int x = at % Grid, y = at / Grid;
                foreach (var next in new[] { x > 0 ? at - 1 : -1, x < Grid - 1 ? at + 1 : -1, y > 0 ? at - Grid : -1, y < Grid - 1 ? at + Grid : -1 })
                {
                    if (next < 0 || !on[next] || seen[next]) continue;
                    seen[next] = true;
                    stack.Push(next);
                }
            }

            largest = Math.Max(largest, size);
        }

        return count;
    }
}
