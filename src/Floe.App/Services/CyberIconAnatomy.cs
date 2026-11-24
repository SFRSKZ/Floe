using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.Core.Services;

namespace Floe.App.Services;

/// <summary>
/// Analyzes the pixel topology, connected islands, negative-space cutout holes, tonal depth planes,
/// and structural seams of dock icons so each icon's actual sub-parts can be animated individually.
/// </summary>
public static class CyberIconAnatomy
{
    public readonly record struct IconPart(
        BitmapSource Bitmap,
        Point NormalizedCenter,
        Rect NormalizedBounds,
        Vector RadialDir,
        int PixelCount,
        bool IsCutoutHole);

    public sealed record CutoutStencil(BitmapSource FilledBase, IReadOnlyList<IconPart> Parts);

    public sealed record SteamAnatomy(
        BitmapSource FilledDisc,
        BitmapSource CrankCutout,
        IconPart? UpperWheel,
        IconPart? LowerWheel,
        bool IsColored = false);

    public sealed record ToneAnatomy(
        BitmapSource BaseLayer,
        BitmapSource? MidLayer,
        BitmapSource FrontLayer,
        Point FrontCenter);

    public sealed record StarburstAnatomy(
        Point CoreCenter,
        BitmapSource CoreHub,
        BitmapSource GroupA,
        BitmapSource GroupB,
        BitmapSource GroupC);

    public sealed record TerminalAnatomy(
        BitmapSource FilledBody,
        IReadOnlyList<IconPart> TopTabs,
        IconPart? PromptHole,
        IconPart? CursorHole);

    public sealed record RivatunerAnatomy(
        BitmapSource ScreenBody,
        BitmapSource StandBase,
        IconPart? FpsBadge,
        IconPart? BezelSlider);

    private static readonly ConditionalWeakTable<ImageSource, IReadOnlyList<IconPart>?> IslandsCache = new();
    private static readonly ConditionalWeakTable<ImageSource, CutoutStencil?> CutoutCache = new();
    private static readonly ConditionalWeakTable<ImageSource, SteamAnatomy?> SteamCache = new();
    private static readonly ConditionalWeakTable<ImageSource, Dictionary<int, ToneAnatomy?>> ToneCache = new();
    private static readonly ConditionalWeakTable<ImageSource, StarburstAnatomy?> StarburstCache = new();
    private static readonly ConditionalWeakTable<ImageSource, TerminalAnatomy?> TerminalCache = new();
    private static readonly ConditionalWeakTable<ImageSource, RivatunerAnatomy?> RivatunerCache = new();
    private static readonly ConditionalWeakTable<ImageSource, Dictionary<int, (BitmapSource A, BitmapSource B)>> SeamCache = new();

    /// <summary>Clears all cached topological decompositions.</summary>
    public static void ClearCache()
    {
        IslandsCache.Clear();
        CutoutCache.Clear();
        SteamCache.Clear();
        ToneCache.Clear();
        StarburstCache.Clear();
        TerminalCache.Clear();
        RivatunerCache.Clear();
        SeamCache.Clear();
    }

