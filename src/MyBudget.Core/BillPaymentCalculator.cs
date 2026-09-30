namespace MyBudget.Core;

public static class BillPaymentCalculator
{
    public static IReadOnlyList<MonthlyBillStatus> GetMonth(
        IEnumerable<RecurringBill> bills,
        IEnumerable<BillPayment> payments,
        BudgetMonth month)
    {
        ArgumentNullException.ThrowIfNull(bills);
        ArgumentNullException.ThrowIfNull(payments);
        month.EnsureValid(nameof(month));
        var paid = payments.Where(payment => payment.Month == month)
            .ToDictionary(payment => payment.BillId);

        return bills.Select(bill =>
            {
                paid.TryGetValue(bill.Id, out var payment);
                // Preserve the original paid amount and due date after schedule edits.
                var dueDate = payment?.DueDate ?? RecurringDateCalculator.GetDueDate(bill, month);
                return dueDate is null ? null : new MonthlyBillStatus(bill, month, dueDate.Value, payment);
            })
            .OfType<MonthlyBillStatus>()
            .OrderBy(item => item.IsPaid)
            .ThenBy(item => item.DueDate)
            .ThenBy(item => item.Bill.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Bill.Id)
            .ToArray();
    }

    /// <summary>
    /// Returns each bill's earliest unpaid occurrence since payment tracking
    /// began. Overdue occurrences remain outstanding, even across month changes.
    /// Payment keys use the due month, never the month money was paid early.
    /// </summary>
    public static IReadOnlyList<RecurringBillOccurrence> GetNextUnpaidBills(
        IEnumerable<RecurringBill> bills,
        IEnumerable<BillPayment> payments,
        DateOnly localToday)
    {
        ArgumentNullException.ThrowIfNull(bills);
        ArgumentNullException.ThrowIfNull(payments);
        var paid = payments.Select(payment => (payment.BillId, payment.Month)).ToHashSet();
        var result = new List<RecurringBillOccurrence>();

        foreach (var bill in bills)
        {
            var from = bill.PaymentTrackingStart ?? BudgetMonth.FromDate(localToday).FirstDay;
            var due = RecurringDateCalculator.GetNextDueDate(bill, from);
            while (due is not null)
            {
                var month = BudgetMonth.FromDate(due.Value);
                if (!paid.Contains((bill.Id, month)))
                {
                    result.Add(new RecurringBillOccurrence(bill, due.Value,
                        RecurringDateCalculator.GetDaysUntilDue(localToday, due.Value)));
                    break;
                }

                if (month.Year == 9999 && month.Month == 12) break;
                due = RecurringDateCalculator.GetNextDueDate(bill, month.Next.FirstDay);
            }
        }

        return result.OrderBy(item => item.DueDate)
            .ThenBy(item => item.Bill.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Bill.Id)
            .ToArray();
    }
}
