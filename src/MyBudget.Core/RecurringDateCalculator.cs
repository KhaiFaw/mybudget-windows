namespace MyBudget.Core;

public static class RecurringDateCalculator
{
    public static DateOnly GetDueDate(BudgetMonth month, int dueDay)
    {
        month.EnsureValid(nameof(month));
        if (dueDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(dueDay), dueDay, "Due day must be between 1 and 31.");
        }

        var clampedDay = Math.Min(dueDay, DateTime.DaysInMonth(month.Year, month.Month));
        return new DateOnly(month.Year, month.Month, clampedDay);
    }

    public static DateOnly? GetDueDate(RecurringBill bill, BudgetMonth month)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ValidateSchedule(bill);

        if (!bill.IsActive)
        {
            return null;
        }

        var dueDate = GetDueDate(month, bill.DueDay);

        if (bill.StartDate is not null && dueDate < bill.StartDate.Value)
        {
            return null;
        }

        if (bill.EndDate is not null && dueDate > bill.EndDate.Value)
        {
            return null;
        }

        return dueDate;
    }

    /// <summary>
    /// Finds the first bill occurrence on or after the supplied local calendar
    /// date. A due day of 29-31 is clamped independently in every month.
    /// </summary>
    public static DateOnly? GetNextDueDate(RecurringBill bill, DateOnly onOrAfter)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ValidateSchedule(bill);

        if (!bill.IsActive || bill.EndDate is not null && bill.EndDate.Value < onOrAfter)
        {
            return null;
        }

        var effectiveStart = bill.StartDate is not null && bill.StartDate.Value > onOrAfter
            ? bill.StartDate.Value
            : onOrAfter;
        var month = BudgetMonth.FromDate(effectiveStart);

        while (true)
        {
            var dueDate = GetDueDate(month, bill.DueDay);
            if (dueDate >= effectiveStart
                && (bill.StartDate is null || dueDate >= bill.StartDate.Value)
                && (bill.EndDate is null || dueDate <= bill.EndDate.Value))
            {
                return dueDate;
            }

            if (bill.EndDate is not null && dueDate >= bill.EndDate.Value)
            {
                return null;
            }

            if (month.Year == 9999 && month.Month == 12)
            {
                return null;
            }

            month = month.Next;
        }
    }

    /// <summary>
    /// Returns one next occurrence for each active bill, ordered by due date.
    /// </summary>
    public static IReadOnlyList<RecurringBillOccurrence> GetUpcomingBills(
        IEnumerable<RecurringBill> bills,
        DateOnly onOrAfter)
    {
        ArgumentNullException.ThrowIfNull(bills);

        return bills
            .Select(bill => (Bill: bill, DueDate: GetNextDueDate(bill, onOrAfter)))
            .Where(item => item.DueDate is not null)
            .Select(item => new RecurringBillOccurrence(
                item.Bill,
                item.DueDate!.Value,
                GetDaysUntilDue(onOrAfter, item.DueDate.Value)))
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.Bill.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Bill.Id)
            .ToArray();
    }

    public static int GetDaysUntilDue(DateOnly localToday, DateOnly dueDate) =>
        dueDate.DayNumber - localToday.DayNumber;

    private static void ValidateSchedule(RecurringBill bill)
    {
        if (bill.StartDate is not null && bill.EndDate is not null && bill.StartDate.Value > bill.EndDate.Value)
        {
            throw new ArgumentException("A recurring bill's start date cannot be after its end date.", nameof(bill));
        }
    }
}
