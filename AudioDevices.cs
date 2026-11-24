#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CambiaAudio
{
    /// <summary>Un'uscita audio di Windows.</summary>
    public sealed class AudioDevice
    {
        public string Id;
        /// <summary>Nome completo, es. "Casse (Realtek(R) Audio)".</summary>
        public string Name;
        /// <summary>Nome breve, es. "Casse".</summary>
        public string ShortName;
    }

    /// <summary>
    /// Legge le uscite audio con le API Core Audio e imposta quella predefinita
    /// tramite IPolicyConfig, l'interfaccia (non documentata ma stabile da Windows 7)
    /// usata anche dal pannello Suono di Windows.
    /// </summary>
    public static class AudioDevices
    {
        const uint DEVICE_STATE_ACTIVE = 0x1;
        const int STGM_READ = 0;
        const ushort VT_LPWSTR = 31;

        static readonly PropertyKey PKEY_Device_FriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        static readonly PropertyKey PKEY_Device_DeviceDesc = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);

        public static List<AudioDevice> GetActiveOutputs()
        {
            var list = new List<AudioDevice>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                Check(enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out IMMDeviceCollection collection));
                try
                {
                    Check(collection.GetCount(out uint count));
                    for (uint i = 0; i < count; i++)
                    {
                        Check(collection.Item(i, out IMMDevice device));
                        try
                        {
                            Check(device.GetId(out string id));
                            Check(device.OpenPropertyStore(STGM_READ, out IPropertyStore store));
                            try
                            {
                                string full = ReadString(store, PKEY_Device_FriendlyName);
                                string shortName = ReadString(store, PKEY_Device_DeviceDesc);
                                list.Add(new AudioDevice
                                {
                                    Id = id,
                                    Name = full ?? shortName ?? id,
                                    ShortName = shortName ?? full ?? id
                                });
                            }
                            finally { Marshal.ReleaseComObject(store); }
                        }
                        finally { Marshal.ReleaseComObject(device); }
                    }
                }
                finally { Marshal.ReleaseComObject(collection); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
            return list;
        }

        /// <summary>Id dell'uscita predefinita, o null se non ce n'è nessuna.</summary>
        public static string GetDefaultOutputId()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice device);
                if (hr < 0 || device == null) return null;
                try
                {
                    Check(device.GetId(out string id));
                    return id;
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }

        /// <summary>Imposta l'uscita predefinita per tutti i ruoli (giochi, musica, chiamate).</summary>
        public static void SetDefaultOutput(string id)
        {
            var policy = (IPolicyConfig)new PolicyConfigClientCom();
            try
            {
                Check(policy.SetDefaultEndpoint(id, ERole.eConsole));
                Check(policy.SetDefaultEndpoint(id, ERole.eMultimedia));
                Check(policy.SetDefaultEndpoint(id, ERole.eCommunications));
            }
            finally { Marshal.ReleaseComObject(policy); }
        }

        // ---- Microfono ----

        const int CLSCTX_ALL = 23;
        static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        /// <summary>Id dei microfoni predefiniti (uso generale e chiamate), senza doppioni.</summary>
        public static List<string> GetDefaultInputIds()
        {
            var ids = new List<string>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                foreach (var role in new[] { ERole.eConsole, ERole.eCommunications })
                {
                    if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, role, out IMMDevice device) < 0 || device == null) continue;
                    try
                    {
                        Check(device.GetId(out string id));
                        if (!ids.Contains(id)) ids.Add(id);
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
            return ids;
        }

        /// <summary>true se il microfono è in muto, false se è attivo, null se non c'è nessun microfono.</summary>
        public static bool? IsMicMuted()
        {
            var ids = GetDefaultInputIds();
            if (ids.Count == 0) return null;
            bool allMuted = true;
            foreach (string id in ids)
                WithEndpointVolume(id, volume =>
                {
                    Check(volume.GetMute(out bool muted));
                    if (!muted) allMuted = false;
                });
            return allMuted;
        }

        /// <summary>Mette o toglie il muto al microfono per tutti i programmi. Restituisce il nuovo stato (true = muto) o null se non c'è un microfono.</summary>
        public static bool? ToggleMicMute()
        {
            bool? muted = IsMicMuted();
            if (muted == null) return null;
            bool target = !muted.Value;
            foreach (string id in GetDefaultInputIds())
                WithEndpointVolume(id, volume => Check(volume.SetMute(target, IntPtr.Zero)));
            return target;
        }

        static void WithEndpointVolume(string id, Action<IAudioEndpointVolume> action)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                Check(enumerator.GetDevice(id, out IMMDevice device));
                try
                {
                    Guid iid = IID_IAudioEndpointVolume;
                    Check(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object obj));
                    var volume = (IAudioEndpointVolume)obj;
                    try { action(volume); }
                    finally { Marshal.ReleaseComObject(volume); }
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }

        static string ReadString(IPropertyStore store, PropertyKey key)
        {
            PropertyKey k = key;
            if (store.GetValue(ref k, out PropVariant value) < 0) return null;
            try
            {
                return value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.pointerValue) : null;
            }
            finally { PropVariantClear(ref value); }
        }

        static void Check(int hr)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pvar);
    }

    // ---- Definizioni COM ----

    enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
        public PropertyKey(Guid fmtid, int pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    // PROPVARIANT: 24 byte su x64; leggiamo solo il tipo e il puntatore alla stringa.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClientCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant propvar);
        [PreserveSig] int Commit();
    }

    // L'ordine dei metodi deve restare questo: conta la posizione nella vtable.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, IntPtr pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, IntPtr pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, IntPtr pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, IntPtr pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, IntPtr pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig] int VolumeStepUp(IntPtr pguidEventContext);
        [PreserveSig] int VolumeStepDown(IntPtr pguidEventContext);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }

    // L'ordine dei metodi deve restare questo: conta la posizione nella vtable.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bVisible);
    }
}
