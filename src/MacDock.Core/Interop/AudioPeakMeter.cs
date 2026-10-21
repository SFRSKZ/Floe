using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

/// <summary>
/// Reads the peak level of the default render endpoint through WASAPI
/// (IAudioMeterInformation). Values are 0..1 and silence is reported whenever
/// the endpoint cannot be read, so callers never have to handle failures.
/// <para>
/// The instance may be created and sampled on any thread. The MMDevice API is
/// free-threaded and every call here is serialized by an internal lock, so an
/// instance created on the WPF UI (STA) thread is fine; sharing it with an MTA
/// thread also works (COM marshals between the apartments), it is just not
/// optimized for that.
/// </para>
/// </summary>
public sealed class AudioPeakMeter : IDisposable
{
    private const int ERender = 0;
    private const int EMultimedia = 1;
    private const uint ClsCtxAll = 23; // CLSCTX_ALL
    private const int MaxChannels = 64;
    private const long ReacquireIntervalMs = 1000;

    private static readonly Guid AudioMeterInformationIid = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioMeterInformation? _meter;
    private float[] _channelBuffer = Array.Empty<float>();
    private int _channelCount;
    private long _lastAcquireMs = -ReacquireIntervalMs;
    private bool _disposed;

    /// <summary>Peak across all channels, 0..1. Returns 0 on any failure.</summary>
    public float GetPeak()
    {
        lock (_gate)
        {
            if (!TryGetMeter(out var meter))
            {
                return 0f;
            }

            try
            {
                if (meter.GetPeakValue(out float peak) < 0)
                {
                    DropEndpoint();
                    return 0f;
                }

                return Sanitize(peak);
            }
            catch
            {
                DropEndpoint();
                return 0f;
            }
        }
    }

    /// <summary>
    /// Copies per-channel peaks (normally left, right), 0..1, into
    /// <paramref name="destination"/> and returns how many values were written;
    /// 0 on any failure. The channel count is cached until the endpoint changes,
    /// so a steady sample loop allocates nothing.
    /// </summary>
    public int GetChannelPeaks(float[] destination)
    {
        if (destination.Length == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            if (!TryGetMeter(out var meter) || !TryGetChannelCount(meter, out int count))
            {
                return 0;
            }

            try
            {
                if (_channelBuffer.Length < count)
                {
                    _channelBuffer = new float[count];
                }

                var buffer = _channelBuffer;
                if (meter.GetChannelsPeakValues((uint)count, buffer) < 0)
                {
                    DropEndpoint();
                    return 0;
                }

                int written = Math.Min(count, destination.Length);
                for (int i = 0; i < written; i++)
                {
                    destination[i] = Sanitize(buffer[i]);
                }

                return written;
            }
            catch
            {
                DropEndpoint();
                return 0;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseAll();
        }
    }

    /// <summary>
    /// Returns the cached meter, acquiring the current default render device at
    /// most once per second. A failed call drops the cached device so the next
    /// sample picks up a newly selected endpoint.
    /// </summary>
    private bool TryGetMeter(out IAudioMeterInformation meter)
    {
        meter = null!;
        if (_disposed)
        {
            return false;
        }

        if (_meter is not null)
        {
            meter = _meter;
            return true;
        }

        long now = _clock.ElapsedMilliseconds;
        if (now - _lastAcquireMs < ReacquireIntervalMs)
        {
            return false;
        }

        _lastAcquireMs = now;
        try
        {
            _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();

            if (_enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out var device) < 0 || device is null)
            {
                return false;
            }

            _device = device;
            var iid = AudioMeterInformationIid;
            if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object? activated) < 0 ||
                activated is not IAudioMeterInformation activatedMeter)
            {
                DropEndpoint();
                return false;
            }

            _meter = activatedMeter;
            meter = activatedMeter;
            return true;
        }
        catch
        {
            ReleaseAll();
            return false;
        }
    }

    /// <summary>
    /// Channel count of the current endpoint, asked once per endpoint: a new
    /// device can change it, so it is dropped together with the meter.
    /// </summary>
    private bool TryGetChannelCount(IAudioMeterInformation meter, out int count)
    {
        count = _channelCount;
        if (count > 0)
        {
            return true;
        }

        if (meter.GetMeteringChannelCount(out uint channelCount) < 0 ||
            channelCount == 0 || channelCount > MaxChannels)
        {
            DropEndpoint();
            return false;
        }

        count = _channelCount = (int)channelCount;
        return true;
    }

    /// <summary>Drops the endpoint/meter but keeps the enumerator for a quick re-acquire.</summary>
    private void DropEndpoint()
    {
        Release(ref _meter);
        Release(ref _device);
        _channelBuffer = Array.Empty<float>();
        _channelCount = 0;
    }

    private void ReleaseAll()
    {
        DropEndpoint();
        Release(ref _enumerator);
    }

    private static void Release<T>(ref T? comObject) where T : class
    {
        var instance = comObject;
        comObject = null;
        if (instance is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(instance))
            {
                Marshal.ReleaseComObject(instance);
            }
        }
        catch
        {
            // Releasing a dead COM proxy must never surface.
        }
    }

    private static float Sanitize(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

    private const string EnumeratorClsid = "BCDE0395-E52F-467C-8E3D-C4579291692E";

    [ComImport]
    [Guid(EnumeratorClsid)]
    [ClassInterface(ClassInterfaceType.None)]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection? devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

        [PreserveSig]
        int OpenPropertyStore(uint access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float peak);

        [PreserveSig]
        int GetMeteringChannelCount(out uint channelCount);

        [PreserveSig]
        int GetChannelsPeakValues(uint channelCount,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peakValues);

        [PreserveSig]
        int QueryHardwareSupport(out uint mask);
    }
}
