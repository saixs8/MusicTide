using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpectrumKlinePlayer;

public readonly record struct PlayerVolume(float Level, bool Muted);

public static class MusicAudioVolume
{
    public static PlayerVolume? Read(string? sourceApp)
    {
        float sum = 0; int count = 0; bool muted = true;
        Visit(sourceApp, volume =>
        {
            Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out float value));
            Marshal.ThrowExceptionForHR(volume.GetMute(out bool isMuted));
            sum += value; count++; muted &= isMuted;
        });
        return count == 0 ? null : new PlayerVolume(sum / count, muted);
    }

    public static bool Set(string? sourceApp, float? level = null, bool? muted = null)
    {
        bool changed = false;
        Visit(sourceApp, volume =>
        {
            var context = Guid.Empty;
            if (level is { } value) Marshal.ThrowExceptionForHR(volume.SetMasterVolume(Math.Clamp(value, 0, 1), ref context));
            if (muted is { } mute) Marshal.ThrowExceptionForHR(volume.SetMute(mute, ref context));
            changed = true;
        });
        return changed;
    }

    private static void Visit(string? sourceApp, Action<ISimpleAudioVolume> action)
    {
        if (string.IsNullOrWhiteSpace(sourceApp)) return;
        var app = MusicPlayerProfiles.Find(sourceApp);
        if (app is null) return;
        var ids = new HashSet<uint>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { if (MusicPlayerProfiles.Find(process.ProcessName)?.Id == app.Id) ids.Add((uint)process.Id); }
                catch { }
            }
        }
        object? enumeratorObject = null; IDeviceCollection? devices = null;
        try
        {
            // Loopback capture may already own the same COM identity with a different
            // coclass wrapper. Activate as object and query the interface directly.
            enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)
                ?? throw new InvalidOperationException("Cannot activate audio device enumerator");
            var enumerator = (IDeviceEnumerator)enumeratorObject;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out devices));
            Marshal.ThrowExceptionForHR(devices.GetCount(out uint deviceCount));
            for (uint d = 0; d < deviceCount; d++)
            {
                IDevice? device = null; object? managerObject = null; ISessionEnumerator? sessions = null;
                try
                {
                    Marshal.ThrowExceptionForHR(devices.Item(d, out device));
                    Guid iid = typeof(ISessionManager).GUID;
                    Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out managerObject));
                    var manager = (ISessionManager)managerObject;
                    Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessions));
                    Marshal.ThrowExceptionForHR(sessions.GetCount(out int count));
                    for (int i = 0; i < count; i++)
                    {
                        object? session = null;
                        try
                        {
                            Marshal.ThrowExceptionForHR(sessions.GetSession(i, out session));
                            var control = (ISessionControl2)session;
                            Marshal.ThrowExceptionForHR(control.GetProcessId(out uint pid));
                            if (ids.Contains(pid)) action((ISimpleAudioVolume)session);
                        }
                        catch { }
                        finally { Release(session); }
                    }
                }
                catch { }
                finally { Release(sessions); Release(managerObject); Release(device); }
            }
        }
        catch { }
        finally { Release(devices); Release(enumeratorObject); }
    }

    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IDeviceCollection devices);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionManager
    {
        [PreserveSig] int GetAudioSessionControl(ref Guid session, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid session, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out ISessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
        [PreserveSig] int GetProcessId(out uint pid);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float value, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float value);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
