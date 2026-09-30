namespace MyBudget.Core;

/// <summary>Evaluates estimates without posting income, spending, savings or carry-forward.</summary>
public static class PlanningCalculator
{
    public static PlanningScenario Calculate(
        PlanningProfile profile,
        SubscriptionScenario subscriptionScenario,
        BudgetMonth? month = null,
        bool useOriginal = false)
    {
        Validate(profile);
        month?.EnsureValid();
        var plan = useOriginal ? profile.Original : profile.Current;
        var firstMonth = month?.Contains(plan.EmploymentStart) == true;
        var actualFullPay = !useOriginal && !firstMonth && profile.Payslip is { IsFullMonth: true, TakeHome: not null };
        var income = firstMonth ? plan.FirstMonthEnvelope
            : actualFullPay ? profile.Payslip!.TakeHome!.Value : plan.ProvisionalTakeHome;
        var subscription = plan.Costs.SingleOrDefault(cost => cost.Purpose == PlanningPurpose.Subscription);
        var subscriptionAmount = subscription?.Amount;
        var changeApplies = false;
        var warnings = new List<string>();

        if (subscriptionScenario == SubscriptionScenario.Proposed && plan.ProposedSubscriptionAmount.HasValue)
        {
            changeApplies = IsEffective(plan.ProposedSubscriptionFrom, month);
            if (changeApplies) subscriptionAmount = plan.ProposedSubscriptionAmount;
            warnings.Add("Subscription change is proposed, not a confirmed charge or saving.");
        }
        else if (subscriptionScenario == SubscriptionScenario.Confirmed)
        {
            if (!useOriginal && profile.ConfirmedSubscriptionAmount.HasValue && profile.ConfirmedSubscriptionFrom.HasValue)
            {
                changeApplies = IsEffective(profile.ConfirmedSubscriptionFrom, month);
                if (changeApplies) subscriptionAmount = profile.ConfirmedSubscriptionAmount;
            }
            else warnings.Add("No confirmed subscription amount and effective month; current cost retained.");
        }

        var foodAdjustment = month.HasValue
            ? plan.Costs.Where(cost => cost.Purpose == PlanningPurpose.Food && cost.Amount.HasValue && cost.DailyRate.HasValue)
                .Sum(cost => Round(cost.DailyRate!.Value * month.Value.LastDay.Day) - cost.Amount!.Value)
            : 0m;
        var buffer = plan.Costs.SingleOrDefault(cost => cost.Purpose == PlanningPurpose.Buffer);
        // Calendar food is funded from the buffer first. Never reduce a fixed commitment.
        var adjustedBuffer = buffer?.Amount is { } bufferAmount ? Math.Max(0m, bufferAmount - foodAdjustment) : (decimal?)null;
        var extra = firstMonth ? 0m : Math.Max(0m, income - plan.SurplusThreshold);
        var extraEmergency = Round(extra * 0.5m);
        var extraGoals = Round(extra * 0.3m);
        var extraLifestyle = extra - extraEmergency - extraGoals;
        var rows = new List<PlannedCost>();

        foreach (var cost in plan.Costs)
        {
            var amount = cost.Amount;
            var basis = cost.Basis;
            if (cost.Purpose == PlanningPurpose.Subscription)
            {
                amount = subscriptionAmount;
                if (changeApplies) basis = subscriptionScenario == SubscriptionScenario.Proposed
                    ? "Proposed amount · confirm plan change and actual bill" : "Confirmed subscription charge";
            }
            if (cost.Purpose == PlanningPurpose.Food && month.HasValue && cost.DailyRate.HasValue && cost.Amount.HasValue)
            {
                amount = Round(cost.DailyRate.Value * month.Value.LastDay.Day);
                basis = $"{month.Value.LastDay.Day} calendar days × {cost.DailyRate.Value:0.00}; original base retained for comparison";
            }
            if (cost.Purpose == PlanningPurpose.Buffer)
            {
                amount = adjustedBuffer;
                if (foodAdjustment != 0m) basis = "Unassigned buffer after calendar-food adjustment";
            }
            if (cost.Purpose == PlanningPurpose.Lifestyle && amount.HasValue) amount += extraLifestyle;
            rows.Add(new PlannedCost(cost.Id, cost.Name, amount, cost.Bucket, basis, cost.Purpose));
        }

        if (extraGoals > 0m) rows.Add(new PlannedCost("additional-goals", "Additional goals allocation", extraGoals,
            PlanningBucket.Goals, "30% of recurring take-home above the separate threshold; specific goals unconfirmed"));
        if (extraLifestyle > 0m && !plan.Costs.Any(cost => cost.Purpose == PlanningPurpose.Lifestyle && cost.Amount.HasValue))
            rows.Add(new PlannedCost("additional-lifestyle", "Additional lifestyle allocation", extraLifestyle,
                PlanningBucket.Wants, "20% of recurring take-home above the separate threshold; base lifestyle costs remain unconfirmed"));
        var remaining = income - rows.Sum(cost => cost.Amount ?? 0m);
        var emergency = Math.Max(0m, remaining);
        var subscriptionSaving = subscription?.Amount is { } current && subscriptionAmount.HasValue
            ? current - subscriptionAmount.Value : 0m;
        var target = plan.EmergencySavingsTarget + Math.Max(0m, subscriptionSaving) + extraEmergency;
        rows.Add(new PlannedCost("emergency-savings", "Emergency savings", emergency,
            PlanningBucket.Savings, firstMonth ? "Remainder of the cautious first-month envelope · not savings already held"
                : "Monthly savings target plus subscription saving; not an existing savings balance"));

        if (firstMonth)
        {
            warnings.Add("First-month take-home is a cautious planning envelope, not calculated or confirmed salary. All listed commitments and full calendar-month living costs remain funded; work costs are not silently prorated.");
            foreach (var cost in plan.Costs.Where(cost => cost.FixedCommitment && cost.Amount > 0m))
            {
                var due = cost.DueDay.HasValue
                    ? new DateOnly(month!.Value.Year, month.Value.Month, Math.Min(cost.DueDay.Value, month.Value.LastDay.Day)) : (DateOnly?)null;
                if (!profile.FirstPayday.HasValue)
                    warnings.Add($"{cost.Name}: {(due.HasValue ? $"due {due:dd MMM}; " : "due date unconfirmed; ")}first payday unconfirmed — may require money before salary arrives.");
                else if (!due.HasValue)
                    warnings.Add($"{cost.Name}: due date unconfirmed — check against first payday {profile.FirstPayday:dd MMM yyyy}.");
                else if (due.Value < profile.FirstPayday.Value)
                    warnings.Add($"BEFORE FIRST PAYDAY: {cost.Name} is due {due:dd MMM}, before {profile.FirstPayday:dd MMM yyyy}. Arrange opening cash; the plan does not assume existing savings.");
            }
        }
        if (rows.Any(cost => !cost.Amount.HasValue) || plan.UnconfirmedItems.Count > 0)
            warnings.Add("This balances only the supplied allocations. Unconfirmed costs and balances remain unknown, not zero; they may reduce savings or the buffer.");
        if (foodAdjustment > 0m)
            warnings.Add($"Calendar food needs an extra {foodAdjustment:0.00}; covered by the buffer first. Any excess reduces the savings remainder.");
        if (remaining < 0m) warnings.Add("Known allocations exceed take-home; fixed commitments were preserved rather than silently cut.");

        return new PlanningScenario(income, actualFullPay, firstMonth, changeApplies, subscriptionSaving, target,
            emergency, Math.Max(0m, target - emergency), Math.Max(0m, -remaining), foodAdjustment,
            extra, extraEmergency, extraGoals, extraLifestyle, rows, warnings);
    }

