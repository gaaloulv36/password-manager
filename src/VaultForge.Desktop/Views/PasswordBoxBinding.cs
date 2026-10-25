using System.Windows;
using System.Windows.Controls;

namespace VaultForge.Desktop.Views;

/// <summary>
/// Lets a PasswordBox inside a DataTemplate bind its password (PasswordBox.Password isn't a dependency property).
/// The value already lives in the view model as a string, so this doesn't add exposure — it only masks the field.
/// </summary>
public static class PasswordBoxBinding
{
    // Default is null, not "": the change callback below is what starts listening to the box, and binding an
    // empty password (a new entry) must still count as a change — otherwise typing never reaches the view model.
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordPropertyChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached("IsUpdating", typeof(bool), typeof(PasswordBoxBinding));

    public static string GetPassword(DependencyObject element) => (string)element.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject element, string value) => element.SetValue(PasswordProperty, value);

    private static void OnPasswordPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        box.PasswordChanged -= OnPasswordBoxChanged;
        if (!(bool)box.GetValue(IsUpdatingProperty) && box.Password != (string?)e.NewValue)
            box.Password = (string?)e.NewValue ?? "";
        box.PasswordChanged += OnPasswordBoxChanged;
    }

    private static void OnPasswordBoxChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
