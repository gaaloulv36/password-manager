using System.Windows;
using Vault.Desktop.Views;

namespace Vault.Desktop.Tests;

/// <summary>
/// Builds every window and page from XAML with the app's real resources. A broken XAML file (unknown
/// resource, bad binding syntax, missing type) would otherwise only show up when a user opens that screen —
/// or, for MainWindow, as an app that won't start after an automatic update.
/// </summary>
public class XamlSmokeTests
{
    [Fact]
    public void AllWindowsAndPages_LoadFromXaml()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent(); // App.xaml: theme, converters, styles, page templates

                List<Func<object>> views =
                [
                    () => new MainWindow(),
                    () => new WelcomeView(),
                    () => new UnlockView(),
                    () => new CreateVaultView(),
                    () => new VaultView(),
                    () => new SettingsWindow(),
                    () => new GeneratorWindow(),
                    () => new ImportWindow(),
                    () => new HealthWindow(),
                    () => new PairingWindow("Test browser", "123 456"),
                ];
                foreach (var create in views)
                {
                    var view = create();
                    Assert.NotNull(view);
                    if (view is Window window) window.Close();
                }
                app.Shutdown();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw new Xunit.Sdk.XunitException($"XAML failed to load: {failure}");
    }
}
