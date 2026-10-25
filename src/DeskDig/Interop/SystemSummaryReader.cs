using System.Runtime.InteropServices;
using DeskDig.Core;
using Microsoft.Win32;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>
/// SystemSummary の取得。レジストリと kernel32 で取れる項目（デバイス名・Windows・CPU・メモリ）は即時に返し、
/// GPU は DXGI でアダプタを列挙して後から埋める。
/// </summary>
internal static class SystemSummaryReader
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string ProcessorKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";

    /// <summary>即時に取れる項目だけを埋めた要約。GPU は null。</summary>
    public static SystemSummary ReadQuick()
    {
        string device = Environment.MachineName;
        string edition = SystemSummary.Unknown, version = SystemSummary.Unknown, build = SystemSummary.Unknown;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);
            if (key is not null)
            {
                int.TryParse(key.GetValue("CurrentBuild") as string, out int buildNumber);
                edition = SystemSummary.NormalizeEdition(key.GetValue("ProductName") as string, buildNumber);
                version = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string ?? SystemSummary.Unknown;
                var ubr = key.GetValue("UBR");
                build = buildNumber > 0 ? (ubr is int u ? $"{buildNumber}.{u}" : buildNumber.ToString()) : Environment.OSVersion.Version.Build.ToString();
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log("SystemSummary: Windows version read failed", ex);
        }

        string cpu = SystemSummary.Unknown;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ProcessorKey);
            cpu = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? SystemSummary.Unknown;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("SystemSummary: CPU read failed", ex);
        }

        string memory = GetPhysicallyInstalledSystemMemory(out long kilobytes) ? SystemSummary.FormatMemory(kilobytes) : SystemSummary.Unknown;

        return new SystemSummary(device, edition, version, build, cpu, memory, Gpu: null);
    }

    /// <summary>DXGI でハードウェアアダプタを列挙し、「名前 (VRAM)」を「 / 」で連結する。失敗すれば「不明」。</summary>
    public static string ReadGpu()
    {
        try
        {
            var names = DxgiAdapters.Enumerate();
            return names.Count == 0 ? SystemSummary.Unknown : string.Join(" / ", names);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("SystemSummary: GPU read failed", ex);
            return SystemSummary.Unknown;
        }
    }
}

/// <summary>IDXGIFactory1.EnumAdapters1 によるアダプタの列挙。ソフトウェアアダプタ（Microsoft Basic Render Driver）は除く。</summary>
internal static class DxgiAdapters
{
    private const uint DxgiErrorNotFound = 0x887A0002;
    private const uint AdapterFlagSoftware = 2;

    public static IReadOnlyList<string> Enumerate()
    {
        var iid = typeof(IDXGIFactory1).GUID;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out object factoryObj));
        var factory = (IDXGIFactory1)factoryObj;
        var names = new List<string>();
        try
        {
            for (uint i = 0; ; i++)
            {
                int hr = factory.EnumAdapters1(i, out var adapter);
                if ((uint)hr == DxgiErrorNotFound) break;
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Marshal.ThrowExceptionForHR(adapter.GetDesc1(out var desc));
                    if ((desc.Flags & AdapterFlagSoftware) != 0) continue;
                    string name = desc.Description.Trim();
                    if (name.Length == 0) continue;
                    long vramMb = (long)desc.DedicatedVideoMemory / (1024 * 1024);
                    names.Add(vramMb >= 512 ? $"{name} ({FormatVram(vramMb)})" : name);
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
        return names;
    }

    private static string FormatVram(long megabytes)
    {
        double gb = megabytes / 1024.0;
        return gb >= 1 ? $"{Math.Round(gb)} GB" : $"{megabytes} MB";
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    // vtable の順序を保つため、継承元 IDXGIObject / IDXGIFactory / IDXGIAdapter のメソッドも並べる
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIFactory1
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, nint data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, nint unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, nint data);
        [PreserveSig] int GetParent(ref Guid riid, out nint parent);
        // IDXGIFactory
        [PreserveSig] int EnumAdapters(uint index, out nint adapter);
        [PreserveSig] int MakeWindowAssociation(nint hwnd, uint flags);
        [PreserveSig] int GetWindowAssociation(out nint hwnd);
        [PreserveSig] int CreateSwapChain(nint device, nint desc, out nint swapChain);
        [PreserveSig] int CreateSoftwareAdapter(nint module, out nint adapter);
        // IDXGIFactory1
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
        [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIAdapter1
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, nint data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, nint unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, nint data);
        [PreserveSig] int GetParent(ref Guid riid, out nint parent);
        // IDXGIAdapter
        [PreserveSig] int EnumOutputs(uint index, out nint output);
        [PreserveSig] int GetDesc(nint desc);
        [PreserveSig] int CheckInterfaceSupport(ref Guid interfaceName, out long umdVersion);
        // IDXGIAdapter1
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }
}
