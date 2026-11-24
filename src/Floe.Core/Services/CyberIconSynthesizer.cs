using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Floe.Core.Services;

/// <summary>Detected structural archetype of an application icon for Cyberpunk data-card synthesis.</summary>
public enum IconArchetype
{
    MonochromeGlyph,
    TileEmblemLift,
    IconicBadgeCutout,
    MultiColorVector,
    DetailedArtwork,
}

/// <summary>
/// Pure computer-vision pipeline that transforms arbitrary BGRA32 application icons into crisp 3-tone
/// Cyberpunk data-card emblems strictly scaled from <c>#ABB2C0</c> so theme recoloring works automatically.
/// </summary>
public static class CyberIconSynthesizer
{
    public const int LogoPixels = 106;

    private const int WorkSize = 128;
    private const int Margin = 2;
    private const int InnerSize = WorkSize - (2 * Margin);
    private const int WorkPixels = WorkSize * WorkSize;
    private const byte CardR = 0xAB;
    private const byte CardG = 0xB2;
    private const byte CardB = 0xC0;

    /// <summary>Computes a deterministic versioned cache key (<c>v2_xxxxxxxxxxxxxxxx</c>) for an icon buffer.</summary>
    public static string ComputeCacheKey(byte[] bgra32, int width, int height)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header[..4], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], height);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(header);
        if (bgra32 is { Length: > 0 } && width > 0 && height > 0)
        {
            long expected = (long)width * height * 4L;
            int length = expected > bgra32.Length ? bgra32.Length : (int)expected;
            hasher.AppendData(bgra32, 0, length);
        }

        byte[] hash = hasher.GetHashAndReset();
        return "v3_" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    /// <summary>
    /// Converts a straight-alpha BGRA32 icon into a <c>106x106</c> straight-alpha BGRA32 Cyberpunk card emblem.
    /// </summary>
    public static byte[] Synthesize(byte[] bgra32, int width, int height, out IconArchetype archetype)
    {
        archetype = IconArchetype.MonochromeGlyph;
        var empty = new byte[LogoPixels * LogoPixels * 4];
        if (bgra32 is null || width <= 0 || height <= 0 || (long)bgra32.Length < (long)width * height * 4L)
        {
            return empty;
        }

        if (!FindTightBounds(bgra32, width, height, 24, out int minX, out int minY, out int maxX, out int maxY))
        {
            return empty;
        }

        int srcMaxDim = Math.Max(maxX - minX + 1, maxY - minY + 1);
        var work = new float[WorkPixels * 4];
        ResampleRegionToWork(bgra32, null, width, minX, minY, maxX, maxY, work);
        if (srcMaxDim <= 48)
        {
            SharpenLowResAlpha(work);
        }

        var L = new float[WorkPixels];
        var a = new float[WorkPixels];
        var b = new float[WorkPixels];
        var alpha = new float[WorkPixels];
        var opaque = new bool[WorkPixels];
        ConvertWorkToLab(work, L, a, b, alpha, opaque, out int opaqueCount);
        if (opaqueCount == 0)
        {
            return empty;
        }

        var diag = AnalyzeWork(L, a, b, opaque, opaqueCount);

        var tone = new float[WorkPixels];
        var outAlpha = new float[WorkPixels];

        if (diag.AvgChroma < 11f && diag.LumRange < 48f && (diag.PerimeterUniformity < 0.65f || diag.InteriorContrastFraction < 0.04f))
        {
            archetype = IconArchetype.MonochromeGlyph;
            RenderMonochrome(alpha, opaque, tone, outAlpha);
        }
        else if (diag.PerimeterUniformity >= 0.65f
            && diag.InteriorContrastFraction >= 0.04f
            && diag.InteriorContrastFraction <= 0.55f
            && TryHandleSolidPlate(
                work, L, a, b, alpha, opaque, opaqueCount, diag,
                tone, outAlpha, out var plateArchetype))
        {
            archetype = plateArchetype;
        }
        else if (diag.FillRatio >= 0.76f
            && diag.CornerFill >= 0.22f
            && diag.PerimeterUniformity < 0.58f
            && (diag.InteriorEdgeDensity > 16f || (diag.MinCornerFill >= 0.30f && diag.FillRatio >= 0.82f && diag.InteriorEdgeDensity > 11.5f)))
        {
            archetype = IconArchetype.DetailedArtwork;
            RenderDetailedArtwork(L, a, b, alpha, opaque, opaqueCount, tone, outAlpha);
        }
        else
        {
            archetype = IconArchetype.MultiColorVector;
            bool chamferSquare = diag.MinCornerFill >= 0.85f && diag.FillRatio >= 0.90f;
            RenderMultiColorVector(L, a, b, alpha, opaque, opaqueCount, chamferSquare, tone, outAlpha);
        }

        return DownsampleWorkToLogo(tone, outAlpha);
    }

    private static void SharpenLowResAlpha(float[] work)
    {
        for (int i = 0; i < WorkPixels; i++)
        {
            int idx = i * 4;
            float a = work[idx + 3];
            if (a <= 0f) continue;

            float sharpened = Math.Clamp(((a - 115f) * 1.85f) + 128f, 0f, 255f);
            if (sharpened < 20f)
            {
                work[idx] = 0;
                work[idx + 1] = 0;
                work[idx + 2] = 0;
                work[idx + 3] = 0;
            }
            else
            {
                work[idx + 3] = sharpened;
            }
        }
    }

    private readonly record struct WorkDiagnostics(
        float FillRatio,
        float CornerFill,
        float MinCornerFill,
        float PlateL,
        float PlateA,
        float PlateB,
        float PerimeterUniformity,
        float InteriorContrastFraction,
        float AvgChroma,
        float LumRange,
        float InteriorEdgeDensity,
        int[] DistFromTrans,
        bool[] InPerimeterRing);

    private static bool FindTightBounds(byte[] bgra, int width, int height, byte minAlpha, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = width;
        minY = height;
        maxX = -1;
        maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                if (bgra[row + (x * 4) + 3] < minAlpha) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        return maxX >= minX && maxY >= minY;
    }

    private static void ResampleRegionToWork(
        byte[]? byteBgra, float[]? floatRgba, int srcW,
        int minX, int minY, int maxX, int maxY, float[] dstWork)
    {
        int cropW = maxX - minX + 1;
        int cropH = maxY - minY + 1;
        float scale = MathF.Min((float)InnerSize / cropW, (float)InnerSize / cropH);
        float dstW = cropW * scale;
        float dstH = cropH * scale;
        float offsetX = (WorkSize - dstW) * 0.5f;
        float offsetY = (WorkSize - dstH) * 0.5f;
        int samplesPerAxis = scale < 1f ? Math.Clamp((int)MathF.Ceiling(1f / scale), 2, 4) : 1;

        for (int dy = 0; dy < WorkSize; dy++)
        {
            for (int dx = 0; dx < WorkSize; dx++)
            {
                float sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                int count = samplesPerAxis * samplesPerAxis;

                for (int sy = 0; sy < samplesPerAxis; sy++)
                {
                    float wy = dy + ((sy + 0.5f) / samplesPerAxis);
                    float srcY = ((wy - offsetY) / scale) - 0.5f;
                    for (int sx = 0; sx < samplesPerAxis; sx++)
                    {
                        float wx = dx + ((sx + 0.5f) / samplesPerAxis);
                        float srcX = ((wx - offsetX) / scale) - 0.5f;
                        SamplePremultipliedBilinear(
                            byteBgra, floatRgba, srcW, minX, minY, cropW, cropH, srcX, srcY,
                            out float r, out float g, out float b, out float a);
                        sumR += r;
                        sumG += g;
                        sumB += b;
                        sumA += a;
                    }
                }

                float avgA = sumA / count;
                int idx = ((dy * WorkSize) + dx) * 4;
                if (avgA < 20f)
                {
                    dstWork[idx] = 0;
                    dstWork[idx + 1] = 0;
                    dstWork[idx + 2] = 0;
                    dstWork[idx + 3] = 0;
                }
                else
                {
                    dstWork[idx] = Math.Clamp(sumR / sumA * 255f, 0f, 255f);
                    dstWork[idx + 1] = Math.Clamp(sumG / sumA * 255f, 0f, 255f);
                    dstWork[idx + 2] = Math.Clamp(sumB / sumA * 255f, 0f, 255f);
                    dstWork[idx + 3] = Math.Clamp(avgA, 0f, 255f);
                }
            }
        }
    }

    private static void SamplePremultipliedBilinear(
        byte[]? byteBgra, float[]? floatRgba, int srcW, int minX, int minY, int cropW, int cropH, float x, float y,
        out float premulR, out float premulG, out float premulB, out float alpha)
    {
        if (x < -1f || y < -1f || x > cropW || y > cropH)
        {
            premulR = premulG = premulB = alpha = 0;
            return;
        }

        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;

        Sample(x0, y0, (1f - fx) * (1f - fy), out float r00, out float g00, out float b00, out float a00);
        Sample(x0 + 1, y0, fx * (1f - fy), out float r10, out float g10, out float b10, out float a10);
        Sample(x0, y0 + 1, (1f - fx) * fy, out float r01, out float g01, out float b01, out float a01);
        Sample(x0 + 1, y0 + 1, fx * fy, out float r11, out float g11, out float b11, out float a11);

        premulR = r00 + r10 + r01 + r11;
        premulG = g00 + g10 + g01 + g11;
        premulB = b00 + b10 + b01 + b11;
        alpha = a00 + a10 + a01 + a11;

        void Sample(int cx, int cy, float w, out float r, out float g, out float b, out float a)
        {
            if (cx < 0 || cy < 0 || cx >= cropW || cy >= cropH)
            {
                r = g = b = a = 0;
                return;
            }

            int idx = (((minY + cy) * srcW) + (minX + cx)) * 4;
            if (byteBgra is not null)
            {
                float sa = byteBgra[idx + 3];
                float wa = w * (sa / 255f);
                b = byteBgra[idx] * wa;
                g = byteBgra[idx + 1] * wa;
                r = byteBgra[idx + 2] * wa;
                a = sa * w;
            }
            else
            {
                float sa = floatRgba![idx + 3];
                float wa = w * (sa / 255f);
                r = floatRgba[idx] * wa;
                g = floatRgba[idx + 1] * wa;
                b = floatRgba[idx + 2] * wa;
                a = sa * w;
            }
        }
    }

    private static void ConvertWorkToLab(float[] work, float[] L, float[] a, float[] b, float[] alpha, bool[] opaque, out int opaqueCount)
    {
        opaqueCount = 0;
        for (int i = 0; i < WorkPixels; i++)
        {
            int idx = i * 4;
            float al = work[idx + 3];
            alpha[i] = al;
            opaque[i] = al >= 24f;
            if (opaque[i]) opaqueCount++;

            Rgb255ToLab(work[idx], work[idx + 1], work[idx + 2], out L[i], out a[i], out b[i]);
        }
    }

    private static void Rgb255ToLab(float r255, float g255, float b255, out float lStar, out float aStar, out float bStar)
    {
        float r = SrgbToLinear(r255 / 255f);
        float g = SrgbToLinear(g255 / 255f);
        float b = SrgbToLinear(b255 / 255f);

        float x = ((0.4124564f * r) + (0.3575761f * g) + (0.1804375f * b)) / 0.95047f;
        float y = (0.2126729f * r) + (0.7151522f * g) + (0.0721750f * b);
        float z = ((0.0193339f * r) + (0.1191920f * g) + (0.9503041f * b)) / 1.08883f;

        float fx = LabF(x);
        float fy = LabF(y);
        float fz = LabF(z);

        lStar = (116f * fy) - 16f;
        aStar = 500f * (fx - fy);
        bStar = 200f * (fy - fz);
    }

    private static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float LabF(float t) => t > 0.0088564516f ? MathF.Cbrt(t) : ((7.787037f * t) + (16f / 116f));

    private static WorkDiagnostics AnalyzeWork(float[] L, float[] a, float[] b, bool[] opaque, int opaqueCount)
    {
        float fillRatio = opaqueCount / (float)(InnerSize * InnerSize);

        const int cornerSize = 16;
        int c00 = 0, c10 = 0, c01 = 0, c11 = 0;
        for (int y = Margin; y < Margin + cornerSize; y++)
        {
            for (int x = Margin; x < Margin + cornerSize; x++)
            {
                if (opaque[(y * WorkSize) + x]) c00++;
                if (opaque[(y * WorkSize) + (WorkSize - 1 - x)]) c10++;
                if (opaque[((WorkSize - 1 - y) * WorkSize) + x]) c01++;
                if (opaque[((WorkSize - 1 - y) * WorkSize) + (WorkSize - 1 - x)]) c11++;
            }
        }
        const float cornerArea = cornerSize * cornerSize;
        float minCornerFill = Math.Min(Math.Min(c00, c10), Math.Min(c01, c11)) / cornerArea;
        float cornerFill = (c00 + c10 + c01 + c11) / (4f * cornerArea);

        var dist = new int[WorkPixels];
        var queue = new int[WorkPixels];
        int head = 0, tail = 0;
        for (int i = 0; i < WorkPixels; i++)
        {
            if (!opaque[i])
            {
                dist[i] = 0;
                queue[tail++] = i;
            }
            else
            {
                int x = i % WorkSize;
                int y = i / WorkSize;
                if (x == 0 || x == WorkSize - 1 || y == 0 || y == WorkSize - 1)
                {
                    dist[i] = 1;
                    queue[tail++] = i;
                }
                else
                {
                    dist[i] = int.MaxValue;
                }
            }
        }

        ReadOnlySpan<int> dx4 = [-1, 1, 0, 0];
        ReadOnlySpan<int> dy4 = [0, 0, -1, 1];
        while (head < tail)
        {
            int cur = queue[head++];
            int cx = cur % WorkSize;
            int cy = cur / WorkSize;
            int nextDist = dist[cur] + 1;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx4[k];
                int ny = cy + dy4[k];
                if ((uint)nx >= WorkSize || (uint)ny >= WorkSize) continue;
                int ni = (ny * WorkSize) + nx;
                if (nextDist < dist[ni])
                {
                    dist[ni] = nextDist;
                    queue[tail++] = ni;
                }
            }
        }

        var inPerimeter = new bool[WorkPixels];
        int ringCount = 0;
        for (int i = 0; i < WorkPixels; i++)
        {
            if (opaque[i] && dist[i] >= 2 && dist[i] <= 6)
            {
                inPerimeter[i] = true;
                ringCount++;
            }
        }

        if (ringCount < 16)
        {
            ringCount = 0;
            for (int i = 0; i < WorkPixels; i++)
            {
                inPerimeter[i] = opaque[i] && dist[i] >= 1 && dist[i] <= 6;
                if (inPerimeter[i]) ringCount++;
            }
        }

        float plateL = 0, plateA = 0, plateB = 0;
        if (ringCount > 0)
        {
            var bins = new Dictionary<int, (int Count, float SumL, float SumA, float SumB)>();
            for (int i = 0; i < WorkPixels; i++)
            {
                if (!inPerimeter[i]) continue;
                int binL = (int)MathF.Floor(L[i] / 12f);
                int binA = (int)MathF.Floor((a[i] + 128f) / 16f);
                int binB = (int)MathF.Floor((b[i] + 128f) / 16f);
                int key = ((binL + 16) << 16) | ((binA + 32) << 8) | (binB + 32);
                bins.TryGetValue(key, out var entry);
                bins[key] = (entry.Count + 1, entry.SumL + L[i], entry.SumA + a[i], entry.SumB + b[i]);
            }

            int bestKey = 0;
            int bestCount = -1;
            foreach (var kv in bins)
            {
                if (kv.Value.Count > bestCount || (kv.Value.Count == bestCount && kv.Key < bestKey))
                {
                    bestCount = kv.Value.Count;
                    bestKey = kv.Key;
                }
            }

            var best = bins[bestKey];
            float seedL = best.SumL / best.Count;
            float seedA = best.SumA / best.Count;
            float seedB = best.SumB / best.Count;

            // 1-step local mean refinement avoids bin-boundary splits on smooth plates.
            float sumL = 0, sumA = 0, sumB = 0;
            int nearCount = 0;
            for (int i = 0; i < WorkPixels; i++)
            {
                if (!inPerimeter[i]) continue;
                if (LabDistSq(L[i], a[i], b[i], seedL, seedA, seedB) <= 22f * 22f)
                {
                    sumL += L[i];
                    sumA += a[i];
                    sumB += b[i];
                    nearCount++;
                }
            }

            if (nearCount > 0)
            {
                plateL = sumL / nearCount;
                plateA = sumA / nearCount;
                plateB = sumB / nearCount;
            }
            else
            {
                plateL = seedL;
                plateA = seedA;
                plateB = seedB;
            }
        }

        int uniformRing = 0;
        for (int i = 0; i < WorkPixels; i++)
        {
            if (inPerimeter[i] && LabDistSq(L[i], a[i], b[i], plateL, plateA, plateB) <= 21f * 21f)
            {
                uniformRing++;
            }
        }
        float perimeterUniformity = ringCount > 0 ? uniformRing / (float)ringCount : 0f;

        var lumValues = new float[opaqueCount];
        int lumIndex = 0;
        int interiorContrastCount = 0;
        double sumChroma = 0;
        for (int i = 0; i < WorkPixels; i++)
        {
            if (!opaque[i]) continue;
            lumValues[lumIndex++] = L[i];
            sumChroma += MathF.Sqrt((a[i] * a[i]) + (b[i] * b[i]));
            if (LabDistSq(L[i], a[i], b[i], plateL, plateA, plateB) > 28f * 28f)
            {
                interiorContrastCount++;
            }
        }

        Array.Sort(lumValues);
        float p05 = lumValues[(int)MathF.Floor((opaqueCount - 1) * 0.05f)];
        float p95 = lumValues[(int)MathF.Ceiling((opaqueCount - 1) * 0.95f)];
        float lumRange = p95 - p05;
        float avgChroma = (float)(sumChroma / opaqueCount);
        float interiorContrastFraction = interiorContrastCount / (float)opaqueCount;

        var edges = ComputeInteriorSobel(L, a, b, opaque, out float meanEdge);

        _ = edges;
        return new WorkDiagnostics(
            fillRatio,
            cornerFill,
            minCornerFill,
            plateL,
            plateA,
            plateB,
            perimeterUniformity,
            interiorContrastFraction,
            avgChroma,
            lumRange,
            meanEdge,
            dist,
            inPerimeter);
    }

    private static float[] ComputeInteriorSobel(float[] L, float[] a, float[] b, bool[] opaque, out float meanEdge)
    {
        var edgeMag = new float[WorkPixels];
        double sumEdge = 0;
        int interiorCount = 0;

        for (int y = 1; y < WorkSize - 1; y++)
        {
            for (int x = 1; x < WorkSize - 1; x++)
            {
                int i = (y * WorkSize) + x;
                if (!IsInterior3x3Opaque(opaque, x, y)) continue;

                float gL = SobelMagAt(L, x, y);
                float gA = SobelMagAt(a, x, y);
                float gB = SobelMagAt(b, x, y);
                float mag = MathF.Sqrt((gL * gL) + (gA * gA) + (gB * gB));
                edgeMag[i] = mag;
                sumEdge += mag;
                interiorCount++;
            }
        }

        meanEdge = interiorCount > 0 ? (float)(sumEdge / interiorCount) : 0f;
        return edgeMag;
    }

    private static bool IsInterior3x3Opaque(bool[] opaque, int x, int y)
    {
        int row0 = (y - 1) * WorkSize;
        int row1 = y * WorkSize;
        int row2 = (y + 1) * WorkSize;
        return opaque[row0 + x - 1] && opaque[row0 + x] && opaque[row0 + x + 1]
            && opaque[row1 + x - 1] && opaque[row1 + x] && opaque[row1 + x + 1]
            && opaque[row2 + x - 1] && opaque[row2 + x] && opaque[row2 + x + 1];
    }

    private static float SobelMagAt(float[] ch, int x, int y)
    {
        int row0 = (y - 1) * WorkSize;
        int row1 = y * WorkSize;
        int row2 = (y + 1) * WorkSize;

        float tl = ch[row0 + x - 1], t = ch[row0 + x], tr = ch[row0 + x + 1];
        float l = ch[row1 + x - 1], r = ch[row1 + x + 1];
        float bl = ch[row2 + x - 1], b = ch[row2 + x], br = ch[row2 + x + 1];

        // Normalized by 2 so crisp 1px contrast edges measure ~2x central difference without noise amplification.
        float gx = (-tl + tr - (2f * l) + (2f * r) - bl + br) * 0.5f;
        float gy = (-tl - (2f * t) - tr + bl + (2f * b) + br) * 0.5f;
        return MathF.Sqrt((gx * gx) + (gy * gy));
    }

    private static void RenderMonochrome(float[] alpha, bool[] opaque, float[] tone, float[] outAlpha)
    {
        for (int i = 0; i < WorkPixels; i++)
        {
            if (!opaque[i] && alpha[i] <= 0f) continue;
            tone[i] = 1.0f;
            outAlpha[i] = alpha[i];
        }
    }

    private static bool TryHandleSolidPlate(
        float[] work,
        float[] L,
        float[] a,
        float[] b,
        float[] alpha,
        bool[] opaque,
        int opaqueCount,
        WorkDiagnostics diag,
        float[] tone,
        float[] outAlpha,
        out IconArchetype archetype)
    {
        archetype = IconArchetype.IconicBadgeCutout;

        var outerPlate = new bool[WorkPixels];
        var queue = new int[WorkPixels];
        int head = 0, tail = 0;

        for (int i = 0; i < WorkPixels; i++)
        {
            if (diag.InPerimeterRing[i] && LabDistSq(L[i], a[i], b[i], diag.PlateL, diag.PlateA, diag.PlateB) <= 26f * 26f)
            {
                outerPlate[i] = true;
                queue[tail++] = i;
            }
        }

        ReadOnlySpan<int> dx4 = [-1, 1, 0, 0];
        ReadOnlySpan<int> dy4 = [0, 0, -1, 1];
        while (head < tail)
        {
            int cur = queue[head++];
            int cx = cur % WorkSize;
            int cy = cur / WorkSize;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx4[k];
                int ny = cy + dy4[k];
                if ((uint)nx >= WorkSize || (uint)ny >= WorkSize) continue;
                int ni = (ny * WorkSize) + nx;
                if (outerPlate[ni] || !opaque[ni]) continue;

                float distPlate = LabDistSq(L[ni], a[ni], b[ni], diag.PlateL, diag.PlateA, diag.PlateB);
                float stepDist = LabDistSq(L[ni], a[ni], b[ni], L[cur], a[cur], b[cur]);
                if (distPlate <= 36f * 36f && stepDist <= 12f * 12f)
                {
                    outerPlate[ni] = true;
                    queue[tail++] = ni;
                }
            }
        }

        var symWeight = new float[WorkPixels];
        int symCount = 0;
        int insetSymCount = 0;
        for (int i = 0; i < WorkPixels; i++)
        {
            if (!opaque[i] || outerPlate[i]) continue;
            float d = MathF.Sqrt(LabDistSq(L[i], a[i], b[i], diag.PlateL, diag.PlateA, diag.PlateB));
            float w = Math.Clamp((d - 16f) / 14f, 0f, 1f);
            symWeight[i] = w;
            if (w >= 0.5f)
            {
                symCount++;
                if (diag.DistFromTrans[i] >= 5) insetSymCount++;
            }
        }

        if (symCount < 16)
        {
            return false;
        }

        float symRatio = symCount / (float)opaqueCount;
        float symInsetRatio = insetSymCount / (float)symCount;
        // Require all 4 corners to resemble a symmetrical container tile AND the emblem to sit enclosed inside it,
        // preventing asymmetric folder/app silhouettes (like File Explorer's front flap) from being torn apart.
        if (diag.FillRatio >= 0.76f
            && diag.MinCornerFill >= 0.16f
            && diag.CornerFill >= 0.25f
            && symInsetRatio >= 0.82f
            && symRatio >= 0.05f
            && symRatio <= 0.72f)
        {
            archetype = IconArchetype.TileEmblemLift;
            return RenderTileEmblemLift(work, outerPlate, symWeight, alpha, tone, outAlpha);
        }

        archetype = IconArchetype.IconicBadgeCutout;
        RenderIconicBadgeCutout(L, a, b, alpha, opaque, outerPlate, symWeight, symCount, diag, tone, outAlpha);
        return true;
    }

    private static bool RenderTileEmblemLift(
        float[] srcWork,
        bool[] outerPlate,
        float[] symWeight,
        float[] alpha,
        float[] tone,
        float[] outAlpha)
    {
        var stripped = new float[WorkPixels * 4];
        int minX = WorkSize, minY = WorkSize, maxX = -1, maxY = -1;

        for (int y = 0; y < WorkSize; y++)
        {
            for (int x = 0; x < WorkSize; x++)
            {
                int i = (y * WorkSize) + x;
                if (outerPlate[i]) continue;
                float aNew = alpha[i] * symWeight[i];
                if (aNew < 20f) continue;

                int idx = i * 4;
                stripped[idx] = srcWork[idx];
                stripped[idx + 1] = srcWork[idx + 1];
                stripped[idx + 2] = srcWork[idx + 2];
                stripped[idx + 3] = aNew;

                if (aNew >= 24f)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return false;
        }

        var liftedWork = new float[WorkPixels * 4];
        ResampleRegionToWork(null, stripped, WorkSize, minX, minY, maxX, maxY, liftedWork);

        var liftedL = new float[WorkPixels];
        var liftedA = new float[WorkPixels];
        var liftedB = new float[WorkPixels];
        var liftedAlpha = new float[WorkPixels];
        var liftedOpaque = new bool[WorkPixels];
        ConvertWorkToLab(liftedWork, liftedL, liftedA, liftedB, liftedAlpha, liftedOpaque, out int liftedCount);
        if (liftedCount == 0)
        {
            return false;
        }

        // Measure chroma & luminance span on core interior pixels so anti-aliased tile fringe pixels
        // never invert a uniform dark lifted glyph (e.g. black note on white tile) into a dark grey blob.
        var coreLums = new List<float>(liftedCount);
        double coreChromaSum = 0;
        for (int y = 1; y < WorkSize - 1; y++)
        {
            for (int x = 1; x < WorkSize - 1; x++)
            {
                int i = (y * WorkSize) + x;
                if (!liftedOpaque[i] || liftedAlpha[i] < 200f) continue;
                if (liftedOpaque[i - 1] && liftedOpaque[i + 1] && liftedOpaque[i - WorkSize] && liftedOpaque[i + WorkSize])
                {
                    coreLums.Add(liftedL[i]);
                    coreChromaSum += MathF.Sqrt((liftedA[i] * liftedA[i]) + (liftedB[i] * liftedB[i]));
                }
            }
        }

        if (coreLums.Count < 24)
        {
            coreLums.Clear();
            coreChromaSum = 0;
            for (int i = 0; i < WorkPixels; i++)
            {
                if (!liftedOpaque[i]) continue;
                coreLums.Add(liftedL[i]);
                coreChromaSum += MathF.Sqrt((liftedA[i] * liftedA[i]) + (liftedB[i] * liftedB[i]));
            }
        }

        coreLums.Sort();
        float p10 = coreLums[(int)MathF.Floor((coreLums.Count - 1) * 0.10f)];
        float p90 = coreLums[(int)MathF.Ceiling((coreLums.Count - 1) * 0.90f)];
        float coreChroma = (float)(coreChromaSum / coreLums.Count);
        if (coreChroma < 11f && (p90 - p10) < 28f)
        {
            RenderMonochrome(liftedAlpha, liftedOpaque, tone, outAlpha);
        }
        else
        {
            RenderMultiColorVector(liftedL, liftedA, liftedB, liftedAlpha, liftedOpaque, liftedCount, chamferFullSquare: false, tone, outAlpha);
        }

        return true;
    }

    private static void RenderIconicBadgeCutout(
        float[] L,
        float[] a,
        float[] b,
        float[] alpha,
        bool[] opaque,
        bool[] outerPlate,
        float[] symWeight,
        int symCount,
        WorkDiagnostics diag,
        float[] tone,
        float[] outAlpha)
    {
        var isSymbol = new bool[WorkPixels];
        for (int i = 0; i < WorkPixels; i++)
        {
            isSymbol[i] = opaque[i] && !outerPlate[i] && symWeight[i] >= 0.5f;
        }

        // Erode the 1px transition rim touching the plate when clustering symbol tones so AA edge blending
        // does not mimic a second solid color around a single-tone glyph.
        var coreSymbol = new bool[WorkPixels];
        int coreCount = 0;
        for (int y = 1; y < WorkSize - 1; y++)
        {
            for (int x = 1; x < WorkSize - 1; x++)
            {
                int i = (y * WorkSize) + x;
                if (!isSymbol[i]) continue;
                if (isSymbol[i - 1] && isSymbol[i + 1] && isSymbol[i - WorkSize] && isSymbol[i + WorkSize])
                {
                    coreSymbol[i] = true;
                    coreCount++;
                }
            }
        }

        var clusterMask = coreCount >= 24 ? coreSymbol : isSymbol;
        int clusterMaskCount = coreCount >= 24 ? coreCount : symCount;
        BilateralFilterLab(L, a, b, isSymbol, passes: 2, radius: 2, sigmaS: 1.5f, sigmaR: 22.0f, out var smoothL, out var smoothA, out var smoothB);
        RunLabKMeans(smoothL, smoothA, smoothB, clusterMask, clusterMaskCount, 3, 10, 22f, 0.04f, out var coreAssign, out var centroids);

        bool singleToneSymbol = centroids.Count <= 1;
        if (centroids.Count == 2)
        {
            // Also collapse if the smaller cluster lies on the linear AA blend segment between C_plate and main cluster.
            bool firstIsMain = centroids[0].Count >= centroids[1].Count;
            var main = firstIsMain ? centroids[0] : centroids[1];
            var minor = firstIsMain ? centroids[1] : centroids[0];
            if (IsBetweenInLab(minor.L, minor.A, minor.B, diag.PlateL, diag.PlateA, diag.PlateB, main.L, main.A, main.B, maxPerpDist: 14f))
            {
                singleToneSymbol = true;
            }
        }

        if (singleToneSymbol)
        {
            for (int i = 0; i < WorkPixels; i++)
            {
                if (alpha[i] <= 0f) continue;
                tone[i] = 1.0f;
                outAlpha[i] = outerPlate[i] ? alpha[i] : alpha[i] * (1f - symWeight[i]);
            }
        }
        else
        {
            var assign = coreCount >= 24
                ? AssignAllToCentroids(smoothL, smoothA, smoothB, isSymbol, centroids)
                : coreAssign;
            var clusterTones = AssignClusterTones(centroids, centroids.Count switch
            {
                2 => [0.36f, 0.62f],
                _ => [0.30f, 0.48f, 0.66f],
            });

            for (int i = 0; i < WorkPixels; i++)
            {
                if (alpha[i] <= 0f) continue;
                if (isSymbol[i])
                {
                    int cid = assign[i];
                    float symTone = cid >= 0 ? clusterTones[cid] : 0.45f;
                    tone[i] = 1.0f + ((symTone - 1.0f) * symWeight[i]);
                }
                else
                {
                    tone[i] = 1.0f;
                }
                outAlpha[i] = alpha[i];
            }

            // Carve a ~1.5px translucent separator between the solid outer badge and the multi-part inner symbol.
            for (int y = 1; y < WorkSize - 1; y++)
            {
                for (int x = 1; x < WorkSize - 1; x++)
                {
                    int i = (y * WorkSize) + x;
                    if (!opaque[i]) continue;
                    bool selfSym = isSymbol[i];
                    bool boundary = false;
                    for (int dy = -1; dy <= 1 && !boundary; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int ni = ((y + dy) * WorkSize) + (x + dx);
                            if (opaque[ni] && isSymbol[ni] != selfSym)
                            {
                                boundary = true;
                                break;
                            }
                        }
                    }

                    if (boundary && selfSym)
                    {
                        outAlpha[i] *= 0.12f;
                    }
                    else if (selfSym && HasDifferentNeighborCluster(assign, L, a, b, opaque, x, y, 18f))
                    {
                        outAlpha[i] *= 0.14f;
                    }
                }
            }
        }

        if (diag.CornerFill >= 0.85f && diag.FillRatio >= 0.90f)
        {
            ApplyChamferClip(opaque, tone, outAlpha, drawBorderHairline: false);
        }
    }

    private static void RenderDetailedArtwork(
        float[] rawL,
        float[] rawA,
        float[] rawB,
        float[] alpha,
        bool[] opaque,
        int opaqueCount,
        float[] tone,
        float[] outAlpha)
    {
        BilateralFilterLab(rawL, rawA, rawB, opaque, passes: 3, radius: 2, sigmaS: 2.0f, sigmaR: 30.0f, out var smoothL, out var smoothA, out var smoothB);
        RunLabKMeans(smoothL, smoothA, smoothB, opaque, opaqueCount, 3, 10, 14f, 0.02f, out var assign, out var centroids);

        float[] palette = centroids.Count switch
        {
            1 => [0.70f],
            2 => [0.36f, 0.96f],
            _ => [0.36f, 0.70f, 1.00f],
        };
        var clusterTones = AssignClusterTones(centroids, palette);

        for (int i = 0; i < WorkPixels; i++)
        {
            if (alpha[i] <= 0f) continue;
            int cid = assign[i];
            if (cid < 0) cid = NearestOpaqueCluster(assign, opaque, i % WorkSize, i / WorkSize);
            tone[i] = cid >= 0 ? clusterTones[cid] : 0.70f;
            outAlpha[i] = alpha[i];
        }

        var edges = ComputeInteriorSobel(smoothL, smoothA, smoothB, opaque, out _);
        var interiorList = new List<float>(opaqueCount);
        for (int i = 0; i < WorkPixels; i++)
        {
            if (edges[i] > 0f) interiorList.Add(edges[i]);
        }

        if (interiorList.Count >= 16)
        {
            interiorList.Sort();
            float p88 = interiorList[(int)MathF.Floor((interiorList.Count - 1) * 0.88f)];
            if (p88 > 1e-3f)
            {
                float denom = MathF.Max(p88 * 0.6f, 1f);
                for (int i = 0; i < WorkPixels; i++)
                {
                    if (edges[i] <= p88 || !opaque[i]) continue;
                    float ink = Math.Clamp((edges[i] - p88) / denom, 0f, 1f);
                    tone[i] = (tone[i] * (1f - ink)) + (0.28f * ink);
                }
            }
        }

        ApplyChamferClip(opaque, tone, outAlpha, drawBorderHairline: true);
    }

    private static void RenderMultiColorVector(
        float[] rawL,
        float[] rawA,
        float[] rawB,
        float[] alpha,
        bool[] opaque,
        int opaqueCount,
        bool chamferFullSquare,
        float[] tone,
        float[] outAlpha)
    {
        BilateralFilterLab(rawL, rawA, rawB, opaque, passes: 2, radius: 2, sigmaS: 1.8f, sigmaR: 22.0f, out var smoothL, out var smoothA, out var smoothB);
        RunLabKMeans(smoothL, smoothA, smoothB, opaque, opaqueCount, 3, 10, 16f, 0.02f, out var assign, out var centroids);

        float[] palette = centroids.Count switch
        {
            1 => [1.00f],
            2 => [0.46f, 1.00f],
            _ => [0.38f, 0.70f, 1.00f],
        };
        var clusterTones = AssignClusterTones(centroids, palette);

        for (int i = 0; i < WorkPixels; i++)
        {
            if (alpha[i] <= 0f) continue;
            int cid = assign[i];
            if (cid < 0)
            {
                // Anti-aliased halo outside the opaque threshold borrows its nearest opaque neighbor's cluster.
                cid = NearestOpaqueCluster(assign, opaque, i % WorkSize, i / WorkSize);
            }
            tone[i] = cid >= 0 ? clusterTones[cid] : 1.00f;
            outAlpha[i] = alpha[i];
        }

        // Chromatic seam carving: separate abutting equal-luminance color regions with a crisp 1.5px notch.
        for (int y = 1; y < WorkSize - 1; y++)
        {
            for (int x = 1; x < WorkSize - 1; x++)
            {
                int i = (y * WorkSize) + x;
                if (!opaque[i]) continue;
                if (HasDifferentNeighborCluster(assign, rawL, rawA, rawB, opaque, x, y, 18f))
                {
                    outAlpha[i] *= 0.14f;
                }
            }
        }

        if (chamferFullSquare)
        {
            ApplyChamferClip(opaque, tone, outAlpha, drawBorderHairline: false);
        }
    }

    private static bool HasDifferentNeighborCluster(int[] assign, float[] rawL, float[] rawA, float[] rawB, bool[] opaque, int x, int y, float minLabDist)
    {
        int i = (y * WorkSize) + x;
        int self = assign[i];
        if (self < 0) return false;

        int left = i - 1;
        int right = i + 1;
        int up = i - WorkSize;
        int down = i + WorkSize;
        if (!opaque[left] || !opaque[right] || !opaque[up] || !opaque[down])
        {
            return false;
        }

        float threshSq = minLabDist * minLabDist;
        ReadOnlySpan<int> neighbors = [left, right, up, down];
        foreach (int ni in neighbors)
        {
            int other = assign[ni];
            if (other >= 0 && other != self && LabDistSq(rawL[i], rawA[i], rawB[i], rawL[ni], rawA[ni], rawB[ni]) > threshSq)
            {
                return true;
            }
        }

        return false;
    }

    private static int NearestOpaqueCluster(int[] assign, bool[] opaque, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            int ny = y + dy;
            if ((uint)ny >= WorkSize) continue;
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                if ((uint)nx >= WorkSize) continue;
                int ni = (ny * WorkSize) + nx;
                if (opaque[ni] && assign[ni] >= 0) return assign[ni];
            }
        }
        return -1;
    }

    private static void ApplyChamferClip(bool[] opaque, float[] tone, float[] outAlpha, bool drawBorderHairline)
    {
        int minX = WorkSize, minY = WorkSize, maxX = -1, maxY = -1;
        for (int y = 0; y < WorkSize; y++)
        {
            for (int x = 0; x < WorkSize; x++)
            {
                if (!opaque[(y * WorkSize) + x]) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX <= minX || maxY <= minY) return;

        const float chamfer = 18f;
        const float invSqrt2 = 0.70710678f;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int i = (y * WorkSize) + x;
                if (outAlpha[i] <= 0f) continue;

                float dLeft = x - (minX - 0.5f);
                float dRight = (maxX + 0.5f) - x;
                float dTop = y - (minY - 0.5f);
                float dBottom = (maxY + 0.5f) - y;
                float dTR = (((maxX + 0.5f) - x) + (y - (minY - 0.5f)) - chamfer) * invSqrt2;
                float dBL = ((x - (minX - 0.5f)) + ((maxY + 0.5f) - y) - chamfer) * invSqrt2;

                float dPoly = MathF.Min(MathF.Min(dLeft, dRight), MathF.Min(MathF.Min(dTop, dBottom), MathF.Min(dTR, dBL)));
                float clip = Math.Clamp(dPoly + 0.5f, 0f, 1f);
                outAlpha[i] *= clip;

                if (drawBorderHairline && dPoly >= 0f && dPoly <= 2.2f && outAlpha[i] > 0f)
                {
                    tone[i] = 1.00f;
                }
            }
        }
    }

    private static void BilateralFilterLab(
        float[] srcL,
        float[] srcA,
        float[] srcB,
        bool[] mask,
        int passes,
        int radius,
        float sigmaS,
        float sigmaR,
        out float[] outL,
        out float[] outA,
        out float[] outB)
    {
        outL = (float[])srcL.Clone();
        outA = (float[])srcA.Clone();
        outB = (float[])srcB.Clone();
        var nextL = new float[WorkPixels];
        var nextA = new float[WorkPixels];
        var nextB = new float[WorkPixels];

        int side = (2 * radius) + 1;
        var spatial = new float[side * side];
        float invTwoS2 = 1f / (2f * sigmaS * sigmaS);
        float invTwoR2 = 1f / (2f * sigmaR * sigmaR);
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                spatial[((dy + radius) * side) + (dx + radius)] = MathF.Exp(-((dx * dx) + (dy * dy)) * invTwoS2);
            }
        }

        for (int pass = 0; pass < passes; pass++)
        {
            for (int y = 0; y < WorkSize; y++)
            {
                for (int x = 0; x < WorkSize; x++)
                {
                    int i = (y * WorkSize) + x;
                    if (!mask[i])
                    {
                        nextL[i] = outL[i];
                        nextA[i] = outA[i];
                        nextB[i] = outB[i];
                        continue;
                    }

                    float cL = outL[i], cA = outA[i], cB = outB[i];
                    float sumW = 0, sumL = 0, sumA = 0, sumB = 0;

                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int ny = y + dy;
                        if ((uint)ny >= WorkSize) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = x + dx;
                            if ((uint)nx >= WorkSize) continue;
                            int ni = (ny * WorkSize) + nx;
                            if (!mask[ni]) continue;

                            float d2 = LabDistSq(outL[ni], outA[ni], outB[ni], cL, cA, cB);
                            float w = spatial[((dy + radius) * side) + (dx + radius)] * MathF.Exp(-d2 * invTwoR2);
                            sumW += w;
                            sumL += w * outL[ni];
                            sumA += w * outA[ni];
                            sumB += w * outB[ni];
                        }
                    }

                    if (sumW > 0f)
                    {
                        nextL[i] = sumL / sumW;
                        nextA[i] = sumA / sumW;
                        nextB[i] = sumB / sumW;
                    }
                    else
                    {
                        nextL[i] = cL;
                        nextA[i] = cA;
                        nextB[i] = cB;
                    }
                }
            }

            (outL, nextL) = (nextL, outL);
            (outA, nextA) = (nextA, outA);
            (outB, nextB) = (nextB, outB);
        }
    }

    private readonly record struct LabCentroid(float L, float A, float B, int Count);

    private static void RunLabKMeans(
        float[] L,
        float[] a,
        float[] b,
        bool[] mask,
        int maskCount,
        int maxClusters,
        int maxIters,
        float mergeDist,
        float minFraction,
        out int[] assign,
        out List<LabCentroid> centroids)
    {
        assign = new int[WorkPixels];
        Array.Fill(assign, -1);
        centroids = new List<LabCentroid>(maxClusters);
        if (maskCount <= 0) return;

        // Deterministic farthest-point K-Means++ initialization anchored on the brightest chromatic pixel.
        int seed0 = -1;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < WorkPixels; i++)
        {
            if (!mask[i]) continue;
            float chroma = MathF.Sqrt((a[i] * a[i]) + (b[i] * b[i]));
            float score = L[i] + (0.5f * chroma);
            if (score > bestScore)
            {
                bestScore = score;
                seed0 = i;
            }
        }

        if (seed0 < 0) return;

        var seedsL = new float[maxClusters];
        var seedsA = new float[maxClusters];
        var seedsB = new float[maxClusters];
        seedsL[0] = L[seed0];
        seedsA[0] = a[seed0];
        seedsB[0] = b[seed0];
        int kCount = 1;

        var minDistSq = new float[WorkPixels];
        Array.Fill(minDistSq, float.MaxValue);

        while (kCount < maxClusters)
        {
            float lastL = seedsL[kCount - 1];
            float lastA = seedsA[kCount - 1];
            float lastB = seedsB[kCount - 1];
            int farthest = -1;
            float maxD2 = -1f;

            for (int i = 0; i < WorkPixels; i++)
            {
                if (!mask[i]) continue;
                float d2 = LabDistSq(L[i], a[i], b[i], lastL, lastA, lastB);
                if (d2 < minDistSq[i]) minDistSq[i] = d2;
                if (minDistSq[i] > maxD2)
                {
                    maxD2 = minDistSq[i];
                    farthest = i;
                }
            }

            if (farthest < 0 || maxD2 <= 1f) break;
            seedsL[kCount] = L[farthest];
            seedsA[kCount] = a[farthest];
            seedsB[kCount] = b[farthest];
            kCount++;
        }

        var counts = new int[kCount];
        var sumL = new double[kCount];
        var sumA = new double[kCount];
        var sumB = new double[kCount];

        for (int iter = 0; iter < maxIters; iter++)
        {
            Array.Clear(counts);
            Array.Clear(sumL);
            Array.Clear(sumA);
            Array.Clear(sumB);
            bool changed = false;

            for (int i = 0; i < WorkPixels; i++)
            {
                if (!mask[i]) continue;
                int bestK = 0;
                float bestD2 = LabDistSq(L[i], a[i], b[i], seedsL[0], seedsA[0], seedsB[0]);
                for (int k = 1; k < kCount; k++)
                {
                    float d2 = LabDistSq(L[i], a[i], b[i], seedsL[k], seedsA[k], seedsB[k]);
                    if (d2 < bestD2)
                    {
                        bestD2 = d2;
                        bestK = k;
                    }
                }

                if (assign[i] != bestK)
                {
                    assign[i] = bestK;
                    changed = true;
                }

                counts[bestK]++;
                sumL[bestK] += L[i];
                sumA[bestK] += a[i];
                sumB[bestK] += b[i];
            }

            for (int k = 0; k < kCount; k++)
            {
                if (counts[k] == 0) continue;
                seedsL[k] = (float)(sumL[k] / counts[k]);
                seedsA[k] = (float)(sumA[k] / counts[k]);
                seedsB[k] = (float)(sumB[k] / counts[k]);
            }

            if (!changed) break;
        }

        // Prune tiny or nearly identical clusters so noise does not split a flat color into multiple tones.
        var active = new bool[kCount];
        for (int k = 0; k < kCount; k++) active[k] = counts[k] > 0;

        int minPixels = Math.Max(4, (int)(maskCount * minFraction));
        while (CountActive(active) > 1)
        {
            int victim = -1;
            int target = -1;
            float victimD2 = float.MaxValue;

            // First absorb clusters below minimum pixel share into their nearest surviving centroid.
            for (int k = 0; k < kCount; k++)
            {
                if (!active[k] || counts[k] >= minPixels) continue;
                int nearest = FindNearestActive(seedsL, seedsA, seedsB, active, k);
                if (nearest >= 0)
                {
                    victim = k;
                    target = nearest;
                    break;
                }
            }

            // Next merge closest pair under mergeDist.
            if (victim < 0)
            {
                float threshSq = mergeDist * mergeDist;
                for (int i = 0; i < kCount; i++)
                {
                    if (!active[i]) continue;
                    for (int j = i + 1; j < kCount; j++)
                    {
                        if (!active[j]) continue;
                        float d2 = LabDistSq(seedsL[i], seedsA[i], seedsB[i], seedsL[j], seedsA[j], seedsB[j]);
                        if (d2 < threshSq && d2 < victimD2)
                        {
                            victimD2 = d2;
                            if (counts[i] >= counts[j])
                            {
                                target = i;
                                victim = j;
                            }
                            else
                            {
                                target = j;
                                victim = i;
                            }
                        }
                    }
                }
            }

            if (victim < 0 || target < 0) break;

            int mergedCount = counts[target] + counts[victim];
            seedsL[target] = ((seedsL[target] * counts[target]) + (seedsL[victim] * counts[victim])) / mergedCount;
            seedsA[target] = ((seedsA[target] * counts[target]) + (seedsA[victim] * counts[victim])) / mergedCount;
            seedsB[target] = ((seedsB[target] * counts[target]) + (seedsB[victim] * counts[victim])) / mergedCount;
            counts[target] = mergedCount;
            counts[victim] = 0;
            active[victim] = false;
        }

        for (int k = 0; k < kCount; k++)
        {
            if (active[k])
            {
                centroids.Add(new LabCentroid(seedsL[k], seedsA[k], seedsB[k], counts[k]));
            }
        }

        // Re-assign all masked pixels against final surviving centroids cleanly.
        assign = AssignAllToCentroids(L, a, b, mask, centroids);
    }

    private static int CountActive(bool[] active)
    {
        int n = 0;
        for (int i = 0; i < active.Length; i++)
        {
            if (active[i]) n++;
        }
        return n;
    }

    private static int FindNearestActive(float[] sL, float[] sA, float[] sB, bool[] active, int self)
    {
        int best = -1;
        float bestD2 = float.MaxValue;
        for (int k = 0; k < active.Length; k++)
        {
            if (k == self || !active[k]) continue;
            float d2 = LabDistSq(sL[self], sA[self], sB[self], sL[k], sA[k], sB[k]);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = k;
            }
        }
        return best;
    }

    private static float[] AssignClusterTones(List<LabCentroid> centroids, float[] palette)
    {
        var order = Enumerable.Range(0, centroids.Count)
            .OrderBy(i =>
            {
                var c = centroids[i];
                float chroma = MathF.Sqrt((c.A * c.A) + (c.B * c.B));
                return (0.72f * c.L) + (0.28f * chroma) + (0.04f * (c.A - c.B));
            })
            .ToArray();

        var tones = new float[centroids.Count];
        for (int rank = 0; rank < order.Length; rank++)
        {
            int paletteIdx = Math.Clamp(rank, 0, palette.Length - 1);
            tones[order[rank]] = palette[paletteIdx];
        }
        return tones;
    }

    private static bool IsBetweenInLab(
        float pL, float pA, float pB,
        float aL, float aA, float aB,
        float bL, float bA, float bB,
        float maxPerpDist)
    {
        float abL = bL - aL, abA = bA - aA, abB = bB - aB;
        float ab2 = (abL * abL) + (abA * abA) + (abB * abB);
        if (ab2 < 1f) return true;

        float apL = pL - aL, apA = pA - aA, apB = pB - aB;
        float t = ((apL * abL) + (apA * abA) + (apB * abB)) / ab2;
        if (t <= 0f || t >= 1f) return false;

        float projL = aL + (t * abL), projA = aA + (t * abA), projB = aB + (t * abB);
        return LabDistSq(pL, pA, pB, projL, projA, projB) <= maxPerpDist * maxPerpDist;
    }

    private static int[] AssignAllToCentroids(float[] L, float[] a, float[] b, bool[] mask, List<LabCentroid> centroids)
    {
        var assign = new int[WorkPixels];
        Array.Fill(assign, -1);
        for (int i = 0; i < WorkPixels; i++)
        {
            if (!mask[i]) continue;
            int bestC = 0;
            float bestD2 = LabDistSq(L[i], a[i], b[i], centroids[0].L, centroids[0].A, centroids[0].B);
            for (int c = 1; c < centroids.Count; c++)
            {
                float d2 = LabDistSq(L[i], a[i], b[i], centroids[c].L, centroids[c].A, centroids[c].B);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestC = c;
                }
            }
            assign[i] = bestC;
        }
        return assign;
    }

    private static float LabDistSq(float l1, float a1, float b1, float l2, float a2, float b2)
    {
        float dl = l1 - l2;
        float da = a1 - a2;
        float db = b1 - b2;
        return (dl * dl) + (da * da) + (db * db);
    }

    private static byte[] DownsampleWorkToLogo(float[] tone, float[] outAlpha)
    {
        var result = new byte[LogoPixels * LogoPixels * 4];
        const float ratio = (float)WorkSize / LogoPixels;

        for (int dy = 0; dy < LogoPixels; dy++)
        {
            float sy0 = dy * ratio;
            float sy1 = (dy + 1) * ratio;
            int iy0 = (int)MathF.Floor(sy0);
            int iy1 = Math.Min(WorkSize - 1, (int)MathF.Ceiling(sy1) - 1);

            for (int dx = 0; dx < LogoPixels; dx++)
            {
                float sx0 = dx * ratio;
                float sx1 = (dx + 1) * ratio;
                int ix0 = (int)MathF.Floor(sx0);
                int ix1 = Math.Min(WorkSize - 1, (int)MathF.Ceiling(sx1) - 1);

                float sumR = 0, sumG = 0, sumB = 0, sumA = 0, sumW = 0;

                for (int sy = iy0; sy <= iy1; sy++)
                {
                    float wy = MathF.Min(sy1, sy + 1f) - MathF.Max(sy0, sy);
                    if (wy <= 0f) continue;

                    for (int sx = ix0; sx <= ix1; sx++)
                    {
                        float wx = MathF.Min(sx1, sx + 1f) - MathF.Max(sx0, sx);
                        if (wx <= 0f) continue;

                        float w = wx * wy;
                        int si = (sy * WorkSize) + sx;
                        float al = Math.Clamp(outAlpha[si] / 255f, 0f, 1f);
                        float wa = w * al;
                        float t = Math.Clamp(tone[si], 0f, 1f);

                        sumR += wa * (CardR * t);
                        sumG += wa * (CardG * t);
                        sumB += wa * (CardB * t);
                        sumA += wa;
                        sumW += w;
                    }
                }

                float avgA = sumW > 0f ? (sumA / sumW) * 255f : 0f;
                int di = ((dy * LogoPixels) + dx) * 4;
                if (avgA < 0.5f || sumA <= 1e-6f)
                {
                    result[di] = 0;
                    result[di + 1] = 0;
                    result[di + 2] = 0;
                    result[di + 3] = 0;
                }
                else
                {
                    result[di] = (byte)Math.Clamp((int)MathF.Round(sumB / sumA), 0, 255);
                    result[di + 1] = (byte)Math.Clamp((int)MathF.Round(sumG / sumA), 0, 255);
                    result[di + 2] = (byte)Math.Clamp((int)MathF.Round(sumR / sumA), 0, 255);
                    result[di + 3] = (byte)Math.Clamp((int)MathF.Round(avgA), 0, 255);
                }
            }
        }

        return result;
    }
}
