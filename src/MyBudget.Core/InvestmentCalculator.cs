namespace MyBudget.Core;

public static class InvestmentCalculator
{
    /// <summary>
    /// Builds an as-of-month portfolio. Contributions come from linked savings
    /// transactions; a valuation overrides contributed cost as the displayed
    /// market value only when it is dated on or before the selected month end.
    /// </summary>
    public static InvestmentPortfolioSummary Calculate(
        IEnumerable<Investment> investments,
        IEnumerable<BudgetTransaction> transactions,
        IEnumerable<InvestmentValuation> valuations,
        BudgetMonth month)
    {
        ArgumentNullException.ThrowIfNull(investments);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(valuations);
        month.EnsureValid(nameof(month));

        var transactionArray = transactions.ToArray();
        var valuationArray = valuations.ToArray();
        Validate(transactionArray, valuationArray);

        var positions = investments
            .OrderBy(investment => investment.IsArchived)
            .ThenBy(investment => investment.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(investment => investment.Id)
            .Select(investment => CreatePosition(investment, transactionArray, valuationArray, month))
            .ToArray();

        return new InvestmentPortfolioSummary(
            positions,
            positions.Sum(position => position.AllTimeContributions),
            positions.Sum(position => position.MonthlyContributions),
            positions.Sum(position => position.CurrentValue),
            positions.Sum(position => position.GainLoss));
    }

    private static InvestmentPosition CreatePosition(
        Investment investment,
        IReadOnlyList<BudgetTransaction> transactions,
        IReadOnlyList<InvestmentValuation> valuations,
        BudgetMonth month)
    {
        var contributions = transactions
            .Where(transaction => transaction.Type == TransactionType.Savings)
            .Where(transaction => transaction.InvestmentId == investment.Id)
            .Where(transaction => transaction.Date <= month.LastDay)
            .ToArray();
        var allTimeContributions = contributions.Sum(transaction => transaction.Amount);
        var monthlyContributions = contributions
            .Where(transaction => month.Contains(transaction.Date))
            .Sum(transaction => transaction.Amount);
        var latestValuation = valuations
            .Where(valuation => valuation.InvestmentId == investment.Id)
            .Where(valuation => valuation.Date <= month.LastDay)
            .OrderByDescending(valuation => valuation.Date)
            .ThenByDescending(valuation => valuation.Id)
            .FirstOrDefault();
        var currentValue = latestValuation?.MarketValue ?? allTimeContributions;

        return new InvestmentPosition(
            investment,
            allTimeContributions,
            monthlyContributions,
            currentValue,
            currentValue - allTimeContributions,
            latestValuation);
    }

    private static void Validate(
        IEnumerable<BudgetTransaction> transactions,
        IEnumerable<InvestmentValuation> valuations)
    {
        foreach (var transaction in transactions.Where(transaction => transaction.InvestmentId is not null))
        {
            if (transaction.Amount < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(transactions),
                    transaction.Amount,
                    "Investment contribution amounts must be zero or greater.");
            }

            TransactionDestinationRules.Validate(transaction, nameof(transactions));
        }

        foreach (var valuation in valuations)
        {
            if (valuation.MarketValue < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(valuations),
                    valuation.MarketValue,
                    "Investment market values must be zero or greater.");
            }

            if (valuation.Units < 0m || valuation.UnitPrice < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(valuations),
                    "Investment units and unit prices must be zero or greater when supplied.");
            }
        }
    }
}
