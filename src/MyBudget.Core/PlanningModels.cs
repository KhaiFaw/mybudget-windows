namespace MyBudget.Core;

public enum PlanningBucket { Needs, Wants, Savings, Goals, Buffer }
public enum PlanningPurpose { Ordinary, Food, Subscription, Lifestyle, Buffer, EssentialFund }
public enum SubscriptionScenario { Current, Proposed, Confirmed }

/// <summary>Unknown amounts are null, never implicit zeroes. Plans are not transactions.</summary>
public sealed record PlanningCost(
    string Id,
    string Name,
    decimal? Amount,
    PlanningBucket Bucket,
    string Basis,
    PlanningPurpose Purpose = PlanningPurpose.Ordinary,
    bool FixedCommitment = false,
    bool FirmPriority = false,
    int? DueDay = null,
    decimal? DailyRate = null);

public sealed record PlanningDefinition(
    decimal? BasicSalary,
    decimal ProvisionalTakeHome,
    DateOnly EmploymentStart,
    decimal FirstMonthEnvelope,
    decimal EmergencySavingsTarget,
    decimal? ProposedSubscriptionAmount,
    DateOnly? ProposedSubscriptionFrom,
    decimal SurplusThreshold,
    IReadOnlyList<PlanningCost> Costs,
    IReadOnlyList<string> UnconfirmedItems,
    string Context = "");

/// <summary>Only user-supplied payslip figures belong here; absence does not mean zero.</summary>
public sealed record PlanningPayslip(
    bool IsFullMonth = false,
    decimal? GrossPay = null,
    decimal? TakeHome = null,
    decimal? Epf = null,
    decimal? Socso = null,
    decimal? Eis = null,
    decimal? Pcb = null,
    decimal? Zakat = null,
    decimal? OtherDeductions = null)
{
    public decimal? TotalDeductions => GrossPay.HasValue && TakeHome.HasValue
        ? GrossPay.Value - TakeHome.Value : null;

    public decimal? ItemizedDeductions =>
        Epf.HasValue && Socso.HasValue && Eis.HasValue && Pcb.HasValue && Zakat.HasValue && OtherDeductions.HasValue
            ? Epf + Socso + Eis + Pcb + Zakat + OtherDeductions : null;
}

public sealed record PlanningProfile(
    int SchemaVersion,
    DateOnly RecordedOn,
    string CurrencyCode,
    PlanningDefinition Original,
    PlanningDefinition Current,
    PlanningPayslip? Payslip = null,
    DateOnly? FirstPayday = null,
    decimal? ConfirmedSubscriptionAmount = null,
    DateOnly? ConfirmedSubscriptionFrom = null,
    IReadOnlyDictionary<string, decimal?>? ActualSpending = null);

public sealed record PlannedCost(
    string Id, string Name, decimal? Amount, PlanningBucket Bucket, string Basis,
    PlanningPurpose Purpose = PlanningPurpose.Ordinary);

public sealed record PlanningScenario(
    decimal Income,
    bool IsActualFullMonthPay,
    bool IsFirstMonth,
    bool SubscriptionChangeApplies,
    decimal SubscriptionSaving,
    decimal EmergencyTarget,
    decimal EmergencyAllocation,
    decimal TargetShortfall,
    decimal FundingGap,
    decimal FoodAdjustment,
    decimal ExtraIncome,
    decimal ExtraEmergency,
    decimal ExtraGoals,
    decimal ExtraLifestyle,
    IReadOnlyList<PlannedCost> Costs,
    IReadOnlyList<string> Warnings)
{
    public decimal Total => Costs.Sum(cost => cost.Amount ?? 0m);
    public decimal KnownEssentialCosts => Costs.Where(cost => cost.Bucket == PlanningBucket.Needs &&
        cost.Purpose != PlanningPurpose.EssentialFund).Sum(cost => cost.Amount ?? 0m);
    public decimal FutureEssentialFunds => Costs.Where(cost => cost.Purpose == PlanningPurpose.EssentialFund)
        .Sum(cost => cost.Amount ?? 0m);
    public decimal AmountFor(PlanningBucket bucket) => Costs
        .Where(cost => cost.Bucket == bucket).Sum(cost => cost.Amount ?? 0m);
    public decimal PercentFor(PlanningBucket bucket) => Income > 0m ? AmountFor(bucket) / Income * 100m : 0m;
}
