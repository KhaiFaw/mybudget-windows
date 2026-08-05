namespace MyBudget.Core;

/// <summary>
/// Keeps a selected day valid when the user moves between calendar months.
/// Dates are deliberately local calendar values; budget entries do not need
/// UTC conversion because they have no time-of-day component.
/// </summary>
public static class BudgetDateSelection
{
    /// <summary>
    /// Returns today's date according to the PC's configured local clock.
    /// </summary>
    public static DateOnly GetLocalToday() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// Uses today's local day number in the selected month, clamping dates such
    /// as the 31st to the last valid day of a shorter month.
    /// </summary>
    public static DateOnly GetDefaultDate(BudgetMonth selectedMonth) =>
        GetDefaultDate(selectedMonth, GetLocalToday());

    /// <summary>
    /// Testable overload that accepts the already-resolved local date.
    /// </summary>
    public static DateOnly GetDefaultDate(BudgetMonth selectedMonth, DateOnly localToday) =>
        MoveToMonth(localToday, selectedMonth);

    /// <summary>
    /// Preserves the selected day where possible when moving to another month.
    /// </summary>
    public static DateOnly MoveToMonth(DateOnly selectedDate, BudgetMonth destinationMonth)
    {
        destinationMonth.EnsureValid(nameof(destinationMonth));
        var day = Math.Min(selectedDate.Day, destinationMonth.LastDay.Day);
        return new DateOnly(destinationMonth.Year, destinationMonth.Month, day);
    }
}
