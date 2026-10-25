using System.Runtime.InteropServices;

namespace DeskDig.Interop;

/// <summary>出力デバイス 1 件。Id は Core Audio のエンドポイント ID。</summary>
internal sealed record AudioOutputDevice(string Id, string Name, bool IsDefault);

/// <summary>既定の出力デバイスの音量（0..1）とミュート。</summary>
internal sealed record AudioVolumeState(float Level, bool IsMuted);

/// <summary>
/// Core Audio による出力デバイスの列挙・既定の切替・音量・ミュート・デバイス変化の通知。
/// 列挙・音量・ミュート・通知は公開 API（IMMDeviceEnumerator / IAudioEndpointVolume）のみで動く。
/// 既定の切替だけは非公開の IPolicyConfig に依存する（ADR-0004）。失敗時は例外を投げ、呼び出し側が退避を提供する。
/// </summary>
internal sealed class AudioDeviceService : IDisposable
{
    private const int ElementNotFound = unchecked((int)0x80070490);
    private const uint ClsctxAll = 23;
    private static readonly PROPERTYKEY FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    private readonly IMMDeviceEnumerator _enumerator;
    private readonly EndpointNotificationClient _notifications;
    private bool _disposed;

    /// <summary>デバイスの追加・削除・状態変化・既定の変更。呼び出しスレッドは COM のワーカースレッドなので、UI へは Dispatcher で渡すこと。</summary>
    public event Action? DevicesChanged;

    public AudioDeviceService()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
        _notifications = new EndpointNotificationClient(() => DevicesChanged?.Invoke());
        Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(_notifications));
    }

    /// <summary>有効な出力デバイス。既定を先頭に、残りは名前順。</summary>
    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? defaultId = GetDefaultDeviceId();
        Marshal.ThrowExceptionForHR(_enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out var collection));
        try
        {
            Marshal.ThrowExceptionForHR(collection.GetCount(out uint count));
            var devices = new List<AudioOutputDevice>((int)count);
            for (uint i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(collection.Item(i, out var device));
                try
                {
                    Marshal.ThrowExceptionForHR(device.GetId(out string id));
                    string name = FriendlyNameOf(device) ?? "名前のないデバイス";
                    devices.Add(new AudioOutputDevice(id, name, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                }
                finally
                {
                    Release(device);
                }
            }
            return devices.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally
        {
            Release(collection);
        }
    }

    /// <summary>既定の出力デバイスを切り替える（Console / Multimedia / Communications のすべて）。非公開 API に依存する。</summary>
    public void SetDefaultDevice(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var policy = (IPolicyConfig)new PolicyConfigClass();
        try
        {
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.Multimedia));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.Communications));
        }
        finally
        {
            Release(policy);
        }
    }

    /// <summary>既定の出力デバイスの音量とミュート。既定が無ければ null。</summary>
    public AudioVolumeState? GetDefaultVolume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var device = GetDefaultDevice();
        if (device is null) return null;
        try
        {
            var volume = ActivateVolume(device);
            try
            {
                Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out float level));
                Marshal.ThrowExceptionForHR(volume.GetMute(out int muted));
                return new AudioVolumeState(level, muted != 0);
            }
            finally
            {
                Release(volume);
            }
        }
        finally
        {
            Release(device);
        }
    }

    public void SetDefaultVolume(float level) =>
        WithDefaultVolume(v => v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), 0));

    public void SetDefaultMuted(bool muted) =>
        WithDefaultVolume(v => v.SetMute(muted, 0));

    private void WithDefaultVolume(Func<IAudioEndpointVolume, int> operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var device = GetDefaultDevice() ?? throw new InvalidOperationException("既定の出力デバイスがありません。");
        try
        {
            var volume = ActivateVolume(device);
            try
            {
                Marshal.ThrowExceptionForHR(operation(volume));
            }
            finally
            {
                Release(volume);
            }
        }
        finally
        {
            Release(device);
        }
    }

    private static IAudioEndpointVolume ActivateVolume(IMMDevice device)
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsctxAll, 0, out object instance));
        return (IAudioEndpointVolume)instance;
    }

    private string? GetDefaultDeviceId()
    {
        var device = GetDefaultDevice();
        if (device is null) return null;
        try
        {
            Marshal.ThrowExceptionForHR(device.GetId(out string id));
            return id;
        }
        finally
        {
            Release(device);
        }
    }

    private IMMDevice? GetDefaultDevice()
    {
        int hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out var device);
        if (hr == ElementNotFound) return null;
        Marshal.ThrowExceptionForHR(hr);
        return device;
    }

    private static string? FriendlyNameOf(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0 /* STGM_READ */, out var store));
        try
        {
            var key = FriendlyNameKey;
            store.GetValue(ref key, out PROPVARIANT value);
            try
            {
                return value.AsString();
            }
            finally
            {
                NativeMethods.PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Release(store);
        }
    }

    private static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notifications);
        }
        catch
        {
            // 終了時の後始末なので失敗しても続ける
        }
        Release(_enumerator);
    }

    private sealed class EndpointNotificationClient(Action changed) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => changed();
        public void OnDeviceAdded(string deviceId) => changed();
        public void OnDeviceRemoved(string deviceId) => changed();
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) => changed();
        public void OnPropertyValueChanged(string deviceId, PROPERTYKEY key) { /* 名前変更などは次回の列挙で拾う */ }
    }
}

// ---- Core Audio の COM 定義（公開 API）----

internal enum EDataFlow { Render, Capture, All }

internal enum ERole { Console, Multimedia, Communications }

[Flags]
internal enum DeviceState : uint
{
    Active = 0x1,
    Disabled = 0x2,
    NotPresent = 0x4,
    Unplugged = 0x8,
    All = 0xF,
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorClass { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out DeviceState state);
}

[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, DeviceState newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PROPERTYKEY key);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(nint notify);
    [PreserveSig] int UnregisterControlChangeNotify(nint notify);
    [PreserveSig] int GetChannelCount(out uint channelCount);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, nint eventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, nint eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, nint eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, nint eventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, nint eventContext);
    [PreserveSig] int GetMute(out int muted);
    [PreserveSig] int GetVolumeStepInfo(out uint currentStep, out uint stepCount);
    [PreserveSig] int VolumeStepUp(nint eventContext);
    [PreserveSig] int VolumeStepDown(nint eventContext);
    [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
    [PreserveSig] int GetVolumeRange(out float minimumDb, out float maximumDb, out float incrementDb);
}

// ---- 非公開（ADR-0004）。使うのは SetDefaultEndpoint だけだが、vtable の順序を保つため全メソッドを宣言する ----

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClass { }

[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint format);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultFormat, nint format);
    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint endpointFormat, nint mixFormat);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod, nint defaultPeriodPtr, nint minimumPeriodPtr);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint period);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PROPERTYKEY key, out PROPVARIANT value);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PROPERTYKEY key, ref PROPVARIANT value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
}
