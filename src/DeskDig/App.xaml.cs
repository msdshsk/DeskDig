using System.Windows;
using DeskDig.Interop;
using DeskDig.Persistence;

namespace DeskDig;

public partial class App : Application
{
    private const string MutexName = @"Local\DeskDig.SingleInstance";
    private const string ShowMessageName = "DeskDig.ShowPalette";

    private Mutex? _mutex;
    private PaletteController? _controller;
    private MessageWindow? _messageWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SanitizeInheritedEnvironment();
        uint showMessage = NativeMethods.RegisterWindowMessage(ShowMessageName);

        // 検証用: デスクトップ余白判定だけを行って終了する（--hittest X Y）
        if (e.Args.Length == 3 && e.Args[0] == "--hittest" && int.TryParse(e.Args[1], out int hx) && int.TryParse(e.Args[2], out int hy))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool blank = DesktopHitTest.IsDesktopBlank(new Core.ScreenPoint(hx, hy), out string detail);
            Diagnostics.Log($"HitTest ({hx},{hy}) => {(blank ? "desktop blank" : "not blank")} [{detail}] {sw.ElapsedMilliseconds} ms");
            Shutdown();
            return;
        }

        // 診断用: タスクバーの固定一覧を読んでログに書き、終了する（--list-pinned）
        if (e.Args.Length == 1 && e.Args[0] == "--list-pinned")
        {
            var apps = PinnedAppReader.ReadAll();
            var icons = new IconProvider();
            Diagnostics.Log($"ListPinned: {apps.Count} apps");
            for (int i = 0; i < apps.Count; i++)
            {
                var a = apps[i];
                string logo = a.AppUserModelId is null ? "-" :
                    $"dark={PackagedAppLogo.ResolveFile(a.AppUserModelId, 32, Core.LogoBackdrop.Dark) ?? "(shell)"} light={PackagedAppLogo.ResolveFile(a.AppUserModelId, 32, Core.LogoBackdrop.Light) ?? "(shell)"}";
                Diagnostics.Log($"  {i + 1,2}. {a.Name} | id={a.Id} | exe={a.TargetExecutablePath ?? "-"} | aumid={a.AppUserModelId ?? "-"} | icon: {DescribeTransparency(icons.ForPinnedApp(a, 32))} | logo: {logo}");
            }
            Shutdown();
            return;
        }

        // 診断用: 指定した名前の PinnedApp を Launcher と同じ経路で起動して終了する（--launch <名前>）
        if (e.Args.Length == 2 && e.Args[0] == "--launch")
        {
            var app = PinnedAppReader.ReadAll().FirstOrDefault(a => string.Equals(a.Name, e.Args[1], StringComparison.OrdinalIgnoreCase));
            if (app is null) Diagnostics.Log($"Launch: '{e.Args[1]}' not found");
            else
            {
                try
                {
                    PinnedAppReader.Launch(app);
                    Diagnostics.Log($"Launch: started {app.Name} via {PinnedAppReader.ParsingNameOf(app)}");
                }
                catch (Exception ex)
                {
                    Diagnostics.Log($"Launch: {app.Name} failed", ex);
                }
            }
            Shutdown();
            return;
        }

        // インストーラー用: 自動起動の登録だけを行って終了する（--autostart on|off）
        if (e.Args.Length == 2 && e.Args[0] == "--autostart" && e.Args[1] is "on" or "off")
        {
            try
            {
                AutoStartRegistration.Apply(e.Args[1] == "on");
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Auto-start registration via command line failed", ex);
            }
            Shutdown();
            return;
        }

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 2 つ目は既存を前面に出して終了する
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, showMessage, 0, 0);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Diagnostics.Log("Unhandled", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Diagnostics.Log($"Fatal: {args.ExceptionObject}");
        Diagnostics.Log("Startup");

        _messageWindow = new MessageWindow();
        _controller = new PaletteController(Dispatcher, new StateStore(), _messageWindow, showMessage);
        _controller.Start();
    }

    /// <summary>診断用: 画像の透明画素の割合。透過が落ちて黒塗りになっていないかを確認する。</summary>
    private static string DescribeTransparency(System.Windows.Media.ImageSource? image)
    {
        if (image is not System.Windows.Media.Imaging.BitmapSource source) return "(none)";
        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);
        int transparent = 0, opaqueBlack = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 255) transparent++;
            else if (pixels[i] < 16 && pixels[i + 1] < 16 && pixels[i + 2] < 16) opaqueBlack++;
        }
        return $"{w}x{h} transparent={transparent} opaqueBlack={opaqueBlack}";
    }

    /// <summary>
    /// インストーラーなどから起動された場合に引き継ぐ環境変数のうち、Launcher から起動する子プロセスに
    /// 悪影響を与えるものを自分の環境から外す。__COMPAT_LAYER は互換性シムを子プロセスにも適用させ、
    /// 一部のアプリの起動を失敗させる（0xc00004bc 等）。
    /// </summary>
    private static void SanitizeInheritedEnvironment()
    {
        foreach (var name in new[] { "__COMPAT_LAYER", "__PSLockdownPolicy" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                Diagnostics.Log($"Removed inherited environment variable {name}={value}");
                Environment.SetEnvironmentVariable(name, null);
            }
        }
        // インストーラー経由では USERNAME が実際のユーザーと食い違うことがあるので、実際の値に戻す
        if (!string.Equals(Environment.GetEnvironmentVariable("USERNAME"), Environment.UserName, StringComparison.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("USERNAME", Environment.UserName);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _messageWindow?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
