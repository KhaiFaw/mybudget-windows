using System.Globalization;

namespace MyBudget.Core;

/// <summary>Explicit allowlist for the only information shared with Notion.</summary>
public sealed record MonthlySummary(string Title, string Marker, string Text)
{
    public static MonthlySummary Create(BudgetSnapshot snapshot)
    {
        var totals = BudgetCalculator.Calculate(snapshot);
        var monthKey = $"{snapshot.Month.Year:D4}-{snapshot.Month.Month:D2}";
        var bills = BillPaymentCalculator.GetMonth(snapshot.Bills, snapshot.BillPayments, snapshot.Month);
        var marker = $"MyBudget monthly summary · {monthKey}";
        string Money(decimal amount) => $"{snapshot.Settings.CurrencyCode} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
        var text = string.Join("\n", new[]
        {
            marker,
            $"Carried forward: {Money(totals.CarryForward)}",
            $"Income recorded: {Money(totals.Income)}",
            $"Spending, less refunds: {Money(totals.Spent)}",
            $"Savings recorded: {Money(totals.Saved)}",
            $"Available in budget: {Money(totals.Available)}",
            $"Budget allocated: {Money(totals.Planned)}",
            $"Bills checked off: {bills.Count(bill => bill.IsPaid)} of {bills.Count}",
            "",
            "Based on recorded entries, not a bank-verified balance. Future plans and estimates are excluded.",
            "Shared manually from MyBudget. No purchase details, account details, statement files or credentials are included."
        });
        return new MonthlySummary($"MyBudget · {monthKey}", marker, text);
    }
}
