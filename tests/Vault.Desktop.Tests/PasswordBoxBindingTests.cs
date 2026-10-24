using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using Vault.Desktop.Views;

namespace Vault.Desktop.Tests;

/// <summary>
/// Regression: in v1.0.0–1.0.2 a password typed into a *new* entry never reached the view model
/// (the box started empty, so the binding helper never attached), and the entry was saved without it.
/// </summary>
public class PasswordBoxBindingTests
{
    private sealed class Model : INotifyPropertyChanged
    {
        private string _password = "";

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Password)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>WPF controls need an STA thread.</summary>
    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private static PasswordBox Bind(Model model)
    {
        var box = new PasswordBox { DataContext = model };
        box.SetBinding(PasswordBoxBinding.PasswordProperty, new Binding(nameof(Model.Password)));
        return box;
    }

    [Theory]
    [InlineData("")]                  // new entry
    [InlineData("existing-password")] // editing an entry
    public void Typing_ReachesTheViewModel(string initial) => OnSta(() =>
    {
        var model = new Model { Password = initial };
        var box = Bind(model);

        box.Password = "typed-by-user";

        Assert.Equal("typed-by-user", model.Password);
    });

    [Fact]
    public void ViewModelChanges_ShowInTheBox() => OnSta(() =>
    {
        var model = new Model();
        var box = Bind(model);

        model.Password = "from-generator";

        Assert.Equal("from-generator", box.Password);
    });

    [Fact]
    public void ExistingPassword_IsShownMasked() => OnSta(() =>
    {
        var box = Bind(new Model { Password = "existing" });
        Assert.Equal("existing", box.Password);
    });
}
