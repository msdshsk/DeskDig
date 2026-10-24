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
