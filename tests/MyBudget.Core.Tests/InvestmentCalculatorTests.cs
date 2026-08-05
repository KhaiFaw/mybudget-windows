namespace MyBudget.Core.Tests;

[TestClass]
public sealed class InvestmentCalculatorTests
{
    private static readonly BudgetMonth August = new(2026, 8);
    private static readonly Investment Asb = new(
        1,
        "ASB",
        "ASNB",
        InvestmentKind.UnitTrust,
        "units");
    private static readonly Investment Gold = new(
        2,
        "Maybank Gold",
        "Maybank",
        InvestmentKind.Gold,
        "grams");

    [TestMethod]
    public void Calculate_UsesLinkedContributionsAndLatestValuationAsOfMonth()
    {
        var transactions = new[]
        {
            Contribution(Asb.Id, new DateOnly(2026, 7, 10), 1_000m),
            Contribution(Asb.Id, new DateOnly(2026, 8, 1), 200m),
            Contribution(Asb.Id, new DateOnly(2026, 9, 1), 900m),
            Contribution(Gold.Id, new DateOnly(2026, 8, 2), 300m),
        };
        var valuations = new[]
        {
            Valuation(Asb.Id, new DateOnly(2026, 7, 31), 1_050m),
            Valuation(Asb.Id, new DateOnly(2026, 8, 31), 1_260m),
            Valuation(Asb.Id, new DateOnly(2026, 9, 30), 2_500m),
        };

        var result = InvestmentCalculator.Calculate([Asb, Gold], transactions, valuations, August);
        var asb = result.Positions.Single(position => position.Investment.Id == Asb.Id);
        var gold = result.Positions.Single(position => position.Investment.Id == Gold.Id);

        Assert.AreEqual(1_200m, asb.AllTimeContributions);
        Assert.AreEqual(200m, asb.MonthlyContributions);
        Assert.AreEqual(1_260m, asb.CurrentValue);
        Assert.AreEqual(60m, asb.GainLoss);
        Assert.AreEqual(new DateOnly(2026, 8, 31), asb.LatestValuation?.Date);

        Assert.AreEqual(300m, gold.CurrentValue);
        Assert.AreEqual(0m, gold.GainLoss);
        Assert.IsNull(gold.LatestValuation);

        Assert.AreEqual(1_500m, result.AllTimeContributions);
        Assert.AreEqual(500m, result.MonthlyContributions);
        Assert.AreEqual(1_560m, result.CurrentValue);
        Assert.AreEqual(60m, result.GainLoss);
    }

    [TestMethod]
    public void Calculate_RejectsNonSavingsInvestmentLink()
    {
        var invalid = Contribution(Asb.Id, new DateOnly(2026, 8, 1), 100m) with
        {
            Type = TransactionType.Expense,
        };

        Assert.Throws<ArgumentException>(() =>
            InvestmentCalculator.Calculate([Asb], [invalid], [], August));
    }

    [TestMethod]
    public void Calculate_RejectsNegativeValuationData()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InvestmentCalculator.Calculate(
                [Gold],
                [],
                [Valuation(Gold.Id, new DateOnly(2026, 8, 1), -1m)],
                August));
    }

    private static BudgetTransaction Contribution(long investmentId, DateOnly date, decimal amount) => new(
        Guid.NewGuid(),
        date,
        TransactionType.Savings,
        amount,
        null,
        InvestmentId: investmentId);

    private static InvestmentValuation Valuation(long investmentId, DateOnly date, decimal value) => new(
        Guid.NewGuid(),
        investmentId,
        date,
        value);
}
