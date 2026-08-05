using System.Text;

namespace MyBudget.Infrastructure;

internal static class CsvCodec
{
    public static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    public static string NeutralizeSpreadsheetFormula(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        // Doubling an existing apostrophe makes this escape reversible. Without
        // that rule, a legitimate value such as "'=literal" would lose data on import.
        return value[0] == '\'' || value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'
            ? $"'{value}"
            : value;
    }

    public static string RestoreNeutralizedSpreadsheetFormula(string value)
    {
        if (value.StartsWith("''", StringComparison.Ordinal))
        {
            return value[1..];
        }

        return value.Length >= 2 &&
               value[0] == '\'' &&
               value[1] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'
                ? value[1..]
                : value;
    }

    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text, int maximumRows)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var insideQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (insideQuotes)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        insideQuotes = false;
                    }
                }
                else
                {
                    field.Append(current);
                }

                continue;
            }

            switch (current)
            {
                case '"' when field.Length == 0:
                    insideQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    CompleteRow(rows, row, field, maximumRows);
                    break;
                case '\n':
                    CompleteRow(rows, row, field, maximumRows);
                    break;
                default:
                    field.Append(current);
                    break;
            }
        }

        if (insideQuotes)
        {
            throw new InvalidDataException("The CSV file contains an unterminated quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            CompleteRow(rows, row, field, maximumRows);
        }

        return rows;
    }

    private static void CompleteRow(
        ICollection<IReadOnlyList<string>> rows,
        List<string> row,
        StringBuilder field,
        int maximumRows)
    {
        row.Add(field.ToString());
        field.Clear();
        rows.Add(row.ToArray());
        row.Clear();

        if (rows.Count > maximumRows)
        {
            throw new InvalidDataException($"The CSV file contains more than {maximumRows:N0} rows.");
        }
    }
}
