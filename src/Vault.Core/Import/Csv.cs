using System.Text;

namespace Vault.Core.Import;

/// <summary>RFC 4180 CSV: quoted fields may contain commas, quotes ("") and line breaks.</summary>
public static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = text.StartsWith('﻿') ? 1 : 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') field.Append(text[++i]);
                else inQuotes = false;
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }
        EndRow();
        return rows;

        void EndRow()
        {
            row.Add(field.ToString());
            field.Clear();
            if (row.Count > 1 || row[0].Length > 0) rows.Add([.. row]);
            row.Clear();
        }
    }

    public static string Format(IEnumerable<string?> fields) => string.Join(',', fields.Select(Escape));

    private static string Escape(string? value)
    {
        value ??= "";
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 || value != value.Trim()
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }
}
