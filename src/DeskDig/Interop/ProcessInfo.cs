using System.Diagnostics;
using System.Text;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>プロセスから実行ファイルパス・AppUserModelId・アプリ名を得る。結果はキャッシュする。</summary>
internal sealed class ProcessInfo
{
    private readonly Dictionary<uint, (string? Path, string? Aumid)> _byPid = new();
    private readonly Dictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase);

    public (string? ExecutablePath, string? AppUserModelId) Describe(uint pid)
    {
        if (_byPid.TryGetValue(pid, out var cached)) return cached;
        string? path = null, aumid = null;
        nint handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != 0)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (QueryFullProcessImageName(handle, 0, sb, ref size)) path = sb.ToString(0, (int)size);

                uint length = 0;
                var buffer = new StringBuilder(1);
                int rc = GetApplicationUserModelId(handle, ref length, buffer);
                if (rc == ERROR_INSUFFICIENT_BUFFER && length > 0)
                {
                    buffer = new StringBuilder((int)length);
                    if (GetApplicationUserModelId(handle, ref length, buffer) == 0) aumid = buffer.ToString();
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        var result = (path, aumid);
        _byPid[pid] = result;
        return result;
    }

    public string AppNameOf(string executablePath)
    {
        if (_appNames.TryGetValue(executablePath, out var name)) return name;
        name = Path.GetFileNameWithoutExtension(executablePath);
        try
        {
            var description = FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) name = description.Trim();
        }
        catch
        {
            // アクセスできない（ストアアプリ等）場合はファイル名で代替
        }
        _appNames[executablePath] = name;
        return name;
    }
}
