namespace MyBudget.Core.Tests;

[TestClass]
public sealed class BudgetDateSelectionTests
{
    [TestMethod]
    public void GetDefaultDate_UsesPcLocalDateWhenSelectedMonthIsCurrent()
    {
        var localToday = new DateOnly(2026, 8, 19);

        var result = BudgetDateSelection.GetDefaultDate(new BudgetMonth(2026, 8), localToday);

        Assert.AreEqual(localToday, result);
    }

    [TestMethod]
    public void MoveToMonth_PreservesSelectedDayAcrossMonthAndYearBoundaries()
    {
        var selectedDate = new DateOnly(2026, 12, 18);

        var result = BudgetDateSelection.MoveToMonth(selectedDate, new BudgetMonth(2027, 1));

        Assert.AreEqual(new DateOnly(2027, 1, 18), result);
    }

    [TestMethod]
    public void MoveToMonth_ClampsEndOfMonthAndLeapYearDates()
    {
        var selectedDate = new DateOnly(2026, 1, 31);

        Assert.AreEqual(
            new DateOnly(2026, 2, 28),
            BudgetDateSelection.MoveToMonth(selectedDate, new BudgetMonth(2026, 2)));
        Assert.AreEqual(
            new DateOnly(2024, 2, 29),
            BudgetDateSelection.MoveToMonth(selectedDate, new BudgetMonth(2024, 2)));
    }

    [TestMethod]
    public void MoveToMonth_RejectsDefaultMonth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BudgetDateSelection.MoveToMonth(new DateOnly(2026, 8, 1), default));
    }
}
