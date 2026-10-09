using System.Runtime.InteropServices;

namespace SpectrumKlinePlayer;

public sealed class AudioLoopbackCapture : IDisposable
{
    private const int CLSCTX_ALL = 23;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    private const ushort WAVE_FORMAT_IEEE_FLOAT = 0x0003;
    private const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly Action<IntPtr, int, WaveFormatInfo, bool> onBuffer;
    private readonly Action<string>? onStatus;
    private readonly object stateLock = new();
    private Thread? worker;
    private volatile bool running;

    public AudioLoopbackCapture(Action<IntPtr, int, WaveFormatInfo, bool> onBuffer, Action<string>? onStatus = null)
    {
        this.onBuffer = onBuffer;
        this.onStatus = onStatus;
    }

    public void Start()
    {
        lock (stateLock)
        {
            if (running)
            {
                return;
            }

            running = true;
            worker = new Thread(RunCapture)
            {
                IsBackground = true,
                Name = "AudioLoopbackCapture"
            };
            worker.Start();
        }
    }

    public void Dispose()
    {
        running = false;
        Thread? thread;
        lock (stateLock)
        {
            thread = worker;
            worker = null;
        }

        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(700);
        }
    }

    private void RunCapture()
    {
        int attempt = 0;
        while (running)
        {
            CaptureEndpoint(attempt++ % 2 == 0 ? ERole.eMultimedia : ERole.eConsole);
            // Release old WASAPI interfaces before selecting the current default device.
            for (int i = 0; i < 10 && running; i++) Thread.Sleep(100);
        }
    }

    private void CaptureEndpoint(ERole preferredRole)
    {
        string operation = "create enumerator";
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        IntPtr mixFormatPtr = IntPtr.Zero;

        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(MMDeviceEnumeratorComObject).GUID)!)!;
            operation = "GetDefaultAudioEndpoint";
            int endpointHr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, preferredRole, out device);
            if (endpointHr < 0 || device is null)
            {
                endpointHr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, preferredRole == ERole.eConsole ? ERole.eMultimedia : ERole.eConsole, out device);
            }

            Marshal.ThrowExceptionForHR(endpointHr);
            if (device == null)
            {
                return;
            }

            Guid audioClientGuid = typeof(IAudioClient).GUID;
            operation = "Activate IAudioClient";
            Marshal.ThrowExceptionForHR(device.Activate(ref audioClientGuid, CLSCTX_ALL, IntPtr.Zero, out var audioClientObject));
            audioClient = (IAudioClient)audioClientObject;

            operation = "GetMixFormat";
            Marshal.ThrowExceptionForHR(audioClient.GetMixFormat(out mixFormatPtr));
            var nativeFormat = Marshal.PtrToStructure<WaveFormatEx>(mixFormatPtr);
            bool isFloat = nativeFormat.wFormatTag == WAVE_FORMAT_IEEE_FLOAT;
            if (nativeFormat.wFormatTag == WAVE_FORMAT_EXTENSIBLE && nativeFormat.cbSize >= 22)
            {
                var extensible = Marshal.PtrToStructure<WaveFormatExtensible>(mixFormatPtr);
                isFloat = extensible.SubFormat == IeeeFloatSubFormat;
            }

            var format = new WaveFormatInfo
            {
                SampleRate = (int)nativeFormat.nSamplesPerSec,
                Channels = nativeFormat.nChannels,
                BitsPerSample = nativeFormat.wBitsPerSample,
                IsFloat = isFloat
            };
            onStatus?.Invoke($"WASAPI_CONNECTED:{format.SampleRate}Hz/{format.Channels}ch/{format.BitsPerSample}bit/{(format.IsFloat ? "float" : "pcm")}");

            operation = "IAudioClient.Initialize";
            Marshal.ThrowExceptionForHR(audioClient.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK,
                10_000_000L,
                0,
                mixFormatPtr,
                IntPtr.Zero));

            Guid captureClientGuid = typeof(IAudioCaptureClient).GUID;
            operation = "GetService IAudioCaptureClient";
            Marshal.ThrowExceptionForHR(audioClient.GetService(ref captureClientGuid, out var captureObject));
            captureClient = (IAudioCaptureClient)captureObject;
            operation = "IAudioClient.Start";
            Marshal.ThrowExceptionForHR(audioClient.Start());
            onStatus?.Invoke("WASAPI_STARTED");

            while (running)
            {
                operation = "IAudioCaptureClient.GetNextPacketSize";
                Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out uint packetFrames));
                if (packetFrames == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                while (packetFrames > 0 && running)
                {
                    IntPtr data = IntPtr.Zero;
                    uint numFrames = 0;
                    bool bufferAcquired = false;
                    try
                    {
                        operation = "IAudioCaptureClient.GetBuffer";
                        Marshal.ThrowExceptionForHR(captureClient.GetBuffer(out data, out numFrames, out uint flags, out _, out _));
                        bufferAcquired = true;
                        bool silent = (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                        if (running)
                        {
                            onBuffer(data, (int)numFrames, format, silent);
                        }
                    }
                    finally
                    {
                        if (bufferAcquired)
                        {
                            operation = "IAudioCaptureClient.ReleaseBuffer";
                            Marshal.ThrowExceptionForHR(captureClient.ReleaseBuffer(numFrames));
                        }
                    }
                    operation = "IAudioCaptureClient.GetNextPacketSize";
                    Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out packetFrames));
                }
            }

            audioClient.Stop();
        }
        catch (Exception ex)
        {
            onStatus?.Invoke($"WASAPI_ERROR:{operation}:{ex.GetType().Name}:{ex.Message}");
        }
        finally
        {
            if (mixFormatPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(mixFormatPtr);
            }

            if (captureClient is not null)
            {
                Marshal.ReleaseComObject(captureClient);
            }

            if (audioClient is not null)
            {
                Marshal.ReleaseComObject(audioClient);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferSize);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint currentPadding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
        [PreserveSig] int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint numFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
    }

    private enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatExtensible
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
        public ushort wValidBitsPerSample;
        public uint dwChannelMask;
        public Guid SubFormat;
    }
}

public readonly struct WaveFormatInfo
{
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int BitsPerSample { get; init; }
    public bool IsFloat { get; init; }
}
