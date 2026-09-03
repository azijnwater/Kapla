using System;
using System.Windows;

namespace Kapla
{
    internal static class App
    {
        [STAThread]
        private static void Main()
        {
            var application = new Application();
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            application.Run(new MainWindow());
        }
    }
}
