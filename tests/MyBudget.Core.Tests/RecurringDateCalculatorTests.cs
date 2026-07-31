namespace MyBudget.Core.Tests;

[TestClass]
public sealed class RecurringDateCalculatorTests
{
    [TestMethod]
    public void GetDueDate_UsesRequestedDayWhenMonthContainsIt()
    {
        var result = RecurringDateCalculator.GetDueDate(new BudgetMonth(2026, 7), 15);

        Assert.AreEqual(new DateOnly(2026, 7, 15), result);
    }

    [TestMethod]
    public void GetDueDate_ClampsDay31ToLastDayOfShortMonth()
    {
        Assert.AreEqual(
            new DateOnly(2026, 2, 28),
            RecurringDateCalculator.GetDueDate(new BudgetMonth(2026, 2), 31));

        Assert.AreEqual(
            new DateOnly(2024, 2, 29),
            RecurringDateCalculator.GetDueDate(new BudgetMonth(2024, 2), 31));
    }

    [TestMethod]
    public void GetDueDate_RejectsDayOutsideSupportedRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecurringDateCalculator.GetDueDate(new BudgetMonth(2026, 7), 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecurringDateCalculator.GetDueDate(new BudgetMonth(2026, 7), 32));
    }

    [TestMethod]
    public void GetDueDate_RejectsDefaultBudgetMonth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecurringDateCalculator.GetDueDate(default, 15));
    }

    [TestMethod]
    public void GetDueDate_ForBillReturnsNullWhenInactive()
    {
        var bill = Bill() with { IsActive = false };

        Assert.IsNull(RecurringDateCalculator.GetDueDate(bill, new BudgetMonth(2026, 7)));
    }

    [TestMethod]
    public void GetDueDate_ForBillHonorsInclusiveActiveDateRange()
    {
        var bill = Bill() with
        {
            StartDate = new DateOnly(2026, 7, 31),
            EndDate = new DateOnly(2026, 8, 31),
        };

        Assert.AreEqual(
            new DateOnly(2026, 7, 31),
            RecurringDateCalculator.GetDueDate(bill, new BudgetMonth(2026, 7)));
        Assert.AreEqual(
            new DateOnly(2026, 8, 31),
            RecurringDateCalculator.GetDueDate(bill, new BudgetMonth(2026, 8)));
        Assert.IsNull(RecurringDateCalculator.GetDueDate(bill, new BudgetMonth(2026, 6)));
        Assert.IsNull(RecurringDateCalculator.GetDueDate(bill, new BudgetMonth(2026, 9)));
    }

    [TestMethod]
    public void GetDueDate_ForBillRejectsReversedActiveDateRange()
    {
        var bill = Bill() with
        {
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 7, 31),
        };

        Assert.Throws<ArgumentException>(
            () => RecurringDateCalculator.GetDueDate(bill, new BudgetMonth(2026, 7)));
    }

    private static RecurringBill Bill() => new(
        1,
        "Rent",
        1_800m,
        31,
        1);
}