    /// <summary>
    /// Extracts physically separate opaque connected components (e.g. Chrome's 3 outer shutter petals + center core,
    /// Blender's outer ring + offset eye pupil, 7-Zip's frame + '7' + 'z').
    /// </summary>
    public static IReadOnlyList<IconPart>? GetConnectedIslands(ImageSource logo)
    {
        if (IslandsCache.TryGetValue(logo, out var cached)) return cached;
        if (!CopyBgra32(logo, out int w, out int h, out var px)) return null;

        if (CardIcons.TryGetMonoLogo(logo, out var monoLogo))
        {
            var monoIslands = GetConnectedIslands(monoLogo);
            if (monoIslands is null)
            {
                IslandsCache.AddOrUpdate(logo, null);
                return null;
            }

            var coloredIslands = new List<IconPart>(monoIslands.Count);
            foreach (var isl in monoIslands)
            {
                coloredIslands.Add(isl with
                {
                    Bitmap = ProjectColorOntoMask(w, h, px, isl.Bitmap),
                    IsCutoutHole = false,
                });
            }

            IslandsCache.AddOrUpdate(logo, coloredIslands);
            return coloredIslands;
        }

        int total = w * h;
        var visited = new bool[total];
        var labelMap = new int[total];
        Array.Fill(labelMap, -1);

        var rawIslands = new List<(List<int> Indices, double SumX, double SumY, int MinX, int MaxX, int MinY, int MaxY)>();
        var queue = new Queue<int>();
        ReadOnlySpan<(int Dx, int Dy)> dirs = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                if (visited[idx] || px[(idx * 4) + 3] <= 42) continue;

                visited[idx] = true;
                queue.Enqueue(idx);
                var members = new List<int>();
                double sx = 0, sy = 0;
                int minX = w, maxX = 0, minY = h, maxY = 0;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    int cx = cur % w;
                    int cy = (cur - cx) / w;
                    members.Add(cur);
                    sx += cx;
                    sy += cy;
                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;

                    foreach (var (dx, dy) in dirs)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                        int ni = (ny * w) + nx;
                        if (!visited[ni] && px[(ni * 4) + 3] > 42)
                        {
                            visited[ni] = true;
                            queue.Enqueue(ni);
                        }
                    }
                }

                if (members.Count >= 14)
                {
                    int id = rawIslands.Count;
                    foreach (int m in members) labelMap[m] = id;
                    rawIslands.Add((members, sx, sy, minX, maxX, minY, maxY));
                }
            }
        }

        if (rawIslands.Count is < 2 or > 10)
        {
            IslandsCache.AddOrUpdate(logo, null);
            return null;
        }

        // Assign anti-aliased fringe pixels (0 < alpha <= 42) to nearest island label within 2px.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                if (labelMap[idx] >= 0 || px[(idx * 4) + 3] == 0) continue;
                int bestId = -1;
                int bestDistSq = 99;
                for (int dy = -2; dy <= 2; dy++)
                {
                    int ny = y + dy;
                    if ((uint)ny >= (uint)h) continue;
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = x + dx;
                        if ((uint)nx >= (uint)w) continue;
                        int lid = labelMap[(ny * w) + nx];
                        int dsq = (dx * dx) + (dy * dy);
                        if (lid >= 0 && dsq < bestDistSq)
                        {
                            bestDistSq = dsq;
                            bestId = lid;
                        }
                    }
                }

                if (bestId >= 0)
                {
                    labelMap[idx] = bestId;
                    rawIslands[bestId].Indices.Add(idx);
                }
            }
        }

        double centerX = (w - 1) * 0.5;
        double centerY = (h - 1) * 0.5;
        var result = new List<IconPart>(rawIslands.Count);

        for (int id = 0; id < rawIslands.Count; id++)
        {
            var item = rawIslands[id];
            var buf = new byte[px.Length];
            foreach (int m in item.Indices)
            {
                int bi = m * 4;
                buf[bi] = px[bi];
                buf[bi + 1] = px[bi + 1];
                buf[bi + 2] = px[bi + 2];
                buf[bi + 3] = px[bi + 3];
            }

            double cx = item.SumX / item.Indices.Count;
            double cy = item.SumY / item.Indices.Count;
            var dir = new Vector(cx - centerX, cy - centerY);
            if (dir.Length > 0.5) dir.Normalize();
            else dir = new Vector(0, -1);

            var bounds = new Rect(
                (double)item.MinX / w,
                (double)item.MinY / h,
                (double)(item.MaxX - item.MinX + 1) / w,
                (double)(item.MaxY - item.MinY + 1) / h);

            result.Add(new IconPart(
                FreezeBgra(w, h, buf),
                new Point(cx / w, cy / h),
                bounds,
                dir,
                item.Indices.Count,
                IsCutoutHole: false));
        }

        IslandsCache.AddOrUpdate(logo, result);
        return result;
    }

    /// <summary>
    /// Extracts interior transparent cutout holes (or high-contrast interior color symbols on Real icons)
    /// along with an inpainted solid base plate/disc so the interior symbols can move freely inside the base.
    /// </summary>
    public static CutoutStencil? GetCutoutStencil(ImageSource logo, bool isCircularDisc = false)
    {
        if (CutoutCache.TryGetValue(logo, out var cached)) return cached;
        if (!CopyBgra32(logo, out int w, out int h, out var px)) return null;

        if (CardIcons.TryGetMonoLogo(logo, out var monoLogo))
        {
            var monoStencil = GetCutoutStencil(monoLogo, isCircularDisc);
            if (monoStencil is null || !CopyBgra32(monoStencil.FilledBase, out _, out _, out var baseMask))
            {
                CutoutCache.AddOrUpdate(logo, null);
                return null;
            }

            // Compute union of all moving part masks so we can cleanly fill behind them in FilledBase.
            int totalPx = w * h;
            var partUnionA = new byte[totalPx];
            var coloredParts = new List<IconPart>(monoStencil.Parts.Count);
            foreach (var mp in monoStencil.Parts)
            {
                if (CopyBgra32(mp.Bitmap, out _, out _, out var pMask))
                {
                    for (int i = 0; i < totalPx; i++)
                    {
                        byte pa = pMask[(i * 4) + 3];
                        if (pa > partUnionA[i]) partUnionA[i] = pa;
                    }
                }

                coloredParts.Add(mp with
                {
                    Bitmap = ProjectColorOntoMask(w, h, px, mp.Bitmap),
                    IsCutoutHole = false,
                });
            }

            // Sample average clean plate color outside the moving parts.
            long pSumB = 0, pSumG = 0, pSumR = 0, pCnt = 0;
            for (int i = 0; i < totalPx; i++)
            {
                if (baseMask[(i * 4) + 3] > 200 && partUnionA[i] < 16)
                {
                    pSumB += px[i * 4];
                    pSumG += px[(i * 4) + 1];
                    pSumR += px[(i * 4) + 2];
                    pCnt++;
                }
            }

            byte fillB = pCnt > 0 ? (byte)(pSumB / pCnt) : (byte)0x28;
            byte fillG = pCnt > 0 ? (byte)(pSumG / pCnt) : (byte)0x28;
            byte fillR = pCnt > 0 ? (byte)(pSumR / pCnt) : (byte)0x28;

            var coloredBase = new byte[px.Length];
            for (int i = 0; i < totalPx; i++)
            {
                int bi = i * 4;
                byte ba = baseMask[bi + 3];
                if (ba == 0) continue;
                double partT = partUnionA[i] / 255.0;
                coloredBase[bi] = (byte)Math.Round((px[bi] * (1.0 - partT)) + (fillB * partT));
                coloredBase[bi + 1] = (byte)Math.Round((px[bi + 1] * (1.0 - partT)) + (fillG * partT));
                coloredBase[bi + 2] = (byte)Math.Round((px[bi + 2] * (1.0 - partT)) + (fillR * partT));
                coloredBase[bi + 3] = ba;
            }

            var coloredStencil = new CutoutStencil(FreezeBgra(w, h, coloredBase), coloredParts);
            CutoutCache.AddOrUpdate(logo, coloredStencil);
            return coloredStencil;
        }

        int total = w * h;
        var exterior = new bool[total];
        var queue = new Queue<int>();
        double cx0 = (w - 1) * 0.5;
        double cy0 = (h - 1) * 0.5;
        double maxDiscR = Math.Min(w, h) * 0.455;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool onEdge = x == 0 || x == w - 1 || y == 0 || y == h - 1;
                if (!onEdge) continue;
                int idx = (y * w) + x;
                if (px[(idx * 4) + 3] <= 55)
                {
                    exterior[idx] = true;
                    queue.Enqueue(idx);
                }
            }
        }

        ReadOnlySpan<(int Dx, int Dy)> dirs4 = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int cx = cur % w;
            int cy = (cur - cx) / w;
            foreach (var (dx, dy) in dirs4)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int ni = (ny * w) + nx;
                if (exterior[ni] || px[(ni * 4) + 3] > 55) continue;

                // For circular badges whose bottom cutout touches the rim (e.g. GitHub Octocat),
                // block exterior flood from entering the inner r <= 0.43 disc.
                if (isCircularDisc)
                {
                    double r = Math.Sqrt(((nx - cx0) * (nx - cx0)) + ((ny - cy0) * (ny - cy0)));
                    if (r < maxDiscR) continue;
                }

                exterior[ni] = true;
                queue.Enqueue(ni);
            }
        }

        // Sample dominant plate color from opaque pixels.
        long sumB = 0, sumG = 0, sumR = 0, opqCount = 0;
        for (int i = 0; i < total; i++)
        {
            if (px[(i * 4) + 3] > 210)
            {
                sumB += px[i * 4];
                sumG += px[(i * 4) + 1];
                sumR += px[(i * 4) + 2];
                opqCount++;
            }
        }

        if (opqCount < 100)
        {
            CutoutCache.AddOrUpdate(logo, null);
            return null;
        }

        byte plateB = (byte)(sumB / opqCount);
        byte plateG = (byte)(sumG / opqCount);
        byte plateR = (byte)(sumR / opqCount);

        // Find connected interior transparent holes (!exterior && alpha <= 140).
        var visited = new bool[total];
        var rawHoles = new List<(HashSet<int> Pixels, double SumX, double SumY, int MinX, int MaxX, int MinY, int MaxY)>();

        for (int y = 2; y < h - 2; y++)
        {
            for (int x = 2; x < w - 2; x++)
            {
                int idx = (y * w) + x;
                if (visited[idx] || exterior[idx] || px[(idx * 4) + 3] > 95) continue;

                visited[idx] = true;
                queue.Enqueue(idx);
                var set = new HashSet<int>();
                double sx = 0, sy = 0;
                int minX = w, maxX = 0, minY = h, maxY = 0;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    int cx = cur % w;
                    int cy = (cur - cx) / w;
                    set.Add(cur);
                    sx += cx;
                    sy += cy;
                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;

                    foreach (var (dx, dy) in dirs4)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                        int ni = (ny * w) + nx;
                        if (!visited[ni] && !exterior[ni] && px[(ni * 4) + 3] <= 170)
                        {
                            visited[ni] = true;
                            queue.Enqueue(ni);
                        }
                    }
                }

                if (set.Count >= 18)
                {
                    // Expand 1px fringe to capture all anti-aliased boundary pixels of the hole.
                    foreach (int coreIdx in set.ToArray())
                    {
                        int cx = coreIdx % w;
                        int cy = (coreIdx - cx) / w;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = cx + dx, ny = cy + dy;
                                if ((uint)nx < (uint)w && (uint)ny < (uint)h)
                                {
                                    int ni = (ny * w) + nx;
                                    if (!exterior[ni] && px[(ni * 4) + 3] < 252 && set.Add(ni))
                                    {
                                        sx += nx;
                                        sy += ny;
                                    }
                                }
                            }
                        }
                    }

                    rawHoles.Add((set, sx, sy, minX, maxX, minY, maxY));
                }
            }
        }

        int totalHolePixels = 0;
        foreach (var hItem in rawHoles) totalHolePixels += hItem.Pixels.Count;

        if (rawHoles.Count > 0 && totalHolePixels >= (total / 50))
        {
            var baseBuf = new byte[px.Length];
            Buffer.BlockCopy(px, 0, baseBuf, 0, px.Length);
            var parts = new List<IconPart>(rawHoles.Count);

            foreach (var hole in rawHoles.OrderBy(hItem => hItem.SumY / hItem.Pixels.Count))
            {
                var holeBuf = new byte[px.Length];
                foreach (int idx in hole.Pixels)
                {
                    int bi = idx * 4;
                    byte origA = px[bi + 3];
                    byte holeA = (byte)(255 - origA);
                    if (holeA == 0) continue;

                    holeBuf[bi] = 0xFF;
                    holeBuf[bi + 1] = 0xFF;
                    holeBuf[bi + 2] = 0xFF;
                    holeBuf[bi + 3] = holeA;

                    // Fill hole in base plate with plate tone.
                    baseBuf[bi] = plateB;
                    baseBuf[bi + 1] = plateG;
                    baseBuf[bi + 2] = plateR;
                    baseBuf[bi + 3] = 255;
                }

                double cx = hole.SumX / hole.Pixels.Count;
                double cy = hole.SumY / hole.Pixels.Count;
                var dir = new Vector(cx - cx0, cy - cy0);
                if (dir.Length > 0.5) dir.Normalize();
                else dir = new Vector(0, -1);

                parts.Add(new IconPart(
                    FreezeBgra(w, h, holeBuf),
                    new Point(cx / w, cy / h),
                    new Rect((double)hole.MinX / w, (double)hole.MinY / h, (double)(hole.MaxX - hole.MinX + 1) / w, (double)(hole.MaxY - hole.MinY + 1) / h),
                    dir,
                    hole.Pixels.Count,
                    IsCutoutHole: true));
            }

            var stencil = new CutoutStencil(FreezeBgra(w, h, baseBuf), parts);
            CutoutCache.AddOrUpdate(logo, stencil);
            return stencil;
        }

        // Fallback for Real Color icons (e.g. Real Green Spotify or Real Apple Music):
        // find high-contrast interior color islands inside the outer disc/tile.
        var colorStencil = TryExtractHighContrastInteriorParts(w, h, px);
        CutoutCache.AddOrUpdate(logo, colorStencil);
        return colorStencil;
    }

    private static CutoutStencil? TryExtractHighContrastInteriorParts(int w, int h, byte[] px)
    {
        double cx0 = (w - 1) * 0.5;
        double cy0 = (h - 1) * 0.5;
        double minR = Math.Min(w, h) * 0.34;
        double maxR = Math.Min(w, h) * 0.46;

        long sumB = 0, sumG = 0, sumR = 0, ringCount = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                if (px[(idx * 4) + 3] < 210) continue;
                double r = Math.Sqrt(((x - cx0) * (x - cx0)) + ((y - cy0) * (y - cy0)));
                if (r >= minR && r <= maxR)
                {
                    sumB += px[idx * 4];
                    sumG += px[(idx * 4) + 1];
                    sumR += px[(idx * 4) + 2];
                    ringCount++;
                }
            }
        }

        if (ringCount < 40) return null;
        byte bgB = (byte)(sumB / ringCount);
        byte bgG = (byte)(sumG / ringCount);
        byte bgR = (byte)(sumR / ringCount);
        double innerLimitR = Math.Min(w, h) * 0.43;

        // Check for a dark inner well (e.g. Media Player: outer silver ring -> dark inner disc at r=0.24..0.32 -> bright '►' play triangle at r<=0.25).
        long wellB = 0, wellG = 0, wellR = 0, wellCount = 0;
        double wellMinR = Math.Min(w, h) * 0.24;
        double wellMaxR = Math.Min(w, h) * 0.32;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                if (px[(idx * 4) + 3] < 210) continue;
                double r = Math.Sqrt(((x - cx0) * (x - cx0)) + ((y - cy0) * (y - cy0)));
                if (r >= wellMinR && r <= wellMaxR)
                {
                    wellB += px[idx * 4];
                    wellG += px[(idx * 4) + 1];
                    wellR += px[(idx * 4) + 2];
                    wellCount++;
                }
            }
        }

        if (wellCount >= 30)
        {
            byte wB = (byte)(wellB / wellCount);
            byte wG = (byte)(wellG / wellCount);
            byte wR = (byte)(wellR / wellCount);
            int ringLum = Math.Max(bgB, Math.Max(bgG, bgR));
            int wellLum = Math.Max(wB, Math.Max(wG, wR));
            if (ringLum - wellLum > 45)
            {
                bgB = wB;
                bgG = wG;
                bgR = wR;
                innerLimitR = Math.Min(w, h) * 0.26;
            }
        }

        int total = w * h;
        var contrastA = new byte[total];

        for (int y = 2; y < h - 2; y++)
        {
            for (int x = 2; x < w - 2; x++)
            {
                int idx = (y * w) + x;
                byte a = px[(idx * 4) + 3];
                if (a < 150) continue;
                double r = Math.Sqrt(((x - cx0) * (x - cx0)) + ((y - cy0) * (y - cy0)));
                if (r > innerLimitR) continue;

                int db = Math.Abs(px[idx * 4] - bgB);
                int dg = Math.Abs(px[(idx * 4) + 1] - bgG);
                int dr = Math.Abs(px[(idx * 4) + 2] - bgR);
                int dist = Math.Max(db, Math.Max(dg, dr));
                if (dist > 38)
                {
                    double t = Math.Clamp((dist - 28.0) / 85.0, 0.0, 1.0);
                    contrastA[idx] = (byte)Math.Round(a * t);
                }
            }
        }

        var visited = new bool[total];
        var queue = new Queue<int>();
        ReadOnlySpan<(int Dx, int Dy)> dirs = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        var rawParts = new List<(List<int> Pixels, double SumX, double SumY, int MinX, int MaxX, int MinY, int MaxY)>();

        for (int y = 2; y < h - 2; y++)
        {
            for (int x = 2; x < w - 2; x++)
            {
                int idx = (y * w) + x;
                if (visited[idx] || contrastA[idx] < 45) continue;

                visited[idx] = true;
                queue.Enqueue(idx);
                var list = new List<int>();
                double sx = 0, sy = 0;
                int minX = w, maxX = 0, minY = h, maxY = 0;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    int cx = cur % w;
                    int cy = (cur - cx) / w;
                    list.Add(cur);
                    sx += cx;
                    sy += cy;
                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;

                    foreach (var (dx, dy) in dirs)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                        int ni = (ny * w) + nx;
                        if (!visited[ni] && contrastA[ni] > 12)
                        {
                            visited[ni] = true;
                            queue.Enqueue(ni);
                        }
                    }
                }

                if (list.Count >= 18) rawParts.Add((list, sx, sy, minX, maxX, minY, maxY));
            }
        }

        if (rawParts.Count == 0) return null;

        var baseBuf = new byte[px.Length];
        Buffer.BlockCopy(px, 0, baseBuf, 0, px.Length);
        var parts = new List<IconPart>(rawParts.Count);

        foreach (var part in rawParts.OrderBy(p => p.SumY / p.Pixels.Count))
        {
            var partBuf = new byte[px.Length];
            foreach (int idx in part.Pixels)
            {
                int bi = idx * 4;
                byte ca = contrastA[idx];
                partBuf[bi] = px[bi];
                partBuf[bi + 1] = px[bi + 1];
                partBuf[bi + 2] = px[bi + 2];
                partBuf[bi + 3] = ca;

                baseBuf[bi] = bgB;
                baseBuf[bi + 1] = bgG;
                baseBuf[bi + 2] = bgR;
            }

            double cx = part.SumX / part.Pixels.Count;
            double cy = part.SumY / part.Pixels.Count;
            var dir = new Vector(cx - cx0, cy - cy0);
            if (dir.Length > 0.5) dir.Normalize();
            else dir = new Vector(0, -1);

            parts.Add(new IconPart(
                FreezeBgra(w, h, partBuf),
                new Point(cx / w, cy / h),
                new Rect((double)part.MinX / w, (double)part.MinY / h, (double)(part.MaxX - part.MinX + 1) / w, (double)(part.MaxY - part.MinY + 1) / h),
                dir,
                part.Pixels.Count,
                IsCutoutHole: false));
        }

        return new CutoutStencil(FreezeBgra(w, h, baseBuf), parts);
    }

    /// <summary>Extracts Steam's circular engine disc, negative-space crank rod cutout, and two internal wheel hubs.</summary>
    public static SteamAnatomy? GetSteamAnatomy(ImageSource logo)
    {
        if (SteamCache.TryGetValue(logo, out var cached)) return cached;
        if (!CopyBgra32(logo, out int w, out int h, out var px)) return null;

        if (CardIcons.TryGetMonoLogo(logo, out var monoLogo))
        {
            if (GetSteamAnatomy(monoLogo) is not { } monoSteam
                || !CopyBgra32(monoSteam.FilledDisc, out _, out _, out var discMask))
            {
                SteamCache.AddOrUpdate(logo, null);
                return null;
            }

            var coloredDisc = new byte[px.Length];
            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)Math.Max(1, h - 1);
                for (int x = 0; x < w; x++)
                {
                    int bi = ((y * w) + x) * 4;
                    byte da = discMask[bi + 3];
                    if (da == 0) continue;
                    float nx = x / (float)Math.Max(1, w - 1);
                    float g = Math.Clamp((nx * 0.35f) + (ny * 0.65f), 0f, 1f);
                    coloredDisc[bi] = (byte)(0x52 + ((0xEC - 0x52) * g));
                    coloredDisc[bi + 1] = (byte)(0x24 + ((0x92 - 0x24) * g));
                    coloredDisc[bi + 2] = (byte)(0x10 + ((0x1A - 0x10) * g));
                    coloredDisc[bi + 3] = da;
                }
            }

            var coloredCrank = ProjectColorOntoMask(w, h, px, monoSteam.CrankCutout);
            IconPart? upper = monoSteam.UpperWheel is { } uw ? uw with { Bitmap = ProjectColorOntoMask(w, h, px, uw.Bitmap) } : null;
            IconPart? lower = monoSteam.LowerWheel is { } lw ? lw with { Bitmap = ProjectColorOntoMask(w, h, px, lw.Bitmap) } : null;

            var coloredSteam = new SteamAnatomy(FreezeBgra(w, h, coloredDisc), coloredCrank, upper, lower, IsColored: true);
            SteamCache.AddOrUpdate(logo, coloredSteam);
            return coloredSteam;
        }

        var islands = GetConnectedIslands(logo);
        IconPart? upperWheel = null;
        IconPart? lowerWheel = null;
        if (islands is not null)
        {
            foreach (var isl in islands)
            {
                if (isl.PixelCount < w * h * 0.25)
                {
                    if (isl.NormalizedCenter.X > 0.52 && isl.NormalizedCenter.Y < 0.52) upperWheel = isl;
                    else if (isl.NormalizedCenter.X < 0.52 && isl.NormalizedCenter.Y > 0.52) lowerWheel = isl;
                }
            }
        }

        int total = w * h;
        long sumB = 0, sumG = 0, sumR = 0, cnt = 0;
        for (int i = 0; i < total; i++)
        {
            if (px[(i * 4) + 3] > 200)
            {
                sumB += px[i * 4];
                sumG += px[(i * 4) + 1];
                sumR += px[(i * 4) + 2];
                cnt++;
            }
        }

        if (cnt < 50) return null;
        byte pB = (byte)(sumB / cnt), pG = (byte)(sumG / cnt), pR = (byte)(sumR / cnt);

        var discBuf = new byte[px.Length];
        var crankBuf = new byte[px.Length];
        double cx = (w - 1) * 0.5, cy = (h - 1) * 0.5;
        double discR = Math.Min(w, h) * 0.492;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                int bi = idx * 4;
                double d = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
                byte discA = (byte)Math.Round(255.0 * Math.Clamp(discR + 0.75 - d, 0.0, 1.0));
                byte origA = px[bi + 3];

                byte unionA = Math.Max(discA, origA);
                discBuf[bi] = origA > 180 ? px[bi] : pB;
                discBuf[bi + 1] = origA > 180 ? px[bi + 1] : pG;
                discBuf[bi + 2] = origA > 180 ? px[bi + 2] : pR;
                discBuf[bi + 3] = unionA;

                if (discA > origA)
                {
                    crankBuf[bi] = 0xFF;
                    crankBuf[bi + 1] = 0xFF;
                    crankBuf[bi + 2] = 0xFF;
                    crankBuf[bi + 3] = (byte)(discA - origA);
                }
            }
        }

        var result = new SteamAnatomy(FreezeBgra(w, h, discBuf), FreezeBgra(w, h, crankBuf), upperWheel, lowerWheel);
        SteamCache.AddOrUpdate(logo, result);
        return result;
    }

    /// <summary>Separates multi-tone shaded icons (Antigravity, Task Manager, OrcaSlicer, Windows Settings) into depth planes.</summary>
    public static ToneAnatomy? GetToneAnatomy(ImageSource logo, AnimatedIconKind kind)
    {
        var dict = ToneCache.GetOrCreateValue(logo);
        int key = (int)kind;
        lock (dict)
        {
            if (dict.TryGetValue(key, out var cached)) return cached;
            if (!CopyBgra32(logo, out int w, out int h, out var px)) return dict[key] = null;

            if (CardIcons.TryGetMonoLogo(logo, out var monoLogo))
            {
                if (GetToneAnatomy(monoLogo, kind) is not { } monoTone)
                {
                    return dict[key] = null;
                }

                return dict[key] = ProjectToneAnatomy(w, h, px, monoTone, kind);
            }

            ToneAnatomy? res = kind switch
            {
                AnimatedIconKind.Antigravity => ExtractAntigravityTones(w, h, px),
                AnimatedIconKind.TaskManager => ExtractTaskManagerTones(w, h, px),
                AnimatedIconKind.OrcaSlicer => ExtractOrcaSlicerTones(w, h, px),
                AnimatedIconKind.WindowsSettings => ExtractSettingsGearTones(w, h, px),
                _ => ExtractGenericShadedTones(w, h, px),
            };
            return dict[key] = res;
        }
    }

    private static ToneAnatomy? ExtractAntigravityTones(int w, int h, byte[] px)
    {
        GetToneRange(w, h, px, out byte minT, out byte maxT, out byte brightB, out byte brightG, out byte brightR, out byte darkB, out byte darkG, out byte darkR);
        if (maxT - minT < 35) return null;

        // Detect whether the outer rounded-square tile is bright (monochrome card) or dark (real color icon).
        long perimBright = 0, perimDark = 0;
        byte mid = (byte)((minT + maxT) / 2);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (x > w * 0.22 && x < w * 0.78 && y > h * 0.22 && y < h * 0.78) continue;
                int bi = ((y * w) + x) * 4;
                if (px[bi + 3] < 180) continue;
                byte tone = Math.Max(px[bi], Math.Max(px[bi + 1], px[bi + 2]));
                if (tone >= mid) perimBright++;
                else perimDark++;
            }
        }

        bool baseIsBright = perimBright >= perimDark;
        byte tileB = baseIsBright ? brightB : darkB;
        byte tileG = baseIsBright ? brightG : darkG;
        byte tileR = baseIsBright ? brightR : darkR;

        var baseBuf = new byte[px.Length];
        var shipBuf = new byte[px.Length];
        double sumX = 0, sumY = 0, weightSum = 0;
        double span = Math.Max(20.0, maxT - minT);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                int bi = idx * 4;
                byte a = px[bi + 3];
                if (a == 0) continue;

                // Base plate is the complete rounded square tile in the outer tile tone.
                baseBuf[bi] = tileB;
                baseBuf[bi + 1] = tileG;
                baseBuf[bi + 2] = tileR;
                baseBuf[bi + 3] = a;

                // Inner 'A' delta-wing spacecraft is the contrasting region inside the tile.
                byte tone = Math.Max(px[bi], Math.Max(px[bi + 1], px[bi + 2]));
                double shipContrast = baseIsBright
                    ? Math.Clamp((maxT - 12.0 - tone) / (span * 0.82), 0.0, 1.0)
                    : Math.Clamp((tone - (minT + 12.0)) / (span * 0.82), 0.0, 1.0);
                if (shipContrast > 0.02 && x > w * 0.10 && x < w * 0.90 && y > h * 0.10 && y < h * 0.88)
                {
                    byte shipA = (byte)Math.Round(a * shipContrast);
                    shipBuf[bi] = baseIsBright ? darkB : px[bi];
                    shipBuf[bi + 1] = baseIsBright ? darkG : px[bi + 1];
                    shipBuf[bi + 2] = baseIsBright ? darkR : px[bi + 2];
                    shipBuf[bi + 3] = shipA;
                    sumX += x * shipA;
                    sumY += y * shipA;
                    weightSum += shipA;
                }
            }
        }

        if (weightSum < 255 * 80) return null;
        return new ToneAnatomy(
            FreezeBgra(w, h, baseBuf),
            null,
            FreezeBgra(w, h, shipBuf),
            new Point(sumX / (weightSum * w), sumY / (weightSum * h)));
    }

    private static ToneAnatomy? ExtractTaskManagerTones(int w, int h, byte[] px)
    {
        GetToneRange(w, h, px, out byte minT, out byte maxT, out byte brightB, out byte brightG, out byte brightR, out byte darkB, out byte darkG, out byte darkR);
        if (maxT - minT < 35) return null;

        byte midT = (byte)((minT + maxT) / 2);
        byte midB = (byte)((darkB + brightB) / 2);
        byte midG = (byte)((darkG + brightG) / 2);
        byte midR = (byte)((darkR + brightR) / 2);

        var skyBuf = new byte[px.Length];
        var backWaveBuf = new byte[px.Length];
        var frontWaveBuf = new byte[px.Length];

        for (int x = 0; x < w; x++)
        {
            int midTopY = h;
            int frontTopY = h;
            int maxOpaqueY = 0;

            for (int y = 0; y < h; y++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = px[bi + 3];
                if (a <= 35) continue;
                if (y > maxOpaqueY) maxOpaqueY = y;

                // Full screen filled with dark monitor sky tone.
                skyBuf[bi] = darkB;
                skyBuf[bi + 1] = darkG;
                skyBuf[bi + 2] = darkR;
                skyBuf[bi + 3] = a;

                byte tone = Math.Max(px[bi], Math.Max(px[bi + 1], px[bi + 2]));
                double norm = (tone - minT) / (double)Math.Max(1, maxT - minT);
                if (norm >= 0.34 && norm < 0.76 && y < midTopY) midTopY = y;
                if (norm >= 0.76 && y < frontTopY) frontTopY = y;
            }

            // Fill each mountain wave from its detected crest all the way down to maxOpaqueY so vertical scaling never leaves a gap.
            for (int y = midTopY; y <= maxOpaqueY; y++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = px[bi + 3];
                if (a == 0) continue;
                backWaveBuf[bi] = midB;
                backWaveBuf[bi + 1] = midG;
                backWaveBuf[bi + 2] = midR;
                backWaveBuf[bi + 3] = y == midTopY ? (byte)(a * 0.7) : a;
            }

            for (int y = frontTopY; y <= maxOpaqueY; y++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = px[bi + 3];
                if (a == 0) continue;
                frontWaveBuf[bi] = brightB;
                frontWaveBuf[bi + 1] = brightG;
                frontWaveBuf[bi + 2] = brightR;
                frontWaveBuf[bi + 3] = y == frontTopY ? (byte)(a * 0.75) : a;
            }
        }

        return new ToneAnatomy(
            FreezeBgra(w, h, skyBuf),
            FreezeBgra(w, h, backWaveBuf),
            FreezeBgra(w, h, frontWaveBuf),
            new Point(0.5, 0.72));
    }

    private static ToneAnatomy? ExtractOrcaSlicerTones(int w, int h, byte[] px)
    {
        GetToneRange(w, h, px, out byte minT, out byte maxT, out _, out _, out _, out byte darkB, out byte darkG, out byte darkR);
        if (maxT - minT < 35) return null;

        var plateBuf = new byte[px.Length];
        var orcaBuf = new byte[px.Length];
        double span = Math.Max(20.0, maxT - minT);

        for (int i = 0; i < w * h; i++)
        {
            int bi = i * 4;
            byte a = px[bi + 3];
            if (a == 0) continue;

            // Grounded dark circular build plate.
            plateBuf[bi] = darkB;
            plateBuf[bi + 1] = darkG;
            plateBuf[bi + 2] = darkR;
            plateBuf[bi + 3] = a;

            // Bright leaping Orca whale & diagonal slicer cut on top of the build plate.
            byte tone = Math.Max(px[bi], Math.Max(px[bi + 1], px[bi + 2]));
            double lightness = Math.Clamp((tone - (minT + 14.0)) / (span * 0.75), 0.0, 1.0);
            if (lightness > 0.03)
            {
                orcaBuf[bi] = px[bi];
                orcaBuf[bi + 1] = px[bi + 1];
                orcaBuf[bi + 2] = px[bi + 2];
                orcaBuf[bi + 3] = (byte)Math.Round(a * lightness);
            }
        }

        return new ToneAnatomy(FreezeBgra(w, h, plateBuf), null, FreezeBgra(w, h, orcaBuf), new Point(0.5, 0.5));
    }

    private static ToneAnatomy? ExtractSettingsGearTones(int w, int h, byte[] px)
    {
        GetToneRange(w, h, px, out byte minT, out byte maxT, out _, out _, out _, out byte darkB, out byte darkG, out byte darkR);
        if (maxT - minT < 30) return null;

        var gearBuf = new byte[px.Length];
        var hubBuf = new byte[px.Length];
        double span = Math.Max(20.0, maxT - minT);

        for (int i = 0; i < w * h; i++)
        {
            int bi = i * 4;
            byte a = px[bi + 3];
            if (a == 0) continue;

            gearBuf[bi] = darkB;
            gearBuf[bi + 1] = darkG;
            gearBuf[bi + 2] = darkR;
            gearBuf[bi + 3] = a;

            byte tone = Math.Max(px[bi], Math.Max(px[bi + 1], px[bi + 2]));
            double hubT = Math.Clamp((tone - (minT + 18.0)) / (span * 0.65), 0.0, 1.0);
            if (hubT > 0.04)
            {
                hubBuf[bi] = px[bi];
                hubBuf[bi + 1] = px[bi + 1];
                hubBuf[bi + 2] = px[bi + 2];
                hubBuf[bi + 3] = (byte)Math.Round(a * hubT);
            }
        }

        return new ToneAnatomy(FreezeBgra(w, h, gearBuf), null, FreezeBgra(w, h, hubBuf), new Point(0.5, 0.5));
    }

    private static ToneAnatomy? ExtractGenericShadedTones(int w, int h, byte[] px)
    {
        GetToneRange(w, h, px, out byte minT, out byte maxT, out byte brightB, out byte brightG, out byte brightR, out byte darkB, out byte darkG, out byte darkR);
        if (maxT - minT < 42) return null;

        // Determine whether outer perimeter is predominantly bright or dark.
        long perimBright = 0, perimDark = 0;
        byte mid = (byte)((minT + maxT) / 2);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (x > w * 0.22 && x < w * 0.78 && y > h * 0.22 && y < h * 0.78) continue;
                int bi = ((y * w) + x) * 4;
                if (px[bi + 3] < 180) continue;
                byte tone = Math.Max(px[bi], Math.Max(px[bi + 1], px[bi + 2]));
                if (tone >= mid) perimBright++;
                else perimDark++;
            }
        }

        bool baseIsBright = perimBright >= perimDark;
        byte baseB = baseIsBright ? brightB : darkB;
        byte baseG = baseIsBright ? brightG : darkG;
        byte baseR = baseIsBright ? brightR : darkR;
        double span = Math.Max(25.0, maxT - minT);

        var baseBuf = new byte[px.Length];
        var frontBuf = new byte[px.Length];
        long fgCount = 0;

        for (int i = 0; i < w * h; i++)
        {
            int bi = i * 4;
            byte a = px[bi + 3];
            if (a == 0) continue;

            baseBuf[bi] = baseB;
            baseBuf[bi + 1] = baseG;
            baseBuf[bi + 2] = baseR;
            baseBuf[bi + 3] = a;

            byte tone = Math.Max(px[bi], Math.Max(px[bi + 1], px[bi + 2]));
            double fg = baseIsBright
                ? Math.Clamp((maxT - 15.0 - tone) / (span * 0.75), 0.0, 1.0)
                : Math.Clamp((tone - (minT + 15.0)) / (span * 0.75), 0.0, 1.0);

            if (fg > 0.05)
            {
                frontBuf[bi] = px[bi];
                frontBuf[bi + 1] = px[bi + 1];
                frontBuf[bi + 2] = px[bi + 2];
                frontBuf[bi + 3] = (byte)Math.Round(a * fg);
                if (fg > 0.4) fgCount++;
            }
        }

        if (fgCount < 120) return null;
        return new ToneAnatomy(FreezeBgra(w, h, baseBuf), null, FreezeBgra(w, h, frontBuf), new Point(0.5, 0.5));
    }

    private static void GetToneRange(
        int w, int h, byte[] px,
        out byte minTone, out byte maxTone,
        out byte brightB, out byte brightG, out byte brightR,
        out byte darkB, out byte darkG, out byte darkR)
    {
        minTone = 255;
        maxTone = 0;
        brightB = brightG = brightR = 0xAB;
        darkB = darkG = darkR = 0x2A;

        for (int i = 0; i < w * h; i++)
        {
            int bi = i * 4;
            if (px[bi + 3] < 200) continue;
            byte tone = Math.Max(px[bi], Math.Max(px[bi + 1], px[bi + 2]));
            if (tone < minTone)
            {
                minTone = tone;
                darkB = px[bi];
                darkG = px[bi + 1];
                darkR = px[bi + 2];
            }

            if (tone > maxTone)
            {
                maxTone = tone;
                brightB = px[bi];
                brightG = px[bi + 1];
                brightR = px[bi + 2];
            }
        }
    }

    /// <summary>
    /// Finds the true solid core center of a radial starburst icon (like Claude at (57.5, 51.4))
    /// and partitions its rays into 3 interleaved ray constellations + central hub.
    /// </summary>
    public static StarburstAnatomy? GetStarburstAnatomy(ImageSource logo)
    {
        if (StarburstCache.TryGetValue(logo, out var cached)) return cached;
        if (!CopyBgra32(logo, out int w, out int h, out var px)) return null;

        if (CardIcons.TryGetMonoLogo(logo, out var monoLogo))
        {
            if (GetStarburstAnatomy(monoLogo) is not { } monoStar)
            {
                StarburstCache.AddOrUpdate(logo, null);
                return null;
            }

            var coloredStar = new StarburstAnatomy(
                monoStar.CoreCenter,
                ProjectColorOntoMask(w, h, px, monoStar.CoreHub),
                ProjectColorOntoMask(w, h, px, monoStar.GroupA),
                ProjectColorOntoMask(w, h, px, monoStar.GroupB),
                ProjectColorOntoMask(w, h, px, monoStar.GroupC));
            StarburstCache.AddOrUpdate(logo, coloredStar);
            return coloredStar;
        }

        // Find true core nexus by 4x4 interior erosion.
        double sumX = 0, sumY = 0;
        int coreCount = 0;
        for (int y = 6; y < h - 6; y++)
        {
            for (int x = 6; x < w - 6; x++)
            {
                bool solid = true;
                for (int dy = -3; dy <= 3 && solid; dy++)
                {
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        if (px[(((y + dy) * w) + (x + dx)) * 4 + 3] < 190)
                        {
                            solid = false;
                            break;
                        }
                    }
                }

                if (solid)
                {
                    sumX += x;
                    sumY += y;
                    coreCount++;
                }
            }
        }

        double cx = coreCount > 10 ? sumX / coreCount : (w - 1) * 0.5;
        double cy = coreCount > 10 ? sumY / coreCount : (h - 1) * 0.5;
        double hubR = Math.Min(w, h) * 0.135;

        var hubBuf = new byte[px.Length];
        var bufA = new byte[px.Length];
        var bufB = new byte[px.Length];
        var bufC = new byte[px.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = px[bi + 3];
                if (a == 0) continue;

                double dx = x - cx;
                double dy = y - cy;
                double r = Math.Sqrt((dx * dx) + (dy * dy));
                double hubT = Math.Clamp((hubR + 1.5 - r) / 3.0, 0.0, 1.0);
                byte hubA = (byte)Math.Round(a * hubT);
                byte rayA = (byte)(a - hubA);

                if (hubA > 0)
                {
                    hubBuf[bi] = px[bi];
                    hubBuf[bi + 1] = px[bi + 1];
                    hubBuf[bi + 2] = px[bi + 2];
                    hubBuf[bi + 3] = hubA;
                }

                if (rayA > 0)
                {
                    // 12 sectors of 30° around true core center, grouped into 3 interleaved 4-ray constellations.
                    double deg = (Math.Atan2(dy, dx) * 180.0 / Math.PI) + 360.0 + 15.0;
                    int sector = ((int)Math.Floor(deg / 30.0)) % 12;
                    byte[] target = (sector % 3) switch
                    {
                        0 => bufA,
                        1 => bufB,
                        _ => bufC,
                    };
                    target[bi] = px[bi];
                    target[bi + 1] = px[bi + 1];
                    target[bi + 2] = px[bi + 2];
                    target[bi + 3] = rayA;
                }
            }
        }

        var res = new StarburstAnatomy(
            new Point(cx / w, cy / h),
            FreezeBgra(w, h, hubBuf),
            FreezeBgra(w, h, bufA),
            FreezeBgra(w, h, bufB),
            FreezeBgra(w, h, bufC));
        StarburstCache.AddOrUpdate(logo, res);
        return res;
    }

    /// <summary>Extracts Windows Terminal's 3 top tabs, filled terminal screen body, '>' prompt hole, and '_' cursor hole.</summary>
    public static TerminalAnatomy? GetTerminalAnatomy(ImageSource logo)
    {
        if (TerminalCache.TryGetValue(logo, out var cached)) return cached;
        var islands = GetConnectedIslands(logo);
        var stencil = GetCutoutStencil(logo);
        if (islands is null || stencil is null) return null;

        var tabs = islands.Where(i => i.NormalizedBounds.Bottom < 0.30).OrderBy(i => i.NormalizedCenter.X).ToList();
        var holesByX = stencil.Parts.OrderBy(p => p.NormalizedCenter.X).ToList();
        IconPart? prompt = holesByX.Count >= 1 ? holesByX[0] : null;
        IconPart? cursor = holesByX.Count >= 2 ? holesByX[1] : null;

        // Remove top tabs from FilledBody so they don't double-draw when tabs ripple.
        if (!CopyBgra32(stencil.FilledBase, out int w, out int h, out var bodyPx)) return null;
        int tabCutoffY = (int)Math.Round(h * 0.255);
        if (tabs.Count >= 2)
        {
            Array.Clear(bodyPx, 0, tabCutoffY * w * 4);
        }

        var res = new TerminalAnatomy(FreezeBgra(w, h, bodyPx), tabs, prompt, cursor);
        TerminalCache.AddOrUpdate(logo, res);
        return res;
    }

    /// <summary>Extracts RivaTuner's monitor screen, stand base, top-left '60' FPS OSD badge, and bottom bezel slider.</summary>
    public static RivatunerAnatomy? GetRivatunerAnatomy(ImageSource logo)
    {
        if (RivatunerCache.TryGetValue(logo, out var cached)) return cached;
        if (!CopyBgra32(logo, out int w, out int h, out var px)) return null;

        if (CardIcons.TryGetMonoLogo(logo, out var monoLogo))
        {
            if (GetRivatunerAnatomy(monoLogo) is not { } monoRiva)
            {
                RivatunerCache.AddOrUpdate(logo, null);
                return null;
            }

            var coloredRiva = new RivatunerAnatomy(
                ProjectColorOntoMask(w, h, px, monoRiva.ScreenBody),
                ProjectColorOntoMask(w, h, px, monoRiva.StandBase),
                monoRiva.FpsBadge is { } fb ? fb with { Bitmap = ProjectColorOntoMask(w, h, px, fb.Bitmap) } : null,
                monoRiva.BezelSlider is { } bs ? bs with { Bitmap = ProjectColorOntoMask(w, h, px, bs.Bitmap) } : null);
            RivatunerCache.AddOrUpdate(logo, coloredRiva);
            return coloredRiva;
        }

        GetToneRange(w, h, px, out byte minT, out byte maxT, out _, out _, out _, out _, out _, out _);
        int standSplitY = (int)Math.Round(h * 0.785);

        var screenBuf = new byte[px.Length];
        var standBuf = new byte[px.Length];
        var badgeBuf = new byte[px.Length];
        var sliderBuf = new byte[px.Length];
        int badgeCount = 0, sliderCount = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = px[bi + 3];
                if (a == 0) continue;

                if (y >= standSplitY)
                {
                    standBuf[bi] = px[bi];
                    standBuf[bi + 1] = px[bi + 1];
                    standBuf[bi + 2] = px[bi + 2];
                    standBuf[bi + 3] = a;
                    continue;
                }

                screenBuf[bi] = px[bi];
                screenBuf[bi + 1] = px[bi + 1];
                screenBuf[bi + 2] = px[bi + 2];
                screenBuf[bi + 3] = a;

                byte tone = Math.Max(px[bi], Math.Max(px[bi + 1], px[bi + 2]));
                if (x < w * 0.32 && y < h * 0.34 && tone < minT + 45)
                {
                    badgeBuf[bi] = px[bi];
                    badgeBuf[bi + 1] = px[bi + 1];
                    badgeBuf[bi + 2] = px[bi + 2];
                    badgeBuf[bi + 3] = a;
                    badgeCount++;
                }
                else if (y >= h * 0.69 && y < standSplitY && x < w * 0.54 && tone > maxT - 30)
                {
                    sliderBuf[bi] = px[bi];
                    sliderBuf[bi + 1] = px[bi + 1];
                    sliderBuf[bi + 2] = px[bi + 2];
                    sliderBuf[bi + 3] = a;
                    sliderCount++;
                }
            }
        }

        IconPart? badgePart = badgeCount > 15
            ? new IconPart(FreezeBgra(w, h, badgeBuf), new Point(0.16, 0.22), new Rect(0.04, 0.14, 0.26, 0.18), new Vector(-1, -1), badgeCount, false)
            : null;
        IconPart? sliderPart = sliderCount > 15
            ? new IconPart(FreezeBgra(w, h, sliderBuf), new Point(0.26, 0.74), new Rect(0.02, 0.70, 0.50, 0.08), new Vector(1, 0), sliderCount, false)
            : null;

        var res = new RivatunerAnatomy(FreezeBgra(w, h, screenBuf), FreezeBgra(w, h, standBuf), badgePart, sliderPart);
        RivatunerCache.AddOrUpdate(logo, res);
        return res;
    }

    /// <summary>
    /// Splits an icon along a structural seam:
    /// 1 = Notepad diagonal stylus cleft, 2 = OpenCode vertical monolith jaws,
    /// 3 = Autodesk Fusion isometric diagonal fold, 4 = VS Code vertical ribbon scissor.
    /// </summary>
    public static (BitmapSource First, BitmapSource Second)? SplitSeam(ImageSource logo, int seamKind)
    {
        var dict = SeamCache.GetOrCreateValue(logo);
        lock (dict)
        {
            if (dict.TryGetValue(seamKind, out var cached)) return cached;
            if (!CopyBgra32(logo, out int w, out int h, out var px)) return null;

            byte[]? monoPx = null;
            if (CardIcons.TryGetMonoLogo(logo, out var monoLogo) && CopyBgra32(monoLogo, out int mw, out int mh, out var mBytes) && mw == w && mh == h)
            {
                monoPx = mBytes;
            }

            var first = new byte[px.Length];
            var second = new byte[px.Length];

            for (int y = 0; y < h; y++)
            {
                double ny = (double)y / h;
                for (int x = 0; x < w; x++)
                {
                    int bi = ((y * w) + x) * 4;
                    byte alpha = monoPx is not null ? monoPx[bi + 3] : px[bi + 3];
                    if (alpha == 0) continue;
                    double nx = (double)x / w;

                    bool inSecond = seamKind switch
                    {
                        // Notepad: diagonal stylus pen on the upper-right (ny <= 0.53 && nx >= 0.905 - 0.44 * ny).
                        1 => ny <= 0.53 && nx >= (0.905 - (0.44 * ny)),
                        // OpenCode: right vertical monolith jaw (nx >= 0.50).
                        2 => nx >= 0.50,
                        // Autodesk Fusion: lower-right vertical isometric pillar (ny >= 0.49 && nx >= 0.52).
                        3 => ny >= 0.49 && nx >= 0.52,
                        // VS Code: right vertical ribbon loop (nx >= 0.48).
                        4 => nx >= 0.48,
                        _ => nx >= 0.50,
                    };

                    byte[] dst = inSecond ? second : first;
                    dst[bi] = px[bi];
                    dst[bi + 1] = px[bi + 1];
                    dst[bi + 2] = px[bi + 2];
                    dst[bi + 3] = alpha;
                }
            }

            return dict[seamKind] = (FreezeBgra(w, h, first), FreezeBgra(w, h, second));
        }
    }

    private static bool CopyBgra32(ImageSource logo, out int width, out int height, out byte[] pixels)
    {
        if (logo is not BitmapSource bmp || bmp.PixelWidth < 16 || bmp.PixelHeight < 16)
        {
            width = height = 0;
            pixels = Array.Empty<byte>();
            return false;
        }

        var straight = bmp.Format == PixelFormats.Bgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        width = straight.PixelWidth;
        height = straight.PixelHeight;
        int stride = width * 4;
        pixels = new byte[stride * height];
        straight.CopyPixels(pixels, stride, 0);
        return true;
    }

    private static BitmapSource ProjectColorOntoMask(int w, int h, byte[] colorPx, BitmapSource maskBmp)
    {
        if (!CopyBgra32(maskBmp, out int mw, out int mh, out var maskPx) || mw != w || mh != h)
        {
            return maskBmp;
        }

        var outPx = new byte[colorPx.Length];
        for (int i = 0; i < w * h; i++)
        {
            int bi = i * 4;
            byte ma = maskPx[bi + 3];
            if (ma == 0) continue;
            outPx[bi] = colorPx[bi];
            outPx[bi + 1] = colorPx[bi + 1];
            outPx[bi + 2] = colorPx[bi + 2];
            outPx[bi + 3] = ma;
        }

        return FreezeBgra(w, h, outPx);
    }

    private static ToneAnatomy ProjectToneAnatomy(int w, int h, byte[] colorPx, ToneAnatomy monoTone, AnimatedIconKind kind)
    {
        CopyBgra32(monoTone.BaseLayer, out _, out _, out var baseMask);
        CopyBgra32(monoTone.FrontLayer, out _, out _, out var frontMask);
        byte[]? midMask = null;
        if (monoTone.MidLayer is not null)
        {
            CopyBgra32(monoTone.MidLayer, out _, out _, out midMask);
        }

        int total = w * h;
        long sumB = 0, sumG = 0, sumR = 0, cnt = 0;
        for (int i = 0; i < total; i++)
        {
            int bi = i * 4;
            byte ba = baseMask[bi + 3];
            byte fa = frontMask[bi + 3];
            byte ma = midMask is not null ? midMask[bi + 3] : (byte)0;
            if (ba > 200 && fa < 24 && ma < 24)
            {
                sumB += colorPx[bi];
                sumG += colorPx[bi + 1];
                sumR += colorPx[bi + 2];
                cnt++;
            }
        }

        byte fillB = cnt > 0 ? (byte)(sumB / cnt) : (byte)0x26;
        byte fillG = cnt > 0 ? (byte)(sumG / cnt) : (byte)0x1E;
        byte fillR = cnt > 0 ? (byte)(sumR / cnt) : (byte)0x16;

        var baseOut = new byte[colorPx.Length];
        for (int i = 0; i < total; i++)
        {
            int bi = i * 4;
            byte ba = baseMask[bi + 3];
            if (ba == 0) continue;
            byte fgA = Math.Max(frontMask[bi + 3], midMask is not null ? midMask[bi + 3] : (byte)0);
            double fgT = fgA / 255.0;
            baseOut[bi] = (byte)Math.Round((colorPx[bi] * (1.0 - fgT)) + (fillB * fgT));
            baseOut[bi + 1] = (byte)Math.Round((colorPx[bi + 1] * (1.0 - fgT)) + (fillG * fgT));
            baseOut[bi + 2] = (byte)Math.Round((colorPx[bi + 2] * (1.0 - fgT)) + (fillR * fgT));
            baseOut[bi + 3] = ba;
        }

        BitmapSource? midOutBmp = null;
        if (midMask is not null && monoTone.MidLayer is not null)
        {
            if (kind == AnimatedIconKind.TaskManager)
            {
                var midOut = new byte[colorPx.Length];
                for (int i = 0; i < total; i++)
                {
                    int bi = i * 4;
                    byte ma = midMask[bi + 3];
                    if (ma == 0) continue;
                    midOut[bi] = 0x9E;
                    midOut[bi + 1] = 0x8B;
                    midOut[bi + 2] = 0x00;
                    midOut[bi + 3] = ma;
                }
                midOutBmp = FreezeBgra(w, h, midOut);
            }
            else
            {
                midOutBmp = ProjectColorOntoMask(w, h, colorPx, monoTone.MidLayer);
            }
        }

        var frontOutBmp = ProjectColorOntoMask(w, h, colorPx, monoTone.FrontLayer);
        return new ToneAnatomy(FreezeBgra(w, h, baseOut), midOutBmp, frontOutBmp, monoTone.FrontCenter);
    }

    private static BitmapSource FreezeBgra(int w, int h, byte[] bytes)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bytes, w * 4);
        bmp.Freeze();
        return bmp;
    }
}
