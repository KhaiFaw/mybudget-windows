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

        if (bill.StartDate is not null && bill.EndDate is not null && bill.StartDate.Value > bill.EndDate.Value)
        {
            throw new ArgumentException("A recurring bill's start date cannot be after its end date.", nameof(bill));
        }

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
}
