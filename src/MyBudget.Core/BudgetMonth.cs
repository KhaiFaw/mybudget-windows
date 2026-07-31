using System.Globalization;

namespace MyBudget.Core;

/// <summary>
/// Identifies one calendar month without introducing time-zone concerns.
/// </summary>
public readonly record struct BudgetMonth
{
    public BudgetMonth(int year, int month)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be between 1 and 9999.");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public bool IsValid => Year is >= 1 and <= 9999 && Month is >= 1 and <= 12;

    public DateOnly FirstDay
    {
        get
        {
            EnsureValid();
            return new DateOnly(Year, Month, 1);
        }
    }

    public DateOnly LastDay
    {
        get
        {
            EnsureValid();
            return new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));
        }
    }

    public BudgetMonth Next
    {
        get
        {
            var nextMonth = FirstDay.AddMonths(1);
            return FromDate(nextMonth);
        }
    }

    public BudgetMonth Previous
    {
        get
        {
            var previousMonth = FirstDay.AddMonths(-1);
            return FromDate(previousMonth);
        }
    }

    public bool Contains(DateOnly date)
    {
        EnsureValid();
        return date.Year == Year && date.Month == Month;
    }

    public void EnsureValid(string? parameterName = null)
    {
        if (!IsValid)
        {
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(BudgetMonth),
                $"Year={Year}, Month={Month}",
                "A budget month must contain a year from 1 through 9999 and a month from 1 through 12.");
        }
    }

    public static BudgetMonth FromDate(DateOnly date) => new(date.Year, date.Month);

    public static BudgetMonth FromDate(DateTime date) => new(date.Year, date.Month);

    public override string ToString()
    {
        EnsureValid();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Year:D4}-{Month:D2}");
    }
}
