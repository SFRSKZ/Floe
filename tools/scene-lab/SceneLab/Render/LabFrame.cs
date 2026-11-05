using System.IO;
using System.Windows.Media.Imaging;

namespace SceneLab.Render;

/// <summary>A saved frame in the lab: the pixels plus the height at which the dock covers the strip.</summary>
public sealed record LabFrame(byte[] Pixels, int Width, int Height, float DockLeft, float DockRight)
{
    public BitmapSource ToImage() => Frames.ToBitmap(Pixels, Width, Height);
}

/// <summary>
/// The "with the dock on" preview: the evidence frame with the dock capsule drawn as a solid block over the
/// strip's middle, so a designer sees at once whether the composition survives the gap it will sit behind.
/// This is a preview mark, never part of a scene's output.
/// </summary>
public static class DockPreview
{
    public static void Draw(byte[] pixels, int width, int height, float left, float right, bool filled)
    {
        int x0 = (int)(left * width);
        int x1 = (int)(right * width);
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = x0; x < x1; x++)
            {
                int i = row + (x * 4);
                if (filled)
                {
                    // the dock's dark capsule: the strip behind it reads as "hidden by the dock"
                    pixels[i] = (byte)(pixels[i] * 0.12);
                    pixels[i + 1] = (byte)(pixels[i + 1] * 0.12);
                    pixels[i + 2] = (byte)(pixels[i + 2] * 0.12);
                }
                else if ((x - x0) < 2 || (x1 - x) <= 2)
                {
                    // the gap's edge, a hairline, so the empty middle is visible without hiding anything
                    pixels[i] = 0xFF;
                    pixels[i + 1] = 0x3C;
                    pixels[i + 2] = 0x00;
                }
            }
        }
    }
}
