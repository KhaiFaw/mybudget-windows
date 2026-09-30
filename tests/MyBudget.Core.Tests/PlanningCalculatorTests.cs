using MyBudget.Core;

namespace MyBudget.Core.Tests;

[TestClass]
public sealed class PlanningCalculatorTests
{
    [TestMethod]
    public void ProposedBudgetBalancesAndRetainsCurrentCostComparison()
    {
        var profile = Example();
        var current = PlanningCalculator.Calculate(profile, SubscriptionScenario.Current);
        var proposed = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed);
        Assert.AreEqual(4000m, proposed.Total);
        Assert.AreEqual(1050m, current.EmergencyAllocation);
        Assert.AreEqual(1270m, proposed.EmergencyAllocation);
        Assert.AreEqual(220m, proposed.SubscriptionSaving);
        Assert.AreEqual(1270m, proposed.EmergencyTarget);
        Assert.AreEqual(300m, profile.Original.Costs.Single(cost => cost.Id == "subscription").Amount);
    }

    [TestMethod]
    public void AllocationPercentagesAreDescriptiveAndBufferStaysSeparate()
    {
        var result = PlanningCalculator.Calculate(Example(), SubscriptionScenario.Proposed);
        Assert.AreEqual(60.75m, result.PercentFor(PlanningBucket.Needs));
        Assert.AreEqual(5m, result.PercentFor(PlanningBucket.Wants));
        Assert.AreEqual(31.75m, result.PercentFor(PlanningBucket.Savings));
        Assert.AreEqual(2.5m, result.PercentFor(PlanningBucket.Buffer));
        Assert.AreEqual(100m, Enum.GetValues<PlanningBucket>().Sum(result.PercentFor));
        Assert.AreEqual(2100m, result.KnownEssentialCosts);
        Assert.AreEqual(330m, result.FutureEssentialFunds);
    }

    [TestMethod]
    public void FirstMonthUsesCautiousEnvelopeAndFullyFundsFixedCommitments()
    {
        var result = PlanningCalculator.Calculate(Example(), SubscriptionScenario.Proposed, new BudgetMonth(2026, 11));
        Assert.AreEqual(3300m, result.Income);
        Assert.AreEqual(570m, result.EmergencyAllocation);
        Assert.AreEqual(700m, result.TargetShortfall);
        Assert.AreEqual(350m, result.Costs.Single(cost => cost.Id == "car").Amount);
        Assert.AreEqual(450m, result.Costs.Single(cost => cost.Id == "parents").Amount);
        Assert.AreEqual(400m, result.Costs.Single(cost => cost.Id == "fuel").Amount);
        Assert.IsFalse(result.IsActualFullMonthPay);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("not calculated or confirmed salary")));
    }

    [TestMethod]
    public void CalendarFoodExtraComesFromBufferWithoutChangingTemplate()
    {
        var profile = Example();
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed, new BudgetMonth(2026, 12));
        Assert.AreEqual(20m, result.FoodAdjustment);
        Assert.AreEqual(620m, result.Costs.Single(cost => cost.Id == "food").Amount);
        Assert.AreEqual(80m, result.AmountFor(PlanningBucket.Buffer));
        Assert.AreEqual(1270m, result.EmergencyAllocation);
        Assert.AreEqual(4000m, result.Total);
        Assert.AreEqual(600m, profile.Original.Costs.Single(cost => cost.Id == "food").Amount);
    }

    [TestMethod]
    public void CalendarFoodBeyondBufferReducesSavingsNotFixedBills()
    {
        var profile = Example();
        profile = profile with
        {
            Current = profile.Current with
            {
                Costs = profile.Current.Costs
            .Select(cost => cost.Id == "buffer" ? cost with { Amount = 5m } : cost).ToArray()
            }
        };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed, new BudgetMonth(2026, 12));
        Assert.AreEqual(0m, result.AmountFor(PlanningBucket.Buffer));
        Assert.AreEqual(1350m, result.EmergencyAllocation);
        Assert.AreEqual(4000m, result.Total);
    }

    [TestMethod]
    public void LeapYearFoodIsCalendarCorrect()
    {
        var result = PlanningCalculator.Calculate(Example(), SubscriptionScenario.Proposed, new BudgetMonth(2028, 2));
        Assert.AreEqual(580m, result.Costs.Single(cost => cost.Id == "food").Amount);
        Assert.AreEqual(120m, result.AmountFor(PlanningBucket.Buffer));
        Assert.AreEqual(4000m, result.Total);
    }

    [TestMethod]
    public void UnknownCostsAndActualDeductionsStayNull()
    {
        var profile = Example();
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed);
        Assert.IsNull(result.Costs.Single(cost => cost.Id == "utilities").Amount);
        Assert.IsNull(profile.Payslip);
        Assert.IsNull(profile.ActualSpending);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("unknown, not zero")));
        Assert.IsNull(new PlanningPayslip().TotalDeductions);
        Assert.IsNull(new PlanningPayslip().ItemizedDeductions);
    }

    [TestMethod]
    public void ProposedChangeDoesNotApplyBeforeItsStartMonth()
    {
        var result = PlanningCalculator.Calculate(Example(), SubscriptionScenario.Proposed, new BudgetMonth(2026, 10));
        Assert.AreEqual(300m, result.Costs.Single(cost => cost.Id == "subscription").Amount);
        Assert.IsFalse(result.SubscriptionChangeApplies);
        Assert.AreEqual(0m, result.SubscriptionSaving);
    }

    [TestMethod]
    public void UnconfirmedChangeCannotBeDisplayedAsConfirmed()
    {
        var result = PlanningCalculator.Calculate(Example(), SubscriptionScenario.Confirmed);
        Assert.AreEqual(300m, result.Costs.Single(cost => cost.Id == "subscription").Amount);
        Assert.AreEqual(1050m, result.EmergencyAllocation);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("No confirmed subscription")));
    }

    [TestMethod]
    public void ConfirmedBillReplacesEstimateOnlyInConfirmedScenario()
    {
        var profile = Example() with { ConfirmedSubscriptionAmount = 90m, ConfirmedSubscriptionFrom = new DateOnly(2026, 11, 1) };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Confirmed);
        Assert.AreEqual(90m, result.Costs.Single(cost => cost.Id == "subscription").Amount);
        Assert.AreEqual(1260m, result.EmergencyAllocation);
        Assert.AreEqual(80m, PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed).Costs.Single(cost => cost.Id == "subscription").Amount);
    }

    [TestMethod]
    public void FullPayslipRecalculatesBudgetWithoutReplacingOriginalEnvelope()
    {
        var profile = Example() with { Payslip = new PlanningPayslip(true, 4700m, 4200m, 400m, 20m, 10m, 70m, 0m, 0m) };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed);
        Assert.IsTrue(result.IsActualFullMonthPay);
        Assert.AreEqual(4200m, result.Income);
        Assert.AreEqual(1370m, result.EmergencyAllocation);
        Assert.AreEqual(60m, result.ExtraGoals);
        Assert.AreEqual(40m, result.ExtraLifestyle);
        Assert.AreEqual(100m, result.ExtraEmergency);
        Assert.AreEqual(500m, profile.Payslip.TotalDeductions);
        Assert.AreEqual(4000m, PlanningCalculator.Calculate(profile, SubscriptionScenario.Current, useOriginal: true).Income);
        Assert.AreEqual(3300m, PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed, new BudgetMonth(2026, 11)).Income);
    }

    [TestMethod]
    public void PartialPayslipDoesNotBecomeRecurringFullMonthPay()
    {
        var profile = Example() with { Payslip = new PlanningPayslip(false, 3600m, 3100m) };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed);
        Assert.AreEqual(4000m, result.Income);
        Assert.IsFalse(result.IsActualFullMonthPay);
    }

    [TestMethod]
    public void IncomeBelowThresholdHasNoSurplusAllocation()
    {
        var profile = Example() with { Payslip = new PlanningPayslip(true, TakeHome: 3800m) };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed);
        Assert.AreEqual(0m, result.ExtraIncome);
        Assert.AreEqual(1070m, result.EmergencyAllocation);
        Assert.AreEqual(0m, result.AmountFor(PlanningBucket.Goals));
    }

    [TestMethod]
    public void CentRoundingStillAllocatesExactlyTheAdditionalAmount()
    {
        var profile = Example() with { Payslip = new PlanningPayslip(true, TakeHome: 4000.03m) };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed);
        Assert.AreEqual(0.03m, result.ExtraEmergency + result.ExtraGoals + result.ExtraLifestyle);
        Assert.AreEqual(result.Income, result.Total);
    }

    [TestMethod]
    public void BillsBeforeFirstPaydayAreFlaggedAndUnknownDatesAreNotInvented()
    {
        var profile = Example() with { FirstPayday = new DateOnly(2026, 11, 25) };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed, new BudgetMonth(2026, 11));
        Assert.IsTrue(result.Warnings.Any(warning => warning.StartsWith("BEFORE FIRST PAYDAY") && warning.Contains("Phone")));
        Assert.IsFalse(result.Warnings.Any(warning => warning.StartsWith("BEFORE FIRST PAYDAY") && warning.Contains("Car instalment")));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Parents") && warning.Contains("due date unconfirmed")));
    }

    [TestMethod]
    public void UnknownFirstPaydayFlagsPotentialOpeningCashNeeds()
    {
        var result = PlanningCalculator.Calculate(Example(), SubscriptionScenario.Proposed, new BudgetMonth(2026, 11));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("first payday unconfirmed")));
        Assert.IsFalse(result.Warnings.Any(warning => warning.StartsWith("BEFORE FIRST PAYDAY")));
    }

    [TestMethod]
    public void UnderfundedEnvelopeDoesNotSilentlyCutCommitments()
    {
        var profile = Example();
        profile = profile with { Current = profile.Current with { FirstMonthEnvelope = 500m } };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Current, new BudgetMonth(2026, 11));
        Assert.AreEqual(0m, result.EmergencyAllocation);
        Assert.AreEqual(2450m, result.FundingGap);
        Assert.AreEqual(450m, result.Costs.Single(cost => cost.Id == "parents").Amount);
    }

    [TestMethod]
    public void InvalidAndInconsistentActualAmountsAreRejected()
    {
        var profile = Example();
        Assert.ThrowsExactly<ArgumentException>(() => PlanningCalculator.Validate(profile with { Payslip = new PlanningPayslip(true) }));
        Assert.ThrowsExactly<ArgumentException>(() => PlanningCalculator.Validate(profile with { Payslip = new PlanningPayslip(true, 4000m, 4100m) }));
        Assert.ThrowsExactly<ArgumentException>(() => PlanningCalculator.Validate(profile with { Payslip = new PlanningPayslip(true, 4500m, 4000m, 100m, 0m, 0m, 0m, 0m, 0m) }));
        Assert.ThrowsExactly<ArgumentException>(() => PlanningCalculator.Validate(profile with { ConfirmedSubscriptionAmount = 90m }));
        Assert.ThrowsExactly<ArgumentException>(() => PlanningCalculator.Validate(profile with { Current = profile.Current with { FirstMonthEnvelope = -10m } }));
    }

    [TestMethod]
    public void SubscriptionPriceIncreaseDoesNotLowerTheCoreSavingsTarget()
    {
        var profile = Example() with { ConfirmedSubscriptionAmount = 350m, ConfirmedSubscriptionFrom = new DateOnly(2026, 11, 1) };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Confirmed);
        Assert.AreEqual(1050m, result.EmergencyTarget);
        Assert.AreEqual(1000m, result.EmergencyAllocation);
        Assert.AreEqual(50m, result.TargetShortfall);
    }

    [TestMethod]
    public void UnknownBaseLifestyleDoesNotSwallowItsAdditionalIncomeShare()
    {
        var profile = Example();
        profile = profile with
        {
            Payslip = new PlanningPayslip(true, TakeHome: 4200m),
            Current = profile.Current with
            { Costs = profile.Current.Costs.Select(cost => cost.Id == "personal" ? cost with { Amount = null } : cost).ToArray() }
        };
        var result = PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed);
        Assert.IsNull(result.Costs.Single(cost => cost.Id == "personal").Amount);
        Assert.AreEqual(40m, result.Costs.Single(cost => cost.Id == "additional-lifestyle").Amount);
        Assert.AreEqual(4200m, result.Total);
    }

    internal static PlanningProfile Example()
    {
        var plan = new PlanningDefinition(4800m, 4000m, new DateOnly(2026, 11, 5), 3300m, 1050m,
            80m, new DateOnly(2026, 11, 1), 4000m,
            [
                new("housing", "Housing", 0m, PlanningBucket.Needs, "Confirmed no payment"),
                new("parents", "Parents' allowance", 450m, PlanningBucket.Needs, "Firm priority", FixedCommitment: true, FirmPriority: true),
                new("car", "Car instalment", 350m, PlanningBucket.Needs, "Fixed", FixedCommitment: true, DueDay: 30),
                new("insurance", "Insurance", 130m, PlanningBucket.Needs, "Fixed", FixedCommitment: true),
                new("food", "Food", 600m, PlanningBucket.Needs, "Daily estimate", PlanningPurpose.Food, DailyRate: 20m),
                new("fuel", "Work travel", 400m, PlanningBucket.Needs, "Up to this amount"),
                new("phone", "Phone", 40m, PlanningBucket.Needs, "Fixed", FixedCommitment: true, DueDay: 14),
                new("tolls", "Tolls and parking", 130m, PlanningBucket.Needs, "Provisional"),
                new("car-fund", "Car sinking fund", 250m, PlanningBucket.Needs, "Future essential cost", PlanningPurpose.EssentialFund),
                new("medical", "Medical fund", 80m, PlanningBucket.Needs, "Provisional future essential cost", PlanningPurpose.EssentialFund),
                new("subscription", "Subscription", 300m, PlanningBucket.Wants, "Current bill", PlanningPurpose.Subscription),
                new("personal", "Personal spending", 120m, PlanningBucket.Wants, "Plan", PlanningPurpose.Lifestyle),
                new("buffer", "Monthly buffer", 100m, PlanningBucket.Buffer, "Unassigned", PlanningPurpose.Buffer),
                new("utilities", "Household contribution", null, PlanningBucket.Needs, "Unconfirmed"),
            ], ["Existing savings and goals unconfirmed"]);
        return new PlanningProfile(1, new DateOnly(2026, 9, 30), "MYR", plan, plan);
    }
}
