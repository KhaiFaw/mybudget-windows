using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MyBudget.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace MyBudget.Infrastructure;

// Coordinates are normalized to page width/height. No document text is logged or cached.
public sealed record StatementWord(string Text, double Left, double Top);
public sealed record StatementPage(IReadOnlyList<StatementWord> Words);

public static class MaybankStatementReader
{
    public static Task<BankStatement> ReadAsync(string path, string? password = null,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > 20 * 1024 * 1024)
            throw new InvalidDataException("Choose a PDF smaller than 20 MB.");
        using var document = PdfDocument.Open(path, new ParsingOptions { Password = password ?? string.Empty });
        if (document.NumberOfPages is < 1 or > 100)
            throw new InvalidDataException("Statements must contain between 1 and 100 pages.");
        var pages = new List<StatementPage>();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).Take(15001).Select(word => new StatementWord(
                word.Text, word.BoundingBox.Left / page.Width,
                (page.Height - word.BoundingBox.Top) / page.Height)).ToArray();
            if (words.Length > 15000)
                throw new InvalidDataException("This PDF page is too complex to import safely.");
            pages.Add(new StatementPage(words));
        }
        return Parse(pages, cancellationToken);
    }, cancellationToken);

    /// <summary>
    /// Supports the Maybank savings/current-account layout with signed amounts and
    /// statement balances. Unknown layouts fail closed; never infer cash direction.
    /// </summary>
    public static BankStatement Parse(IReadOnlyList<StatementPage> pages,
        CancellationToken cancellationToken = default)
    {
        if (pages.Count is < 1 or > 100) throw Unsupported();
        string? accountKey = null;
        DateOnly? statementDate = null;
        decimal? opening = null, ending = null, debitTotal = null, creditTotal = null;
        decimal? lastBalance = null;
        var pending = new List<PendingEntry>();
        var ended = false;

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = Lines(page.Words);
            var header = string.Join(" ", lines.Where(line => line.Top < .27).Select(line => line.Text));
            if (!header.Contains("Maybank", StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(header, @"(?:SAVINGS|CURRENT) ACCOUNT", RegexOptions.IgnoreCase))
                throw Unsupported();
            var dateWord = page.Words.FirstOrDefault(word => word.Left > .72 && word.Top < .23 && DatePattern.IsMatch(word.Text));
            var accountWord = page.Words.FirstOrDefault(word => word.Left > .70 && word.Top < .23 && AccountPattern.IsMatch(word.Text));
            if (dateWord is null || accountWord is null || !TryDate(dateWord.Text, out var date)) throw Unsupported();
            var key = accountWord.Text.Replace("-", string.Empty, StringComparison.Ordinal);
            accountKey ??= key;
            statementDate ??= date;
            if (accountKey != key || statementDate != date)
                throw new InvalidDataException("The PDF contains different accounts or statement periods. Import one complete statement at a time.");

            var tableHeading = lines.FirstOrDefault(line =>
                line.Text.Contains("ENTRY DATE", StringComparison.Ordinal) &&
                line.Text.Contains("TRANSACTION DESCRIPTION", StringComparison.Ordinal) &&
                line.Text.Contains("TRANSACTION AMOUNT", StringComparison.Ordinal) &&
                line.Text.Contains("STATEMENT BALANCE", StringComparison.Ordinal));
            if (tableHeading is null) throw Unsupported();
            var tableEnd = lines.FirstOrDefault(line => line.Top > tableHeading.Top &&
                (line.Text.Replace(" ", "").Contains("Perhatian", StringComparison.OrdinalIgnoreCase) || line.Text.StartsWith("Note", StringComparison.OrdinalIgnoreCase)))?.Top ?? 1.0;
            // This supported statement template ends its ledger above the fixed
            // notes area. Some subset fonts extract the footer labels as fragments.
            // A different layout must still pass all opening/closing/total checks.
            tableEnd = Math.Min(tableEnd, .879);

            foreach (var line in lines.Where(line => line.Top > tableHeading.Top + .005 && line.Top < tableEnd))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dateText = Join(line.Words.Where(word => word.Left < .163));
                var description = Join(line.Words.Where(word => word.Left >= .163 && word.Left < .605));
                var amountText = Join(line.Words.Where(word => word.Left >= .605 && word.Left < .785)).Replace(" ", "");
                var balanceText = Join(line.Words.Where(word => word.Left >= .785)).Replace(" ", "");

                if (description.StartsWith("BEGINNING BALANCE", StringComparison.OrdinalIgnoreCase))
                {
                    if (opening is not null || pending.Count != 0 || !TryMoney(balanceText, out var value)) throw Unbalanced();
                    opening = lastBalance = value;
                    continue;
                }
                if (description.StartsWith("ENDING BALANCE", StringComparison.OrdinalIgnoreCase))
                {
                    if (ending is not null || !TryMoney(amountText.Length > 0 ? amountText : balanceText, out var value)) throw Unbalanced();
                    ending = value;
                    ended = true;
                    continue;
                }
                if (description.StartsWith("TOTAL DEBIT", StringComparison.OrdinalIgnoreCase) ||
                    description.StartsWith("TOTAL CREDIT", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ended || !TryMoney(amountText.Length > 0 ? amountText : balanceText, out var value)) throw Unbalanced();
                    if (description.StartsWith("TOTAL DEBIT", StringComparison.OrdinalIgnoreCase))
                    {
                        if (debitTotal is not null) throw Unbalanced();
                        debitTotal = value;
                    }
                    else
                    {
                        if (creditTotal is not null) throw Unbalanced();
                        creditTotal = value;
                    }
                    continue;
                }
                if (dateText.Length > 0)
                {
                    if (ended || lastBalance is null || !TryDate(dateText, out var entryDate) ||
                        BudgetMonth.FromDate(entryDate) != BudgetMonth.FromDate(date) ||
                        description.Length == 0 || !TrySignedMoney(amountText, out var signed) ||
                        !TryMoney(balanceText, out var balance)) throw Unsupported();
                    if (lastBalance + signed != balance) throw Unbalanced();
                    if (pending.Count > 0 && entryDate < pending[^1].Date) throw Unsupported();
                    lastBalance = balance;
                    pending.Add(new PendingEntry(entryDate, signed, balance, description));
                    if (pending.Count > 5000) throw new InvalidDataException("Statements with more than 5,000 transactions are not supported.");
                }
                else if (amountText.Length > 0 || balanceText.Length > 0)
                    throw Unsupported(); // Never silently drop an unrecognized money row.
                else if (!ended && description.Length > 0 && pending.Count > 0)
                    pending[^1].Continuation.Add(description);
            }
        }
        if (statementDate is null || accountKey is null || opening is null || ending is null ||
            debitTotal is null || creditTotal is null || lastBalance != ending ||
            pending.Where(row => row.Signed < 0).Sum(row => -row.Signed) != debitTotal ||
            pending.Where(row => row.Signed > 0).Sum(row => row.Signed) != creditTotal ||
            opening + creditTotal - debitTotal != ending) throw Unbalanced();

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var entries = pending.Select(row =>
        {
            // Account, date, signed amount and running balance distinguish overlapping
            // statements. Description/category edits in MyBudget do not change this ID.
            var fingerprint = FormattableString.Invariant($"maybank-v1|{accountKey}|{row.Date:yyyy-MM-dd}|{row.Signed:F2}|{row.Balance:F2}");
            var occurrence = occurrences.GetValueOrDefault(fingerprint);
            occurrences[fingerprint] = occurrence + 1;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{fingerprint}|{occurrence}"));
            var detail = row.Continuation.FirstOrDefault(text => text.Count(char.IsLetter) >= 3);
            var note = row.Description + (detail is null ? "" : $" · {detail.TrimEnd(' ', '*')}");
            return new StatementEntry(new Guid(hash.AsSpan(0, 16)), row.Date, row.Signed, row.Balance, note);
        }).ToArray();
        return new BankStatement(BudgetMonth.FromDate(statementDate.Value), entries,
            opening.Value, ending.Value, debitTotal.Value, creditTotal.Value);
    }

    private static readonly Regex DatePattern = new(@"^\d{2}/\d{2}/(?:\d{2}|\d{4})$", RegexOptions.CultureInvariant);
    private static readonly Regex AccountPattern = new(@"^\d{6}-?\d{6}$", RegexOptions.CultureInvariant);
    private static readonly Regex MoneyPattern = new(@"^-?(?:\d{1,3}(?:,\d{3})*|\d+)\.\d{2}$", RegexOptions.CultureInvariant);
    private static bool TryMoney(string text, out decimal value)
    {
        value = 0;
        return MoneyPattern.IsMatch(text) && decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }
    private static bool TrySignedMoney(string text, out decimal value)
    {
        value = 0;
        if (text.Length < 2 || text[^1] is not ('+' or '-') || !TryMoney(text[..^1], out var amount) || amount <= 0) return false;
        value = text[^1] == '-' ? -amount : amount;
        return true;
    }
    private static bool TryDate(string text, out DateOnly date)
    {
        date = default;
        if (!DatePattern.IsMatch(text)) return false;
        var parts = text.Split('/');
        var year = int.Parse(parts[2], CultureInfo.InvariantCulture);
        if (parts[2].Length == 2) year += 2000;
        return DateOnly.TryParseExact($"{year:D4}-{parts[1]}-{parts[0]}", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }
    private static string Join(IEnumerable<StatementWord> words) => string.Join(" ", words.Select(word => word.Text)).Trim();
    private static List<Line> Lines(IReadOnlyList<StatementWord> words)
    {
        var lines = new List<Line>();
        foreach (var word in words.OrderBy(word => word.Top).ThenBy(word => word.Left))
        {
            if (lines.Count == 0 || word.Top - lines[^1].Top > .0035) lines.Add(new Line(word.Top));
            lines[^1].Words.Add(word);
        }
        foreach (var line in lines) line.Words.Sort((a, b) => a.Left.CompareTo(b.Left));
        return lines;
    }
    private static InvalidDataException Unsupported() => new("This isn't a supported complete Maybank savings/current-account statement layout. No entries were added. Scans and credit-card statements are not supported yet.");
    private static InvalidDataException Unbalanced() => new("The statement's running balance or final totals could not be verified. No entries were added. Use the original complete statement PDF.");
    private sealed class Line(double top)
    {
        public double Top { get; } = top;
        public List<StatementWord> Words { get; } = [];
        public string Text => Join(Words);
    }
    private sealed record PendingEntry(DateOnly Date, decimal Signed, decimal Balance, string Description)
    {
        public List<string> Continuation { get; } = [];
    }
}
