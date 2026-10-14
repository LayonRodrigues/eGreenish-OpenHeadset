using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using OpenHeadset.Profiles;
using OpenHeadset.Transport;

namespace OpenHeadset.App;

public partial class App : Application
{
    private const string MutexName = "OpenHeadset.Desktop.SingleInstance";
    private const string ShowSignalName = "OpenHeadset.Desktop.ShowWindow";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private HeadsetSession? _session;
    private TaskbarIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Falhas fora da thread de interface não passam pelo DispatcherUnhandledException; sem isto o app
        // poderia sumir sem deixar nada escrito.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogError(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()), fatal: args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            args.SetObserved();
        };

        _singleInstance = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // Já há uma cópia rodando (provavelmente escondida na bandeja): pede que ela mostre a janela.
            SignalRunningInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        _session = new HeadsetSession(BuiltInProfiles.All);
        _session.Faulted += LogError;
        _window = new MainWindow(new MainViewModel(_session, Dispatcher));
        _tray = CreateTray();
        ListenForShowSignal();

        _window.PlaceNearTray(); // antes de mostrar, para abrir já no canto e sem pular
        _window.Show();
        _session.Start();
    }

    /// <summary>Avisa a cópia que já está aberta. Tenta algumas vezes porque a primeira cópia pode ainda estar subindo.</summary>
    private static void SignalRunningInstance()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                signal.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(200);
            }
        }
    }

    private void ListenForShowSignal()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        var thread = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.InvokeAsync(ShowWindow);
        })
        {
            IsBackground = true,
            Name = "OpenHeadset.ShowSignal",
        };
        thread.Start();
    }

    private TaskbarIcon CreateTray()
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Abrir" };
        open.Click += (_, _) => ShowWindow();
        var exit = new MenuItem { Header = "Sair" };
        exit.Click += (_, _) => Quit();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        // O .ico tem vários tamanhos; o Windows escolhe o que combina com o DPI da bandeja.
        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/OpenHeadset.ico"))!.Stream;
        var tray = new TaskbarIcon
        {
            ToolTipText = "eGreenish OpenHeadset",
            ContextMenu = menu,
            Icon = new System.Drawing.Icon(iconStream),
        };
        tray.TrayLeftMouseUp += (_, _) => ToggleWindow();
        tray.ForceCreate();
        return tray;
    }

    private void ToggleWindow()
    {
        if (_window is { IsVisible: true, WindowState: not WindowState.Minimized })
            _window.Hide();
        else
            ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window is null)
            return;

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.PlaceNearTray();
        _window.Show();
        _window.Activate();
    }

    private void Quit()
    {
        _tray?.Dispose();
        _session?.Dispose();
        _singleInstance?.ReleaseMutex();
        Shutdown();
    }

    private static void LogError(Exception e) => LogError(e, fatal: false);

    private static void LogError(Exception e, bool fatal)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenHeadset");
            Directory.CreateDirectory(dir);
            var kind = fatal ? "FATAL (o app vai fechar)" : "erro";
            File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTime.Now:O} [{kind}]{Environment.NewLine}{e}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Sem onde registrar: ignora em vez de derrubar o app.
        }
    }
}
