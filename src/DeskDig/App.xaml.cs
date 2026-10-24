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
            Diagnostics.Log($"ListPinned: {apps.Count} apps");
            for (int i = 0; i < apps.Count; i++)
            {
                var a = apps[i];
                Diagnostics.Log($"  {i + 1,2}. {a.Name} | id={a.Id} | exe={a.TargetExecutablePath ?? "-"} | aumid={a.AppUserModelId ?? "-"}");
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

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _messageWindow?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
