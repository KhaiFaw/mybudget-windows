namespace MyBudget.Core.Tests;

[TestClass]
public sealed class RecurringIncomeScheduleTests
{
    [TestMethod]
    public void GetDepositDate_ClampsPayDayToMonthEnd()
    {
        var income = Income() with { PayDay = 31 };

        Assert.AreEqual(
            new DateOnly(2026, 2, 28),
            RecurringIncomeSchedule.GetDepositDate(income, new BudgetMonth(2026, 2)));
        Assert.AreEqual(
            new DateOnly(2024, 2, 29),
            RecurringIncomeSchedule.GetDepositDate(income, new BudgetMonth(2024, 2)));
    }

    [TestMethod]
    public void GetDepositDate_UsesInclusiveActiveRange()
    {
        var income = Income() with
        {
            PayDay = 15,
            StartDate = new DateOnly(2026, 7, 15),
            EndDate = new DateOnly(2026, 8, 15),
        };

        Assert.IsNull(RecurringIncomeSchedule.GetDepositDate(income, new BudgetMonth(2026, 6)));
        Assert.AreEqual(
            new DateOnly(2026, 7, 15),
            RecurringIncomeSchedule.GetDepositDate(income, new BudgetMonth(2026, 7)));
        Assert.AreEqual(
            new DateOnly(2026, 8, 15),
            RecurringIncomeSchedule.GetDepositDate(income, new BudgetMonth(2026, 8)));
        Assert.IsNull(RecurringIncomeSchedule.GetDepositDate(income, new BudgetMonth(2026, 9)));
    }

    [TestMethod]
    public void GetNextDepositDate_SkipsPayDayBeforeStartDate()
    {
        var income = Income() with
        {
            PayDay = 15,
            StartDate = new DateOnly(2026, 7, 20),
        };

        Assert.AreEqual(
            new DateOnly(2026, 8, 15),
            RecurringIncomeSchedule.GetNextDepositDate(income, new DateOnly(2026, 7, 1)));
    }

    [TestMethod]
    public void GetOccurrences_ReturnsOnlyDatesInsideInclusiveWindow()
    {
        var occurrences = RecurringIncomeSchedule.GetOccurrences(
            Income() with { PayDay = 31 },
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 3, 30));

        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28) },
            occurrences.Select(occurrence => occurrence.Date).ToArray());
    }

    [TestMethod]
    public void GetOccurrences_InactiveSourceProducesNone()
    {
        Assert.IsEmpty(RecurringIncomeSchedule.GetOccurrences(
            Income() with { IsActive = false },
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)));
    }

    [TestMethod]
    public void Validate_RejectsInvalidScheduleAndAmount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurringIncomeSchedule.Validate(Income() with { PayDay = 32 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurringIncomeSchedule.Validate(Income() with { Amount = -1m }));
        Assert.Throws<ArgumentException>(() =>
            RecurringIncomeSchedule.Validate(Income() with
            {
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 7, 31),
            }));
    }

    private static RecurringIncome Income() => new(
        1,
        "Salary",
        5_000m,
        25,
        10);
}
