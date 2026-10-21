namespace Vault.Desktop.Views;

/// <summary>A page with one obvious field to type into, focused when the window is summoned.</summary>
public interface IHasPrimaryInput
{
    void FocusPrimaryInput();
}
