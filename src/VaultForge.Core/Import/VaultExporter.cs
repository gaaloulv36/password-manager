using System.Text;
using VaultForge.Core.Models;

namespace VaultForge.Core.Import;

/// <summary>Plain-text CSV export for moving to another password manager. The output is NOT encrypted.</summary>
public static class VaultExporter
{
    public static string ToCsv(IEnumerable<VaultItem> items)
    {
        var builder = new StringBuilder();
        builder.Append(Csv.Format(VaultImporter.OwnCsvHeader)).Append("\r\n");
        foreach (var item in items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            // Extra websites go in the notes, since most importers accept only one URL.
            var notes = item.Urls.Count > 1
                ? string.Join("\n", new[] { item.Notes }.Concat(item.Urls.Skip(1).Select(u => $"Also used on: {u}")).OfType<string>())
                : item.Notes;
            builder.Append(Csv.Format([item.Title, item.Urls.FirstOrDefault(), item.Username, item.Password, notes, item.TotpSecret]))
                .Append("\r\n");
        }
        return builder.ToString();
    }
}