    public static void Validate(PlanningProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion != 1) throw new ArgumentException("Unsupported planning-profile version.");
        if (string.IsNullOrWhiteSpace(profile.CurrencyCode)) throw new ArgumentException("A planning currency is required.");
        ValidateDefinition(profile.Original);
        ValidateDefinition(profile.Current);
        NonNegative(profile.ConfirmedSubscriptionAmount, "Confirmed subscription");
        if (profile.ConfirmedSubscriptionAmount.HasValue != profile.ConfirmedSubscriptionFrom.HasValue)
            throw new ArgumentException("Confirm both the subscription amount and its effective month.");
        if (profile.ActualSpending is not null)
            foreach (var amount in profile.ActualSpending.Values) NonNegative(amount, "Actual spending");
        if (profile.Payslip is { } payslip)
        {
            foreach (var amount in new[] { payslip.GrossPay, payslip.TakeHome, payslip.Epf, payslip.Socso, payslip.Eis, payslip.Pcb, payslip.Zakat, payslip.OtherDeductions })
                NonNegative(amount, "Payslip amount");
            if (payslip.IsFullMonth && !payslip.TakeHome.HasValue)
                throw new ArgumentException("A full-month payslip needs its actual take-home amount.");
            if (payslip.TotalDeductions < 0m) throw new ArgumentException("Payslip take-home cannot exceed gross pay.");
            if (payslip.TotalDeductions.HasValue && payslip.ItemizedDeductions.HasValue &&
                payslip.TotalDeductions.Value != payslip.ItemizedDeductions.Value)
                throw new ArgumentException("Itemized deductions must match actual gross pay minus actual take-home.");
        }
    }

    private static void ValidateDefinition(PlanningDefinition plan)
    {
        if (plan is null || plan.Costs is null || plan.UnconfirmedItems is null)
            throw new ArgumentException("The plan needs costs and an unconfirmed-items list.");
        foreach (var amount in new decimal?[] { plan.BasicSalary, plan.ProvisionalTakeHome, plan.FirstMonthEnvelope,
                     plan.EmergencySavingsTarget, plan.ProposedSubscriptionAmount, plan.SurplusThreshold })
            NonNegative(amount, "Plan amount");
        if (plan.ProvisionalTakeHome <= 0m || plan.FirstMonthEnvelope <= 0m || plan.SurplusThreshold <= 0m)
            throw new ArgumentException("Income envelopes and the surplus threshold must be positive.");
        if (plan.ProposedSubscriptionAmount.HasValue != plan.ProposedSubscriptionFrom.HasValue)
            throw new ArgumentException("A proposed subscription needs an amount and effective month.");
        if (plan.Costs.Select(cost => cost.Id).Distinct(StringComparer.Ordinal).Count() != plan.Costs.Count ||
            plan.Costs.Any(cost => string.IsNullOrWhiteSpace(cost.Id) || string.IsNullOrWhiteSpace(cost.Name) ||
                cost.Id is "emergency-savings" or "additional-goals" or "additional-lifestyle"))
            throw new ArgumentException("Cost IDs must be unique and non-empty; savings/extra-goal IDs are reserved.");
        foreach (var purpose in new[] { PlanningPurpose.Subscription, PlanningPurpose.Buffer, PlanningPurpose.Lifestyle })
            if (plan.Costs.Count(cost => cost.Purpose == purpose) > 1) throw new ArgumentException($"Only one {purpose} allocation is supported.");
        foreach (var cost in plan.Costs)
        {
            NonNegative(cost.Amount, cost.Name);
            NonNegative(cost.DailyRate, cost.Name);
            if (!Enum.IsDefined(cost.Bucket) || !Enum.IsDefined(cost.Purpose)) throw new ArgumentException("Invalid allocation type.");
            if (cost.DueDay is < 1 or > 31) throw new ArgumentException("Due day must be between 1 and 31.");
            if (cost.Purpose == PlanningPurpose.Buffer && cost.Bucket != PlanningBucket.Buffer ||
                cost.Purpose is PlanningPurpose.Subscription or PlanningPurpose.Lifestyle && cost.Bucket != PlanningBucket.Wants ||
                cost.Purpose is PlanningPurpose.Food or PlanningPurpose.EssentialFund && cost.Bucket != PlanningBucket.Needs)
                throw new ArgumentException("Food is a need, subscriptions/lifestyle are wants, and buffers are unassigned.");
        }
    }

    private static bool IsEffective(DateOnly? from, BudgetMonth? month) => from.HasValue &&
        (!month.HasValue || month.Value.LastDay >= from.Value);

    private static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static void NonNegative(decimal? amount, string field)
    {
        if (amount < 0m) throw new ArgumentException($"{field} cannot be negative.");
        if (amount.HasValue && Round(amount.Value) != amount.Value) throw new ArgumentException($"{field} needs at most two decimal places.");
    }
}
