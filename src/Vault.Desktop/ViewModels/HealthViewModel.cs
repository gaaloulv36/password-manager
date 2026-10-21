using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core.Generators;
using Vault.Core.Security;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

/// <summary>One login in the report, with its problems spelled out.</summary>
public sealed record HealthRow(ItemHealth Health)
{
    public Guid ItemId => Health.Item.Id;
    public string Title => Health.Item.Title;
    public string? Username => Health.Item.Username;
    public string? Website => Health.Item.Urls.FirstOrDefault();

    public IReadOnlyList<string> Issues
    {
        get
        {
            var issues = new List<string>();
            if (Health.IsBreached)
                issues.Add(string.Create(CultureInfo.CurrentCulture, $"Found in data breaches ({Health.BreachCount:N0} times) — change it now"));
            if (Health.IsReused)
                issues.Add(Health.ReusedCount == 1 ? "Also used for 1 other login" : $"Also used for {Health.ReusedCount} other logins");
            if (Health.IsWeak)
                issues.Add(Health.Strength == StrengthLevel.VeryWeak ? "Very weak — easy to guess" : "Weak");
            if (Health.IsOld)
                issues.Add("Not changed for over a year");
            return issues;
        }
    }
}

public sealed partial class HealthViewModel : ObservableObject
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly VaultSession _session;
    private readonly Dictionary<Guid, int> _breachCounts = [];
    private bool _breachCheckDone;
    private CancellationTokenSource? _cancel;

    public HealthViewModel(VaultSession session)
    {
        _session = session;
        Refresh();
    }

    /// <summary>Raised when the user wants to fix a login: the window closes and the vault opens it for editing.</summary>
    public event EventHandler<Guid>? EditRequested;

    public ObservableCollection<HealthRow> Rows { get; } = [];

    [ObservableProperty] private HealthReport _report = new([]);
    [ObservableProperty] private bool _showAll;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string? _breachStatus;

    public string ScoreText => $"{Report.Score}";

    public string Summary => Report.Total == 0
        ? "No passwords to check yet."
        : Report.Items.Any(i => i.HasIssues)
            ? $"{Report.Items.Count(i => i.HasIssues)} of {Report.Total} logins need attention."
            : $"All {Report.Total} logins look good.";

    partial void OnShowAllChanged(bool value) => Refresh();

    [RelayCommand(CanExecute = nameof(CanCheckBreaches))]
    private async Task CheckBreachesAsync()
    {
        var items = _session.Vault!.GetItems().Where(i => !string.IsNullOrEmpty(i.Password)).ToList();
        var client = new PwnedPasswordsClient(Http);
        _cancel = new CancellationTokenSource();
        IsChecking = true;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                BreachStatus = $"Checking {i + 1} of {items.Count}…";
                _breachCounts[items[i].Id] = await client.GetBreachCountAsync(items[i].Password!, _cancel.Token);
            }
            _breachCheckDone = true;
            var breached = _breachCounts.Count(c => c.Value > 0);
            BreachStatus = breached == 0
                ? "None of your passwords appear in known data breaches."
                : $"{breached} password{(breached == 1 ? "" : "s")} found in data breaches. Change {(breached == 1 ? "it" : "them")} first.";
        }
        catch (HttpRequestException)
        {
            BreachStatus = "Couldn't reach the breach database. Check your internet connection and try again.";
        }
        catch (OperationCanceledException)
        {
            BreachStatus = null;
        }
        finally
        {
            IsChecking = false;
            Refresh();
        }
    }

    private bool CanCheckBreaches() => !IsChecking && _session.IsUnlocked;

    partial void OnIsCheckingChanged(bool value) => CheckBreachesCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Edit(HealthRow row) => EditRequested?.Invoke(this, row.ItemId);

    [RelayCommand]
    private static void OpenWebsite(HealthRow row)
    {
        if (Uri.TryCreate(row.Website, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public void Cancel() => _cancel?.Cancel();

    private void Refresh()
    {
        if (!_session.IsUnlocked) return;
        Report = PasswordHealth.Analyze(_session.Vault!.GetItems(), DateTimeOffset.UtcNow, _breachCheckDone ? _breachCounts : null);
        Rows.Clear();
        foreach (var item in Report.Items.Where(i => ShowAll || i.HasIssues)) Rows.Add(new HealthRow(item));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(Summary));
    }
}
