using System;
using System.Threading;
using System.Windows;

namespace Wolpope
{
    /// <summary>
    /// Точка входа приложения.
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string mutexName = "Global\\WolpopeWolpope_SingleInstance";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "Wolpope уже запущен!",
                    "Wolpope",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var mainWindow = new MainWindow();
            
            bool isAutorun = false;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--autorun", StringComparison.OrdinalIgnoreCase))
                {
                    isAutorun = true;
                    break;
                }
            }

            if (!isAutorun)
            {
                mainWindow.Show();
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
