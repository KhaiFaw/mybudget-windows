namespace MyBudget.Core;

/// <summary>
/// Resolves recurring income against local calendar dates. Days 29-31 clamp to
/// the last day of shorter months, matching recurring-bill behavior.
/// </summary>
public static class RecurringIncomeSchedule
{
    public static DateOnly? GetDepositDate(RecurringIncome income, BudgetMonth month)
    {
        ArgumentNullException.ThrowIfNull(income);
        month.EnsureValid(nameof(month));
        Validate(income);

        if (!income.IsActive)
        {
            return null;
        }

        var depositDate = RecurringDateCalculator.GetDueDate(month, income.PayDay);
        if (income.StartDate is not null && depositDate < income.StartDate.Value)
        {
            return null;
        }

        if (income.EndDate is not null && depositDate > income.EndDate.Value)
        {
            return null;
        }

        return depositDate;
    }

    public static DateOnly? GetNextDepositDate(RecurringIncome income, DateOnly onOrAfter)
    {
        ArgumentNullException.ThrowIfNull(income);
        Validate(income);

        if (!income.IsActive || income.EndDate is not null && income.EndDate.Value < onOrAfter)
        {
            return null;
        }

        var effectiveStart = income.StartDate is not null && income.StartDate.Value > onOrAfter
            ? income.StartDate.Value
            : onOrAfter;
        var month = BudgetMonth.FromDate(effectiveStart);

        while (true)
        {
            var depositDate = GetDepositDate(income, month);
            if (depositDate is not null && depositDate.Value >= effectiveStart)
            {
                return depositDate;
            }

            if (income.EndDate is not null && month.LastDay >= income.EndDate.Value)
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
    /// Enumerates all deposits whose concrete dates fall in the inclusive
    /// interval. Persistence uses the source and concrete date as its unique
    /// occurrence key, making repeated synchronization safe.
    /// </summary>
    public static IReadOnlyList<RecurringIncomeOccurrence> GetOccurrences(
        RecurringIncome income,
        DateOnly fromInclusive,
        DateOnly throughInclusive)
    {
        ArgumentNullException.ThrowIfNull(income);
        Validate(income);

        if (fromInclusive > throughInclusive)
        {
            throw new ArgumentException("The occurrence start date cannot be after its end date.", nameof(fromInclusive));
        }

        if (!income.IsActive)
        {
            return Array.Empty<RecurringIncomeOccurrence>();
        }

        var occurrences = new List<RecurringIncomeOccurrence>();
        var month = BudgetMonth.FromDate(fromInclusive);
        var finalMonth = BudgetMonth.FromDate(throughInclusive);

        while (true)
        {
            var depositDate = GetDepositDate(income, month);
            if (depositDate is not null
                && depositDate.Value >= fromInclusive
                && depositDate.Value <= throughInclusive)
            {
                occurrences.Add(new RecurringIncomeOccurrence(income, depositDate.Value));
            }

            if (month == finalMonth)
            {
                break;
            }

            month = month.Next;
        }

        return occurrences;
    }

    public static void Validate(RecurringIncome income)
    {
        ArgumentNullException.ThrowIfNull(income);

        if (string.IsNullOrWhiteSpace(income.Name))
        {
            throw new ArgumentException("A recurring income source needs a name.", nameof(income));
        }

        if (income.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(income), income.Amount, "Recurring income cannot be negative.");
        }

        if (income.PayDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(income), income.PayDay, "Pay day must be between 1 and 31.");
        }

        if (income.StartDate is not null
            && income.EndDate is not null
            && income.StartDate.Value > income.EndDate.Value)
        {
            throw new ArgumentException("A recurring income's start date cannot be after its end date.", nameof(income));
        }
    }
}
