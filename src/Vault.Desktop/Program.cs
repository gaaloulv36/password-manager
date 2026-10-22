using Vault.Desktop.Browser;
using Vault.Desktop.Services;
using Velopack;

namespace Vault.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: when started by the installer/updater with a hook argument, it handles it and exits.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                // Leave nothing behind that points at the removed files. The vault, settings and backups are kept.
                NativeHostRegistration.Unregister();
                StartupRegistration.SetEnabled(false);
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
