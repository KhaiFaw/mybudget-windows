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

    [TestMethod]
    public void GetNextDueDate_CrossesMonthAndYearBoundaries()
    {
        var bill = Bill() with { DueDay = 5 };

        Assert.AreEqual(
            new DateOnly(2027, 1, 5),
            RecurringDateCalculator.GetNextDueDate(bill, new DateOnly(2026, 12, 31)));
    }

    [TestMethod]
    public void GetNextDueDate_ClampsEndOfMonthAndIncludesToday()
    {
        var bill = Bill() with { DueDay = 31 };

        Assert.AreEqual(
            new DateOnly(2026, 2, 28),
            RecurringDateCalculator.GetNextDueDate(bill, new DateOnly(2026, 2, 28)));
        Assert.AreEqual(
            new DateOnly(2024, 2, 29),
            RecurringDateCalculator.GetNextDueDate(bill, new DateOnly(2024, 2, 1)));
    }

    [TestMethod]
    public void GetNextDueDate_RespectsStartAndEndDatesAcrossMonths()
    {
        var startsAfterThisMonthsDueDay = Bill() with
        {
            DueDay = 15,
            StartDate = new DateOnly(2026, 2, 20),
        };
        var endsBeforeClampedDueDay = Bill() with
        {
            DueDay = 31,
            EndDate = new DateOnly(2026, 2, 27),
        };

        Assert.AreEqual(
            new DateOnly(2026, 3, 15),
            RecurringDateCalculator.GetNextDueDate(
                startsAfterThisMonthsDueDay,
                new DateOnly(2026, 2, 1)));
        Assert.IsNull(RecurringDateCalculator.GetNextDueDate(
            endsBeforeClampedDueDay,
            new DateOnly(2026, 2, 1)));
    }

    [TestMethod]
    public void GetUpcomingBills_OrdersOccurrencesAndUsesCalendarDayCountdowns()
    {
        var today = new DateOnly(2026, 1, 30);
        var bills = new[]
        {
            Bill() with { Id = 2, Name = "March rent", DueDay = 1 },
            Bill() with { Id = 1, Name = "Month end", DueDay = 31 },
            Bill() with { Id = 3, Name = "Paused", DueDay = 30, IsActive = false },
        };

        var upcoming = RecurringDateCalculator.GetUpcomingBills(bills, today);

        Assert.HasCount(2, upcoming);
        Assert.AreEqual("Month end", upcoming[0].Bill.Name);
        Assert.AreEqual(new DateOnly(2026, 1, 31), upcoming[0].DueDate);
        Assert.AreEqual(1, upcoming[0].DaysUntilDue);
        Assert.AreEqual("March rent", upcoming[1].Bill.Name);
        Assert.AreEqual(new DateOnly(2026, 2, 1), upcoming[1].DueDate);
        Assert.AreEqual(2, upcoming[1].DaysUntilDue);
    }

    [TestMethod]
    public void GetDaysUntilDue_IsStableAcrossLeapDayAndYearBoundary()
    {
        Assert.AreEqual(
            2,
            RecurringDateCalculator.GetDaysUntilDue(
                new DateOnly(2024, 2, 28),
                new DateOnly(2024, 3, 1)));
        Assert.AreEqual(
            1,
            RecurringDateCalculator.GetDaysUntilDue(
                new DateOnly(2026, 12, 31),
                new DateOnly(2027, 1, 1)));
    }

    private static RecurringBill Bill() => new(
        1,
        "Rent",
        1_800m,
        31,
        1);
}
