using Floe.App.Effects.Gpu;
using Color = System.Windows.Media.Color;

namespace SceneLab.Scenes;

/// <summary>
/// Harness check, not a concept: 24 mirrored columns, one per band, straight from the analyser. It exists to
/// prove the whole chain — device, strip geometry, palette, mock or WAV audio, frames, report — before a
/// designer trusts the lab. If this moves with the beat, the lab is wired correctly.
/// </summary>
[SceneDoc("diagnostic: bare band columns, proves the audio chain (not a shipping look)")]
public sealed class HarnessCheck : FeedbackGpuScene
{
    private const int ConstantFloats = 48; // a multiple of 4: the constant buffer is 16-byte aligned

    public HarnessCheck() : base(ConstantFloats, Shared + FieldPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        // the field is stateless: the show pass draws the columns straight from the bands
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = frame.Time;
        d[5] = frame.Audio.Level;
        d[6] = frame.Audio.Bass;
        d[7] = frame.Audio.Treble;
        PutBands(8, frame);
        Put(32, Color.FromRgb(0xFC, 0xEE, 0x0A)); // Core sits right after the 24 band floats
    }

    private const string Shared = """
        cbuffer Check : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Level;
            float Bass;
            float Treble;
            float4 Bands[6];
            float4 Core;
        };

        """ + ShaderHelpers;

    private const string FieldPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            return float4(0, 0, 0, 1);
        }
        """;

    private const string ShowPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float u = abs(uv.x - .5) * 2;                 // 0 in the middle, 1 at the ends
            float column = floor(u * 24);
            float band = Band(column / 23);
            float height = saturate(.04 + (.92 * band));
            float on = step(uv.y, height);
            // a hard dark edge under the bright core keeps it readable over a bright wallpaper
            float edge = step(height, uv.y) * step(uv.y, height + (1 / Size.y));
            float3 colour = lerp(Core.rgb, float3(1, 1, 1), .25 * band);
            return float4(on * colour * Fade, max(on, edge) * Fade);
        }
        """;
}
