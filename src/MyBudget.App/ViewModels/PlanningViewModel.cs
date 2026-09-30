using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using MyBudget.Core;
using MyBudget.Infrastructure;

namespace MyBudget.App.ViewModels;

public sealed record PlanningChoice(string Label, SubscriptionScenario Subscription, bool FirstMonth);
public sealed record PlanningComparisonRow(string Name, string Basis, string Original, string Planned, string Actual);
public sealed record PlanningShareRow(string Name, string Amount, string Percent);

public sealed class PlanningViewModel : ObservableObject
{
    private readonly PlanningProfileStore _store;
    private PlanningProfile? _profile;
    private PlanningChoice? _selectedChoice;
    private bool _busy;
    private string _status = "Opening private planning figures…";

    public PlanningViewModel(PlanningProfileStore store)
    {
        _store = store;
        ReloadCommand = new AsyncRelayCommand(LoadAsync, () => !_busy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !_busy && _profile is not null);
        _selectedChoice = Choices[0];
    }

    public IAsyncRelayCommand ReloadCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IReadOnlyList<PlanningChoice> Choices { get; } =
    [
        new("Full month · proposed subscription", SubscriptionScenario.Proposed, false),
        new("Full month · current subscription", SubscriptionScenario.Current, false),
        new("First month · proposed subscription", SubscriptionScenario.Proposed, true),
        new("First month · current subscription", SubscriptionScenario.Current, true),
        new("Full month · confirmed subscription", SubscriptionScenario.Confirmed, false),
    ];
    public ObservableCollection<PlanningComparisonRow> Comparisons { get; } = [];
    public ObservableCollection<PlanningShareRow> Shares { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<string> Unknowns { get; } = [];
    public bool HasProfile => _profile is not null;
    public Visibility ProfileVisibility => HasProfile ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => HasProfile ? Visibility.Collapsed : Visibility.Visible;
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public string ProfilePath => _store.ProfilePath;
    public string ContextText { get; private set; } = "";
    public string IncomeContext { get; private set; } = "";
    public string OriginalContext { get; private set; } = "";
    public string CurrentFullSummary { get; private set; } = "";
    public string ProposedFullSummary { get; private set; } = "";
    public string CurrentFirstSummary { get; private set; } = "";
    public string ProposedFirstSummary { get; private set; } = "";
    public string FirstMonthHeading { get; private set; } = "First-month envelope";
    public string FirstMonthContext { get; private set; } = "";
    public string FullIncomeSourceLabel { get; private set; } = "CURRENT-COST COMPARISON · ESTIMATED PAY";
    public string CalendarFoodText { get; private set; } = "";
    public string SurplusRuleText { get; private set; } = "";
    public string ScenarioHeading { get; private set; } = "";
    public string ScenarioBalanceText { get; private set; } = "";
    public string ScenarioTargetText { get; private set; } = "";
    public string EssentialsContextText { get; private set; } = "";
    public string ActualDeductionsText { get; private set; } = "";

    public PlanningChoice? SelectedChoice
    {
        get => _selectedChoice;
        set { if (SetProperty(ref _selectedChoice, value)) RefreshScenario(); }
    }

    // NaN is a deliberately blank NumberBox. It is saved as null, not as a zero deduction.
    public double FullMonthEstimate { get; set; }
    public double FirstMonthEstimate { get; set; }
    public double ProposedSubscription { get; set; } = double.NaN;
    public bool SubscriptionConfirmed { get; set; }
    public double ConfirmedSubscription { get; set; } = double.NaN;
    public DateTimeOffset SubscriptionFrom { get; set; } = DateTimeOffset.Now;
    public bool FirstPaydayKnown { get; set; }
    public DateTimeOffset FirstPayday { get; set; } = DateTimeOffset.Now;
    public bool PayslipIsFullMonth { get; set; }
    public double ActualGrossPay { get; set; } = double.NaN;
    public double ActualTakeHome { get; set; } = double.NaN;
    public double ActualEpf { get; set; } = double.NaN;
    public double ActualSocso { get; set; } = double.NaN;
    public double ActualEis { get; set; } = double.NaN;
    public double ActualPcb { get; set; } = double.NaN;
    public double ActualZakat { get; set; } = double.NaN;
    public double ActualOtherDeductions { get; set; } = double.NaN;

    public async Task LoadAsync()
    {
        SetBusy(true);
        try
        {
            _profile = await _store.LoadAsync();
            if (_profile is null)
            {
                StatusText = "No private planning profile on this PC. Your transaction budget is unchanged.";
                return;
            }
            LoadEditor();
            RefreshAll();
            StatusText = "Private estimates loaded. No salary deposits, spending or savings transactions were created.";
        }
        catch (Exception error) { StatusText = $"Planning profile could not be opened: {error.Message}"; }
        finally
        {
            OnPropertyChanged(nameof(HasProfile));
            OnPropertyChanged(nameof(ProfileVisibility));
            OnPropertyChanged(nameof(EmptyVisibility));
            SetBusy(false);
        }
    }

    private async Task SaveAsync()
    {
        if (_profile is null || _busy) return;
        SetBusy(true);
        try
        {
            var payslip = new PlanningPayslip(PayslipIsFullMonth, Amount(ActualGrossPay), Amount(ActualTakeHome),
                Amount(ActualEpf), Amount(ActualSocso), Amount(ActualEis), Amount(ActualPcb), Amount(ActualZakat), Amount(ActualOtherDeductions));
            var hasPayslip = PayslipIsFullMonth || new[] { payslip.GrossPay, payslip.TakeHome, payslip.Epf, payslip.Socso,
                payslip.Eis, payslip.Pcb, payslip.Zakat, payslip.OtherDeductions }.Any(amount => amount.HasValue);
            var next = _profile with
            {
                Current = _profile.Current with
                {
                    ProvisionalTakeHome = RequiredAmount(FullMonthEstimate, "Full-month estimate"),
                    FirstMonthEnvelope = RequiredAmount(FirstMonthEstimate, "First-month envelope"),
                    ProposedSubscriptionAmount = Amount(ProposedSubscription),
                    ProposedSubscriptionFrom = Amount(ProposedSubscription).HasValue
                        ? _profile.Current.ProposedSubscriptionFrom ?? BudgetMonth.FromDate(_profile.Current.EmploymentStart).FirstDay : null,
                },
                Payslip = hasPayslip ? payslip : null,
                FirstPayday = FirstPaydayKnown ? DateOnly.FromDateTime(FirstPayday.DateTime) : null,
                ConfirmedSubscriptionAmount = SubscriptionConfirmed ? RequiredAmount(ConfirmedSubscription, "Actual subscription bill") : null,
                ConfirmedSubscriptionFrom = SubscriptionConfirmed ? DateOnly.FromDateTime(SubscriptionFrom.DateTime) : null,
            };
            // Clearing a proposal clears its start date as well, keeping absence explicit.
            if (!next.Current.ProposedSubscriptionAmount.HasValue)
                next = next with { Current = next.Current with { ProposedSubscriptionFrom = null } };
            await _store.SaveAsync(next);
            _profile = next;
            RefreshAll();
            StatusText = "Saved locally with a private history copy. Original comparison kept; transaction records unchanged.";
        }
        catch (Exception error) { StatusText = $"Not saved: {error.Message}"; }
        finally { SetBusy(false); }
    }

    public async Task ImportAsync(string path)
    {
        try
        {
            var imported = await new PlanningProfileStore(path).LoadAsync()
                ?? throw new ArgumentException("The selected planning file is empty.");
            await _store.SaveAsync(imported);
            await LoadAsync();
        }
        catch (Exception error) { StatusText = $"Planning file not imported: {error.Message}"; }
    }

    private void LoadEditor()
    {
        var profile = _profile!;
        FullMonthEstimate = (double)profile.Current.ProvisionalTakeHome;
        FirstMonthEstimate = (double)profile.Current.FirstMonthEnvelope;
        ProposedSubscription = Value(profile.Current.ProposedSubscriptionAmount);
        SubscriptionConfirmed = profile.ConfirmedSubscriptionAmount.HasValue;
        ConfirmedSubscription = Value(profile.ConfirmedSubscriptionAmount);
        SubscriptionFrom = LocalDate(profile.ConfirmedSubscriptionFrom ?? profile.Current.ProposedSubscriptionFrom ?? profile.Current.EmploymentStart);
        FirstPaydayKnown = profile.FirstPayday.HasValue;
        FirstPayday = LocalDate(profile.FirstPayday ?? profile.Current.EmploymentStart);
        PayslipIsFullMonth = profile.Payslip?.IsFullMonth == true;
        ActualGrossPay = Value(profile.Payslip?.GrossPay);
        ActualTakeHome = Value(profile.Payslip?.TakeHome);
        ActualEpf = Value(profile.Payslip?.Epf);
        ActualSocso = Value(profile.Payslip?.Socso);
        ActualEis = Value(profile.Payslip?.Eis);
        ActualPcb = Value(profile.Payslip?.Pcb);
        ActualZakat = Value(profile.Payslip?.Zakat);
        ActualOtherDeductions = Value(profile.Payslip?.OtherDeductions);
        OnPropertyChanged(string.Empty);
    }

    private void RefreshAll()
    {
        var profile = _profile!;
        var plan = profile.Current;
        var firstMonth = BudgetMonth.FromDate(plan.EmploymentStart);
        var firstName = plan.EmploymentStart.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        // Stable choice objects prevent a TwoWay ComboBox from clearing selection on reload.
        _selectedChoice ??= Choices[0];
        ContextText = plan.Context;
        var hasActualFullPay = profile.Payslip is { IsFullMonth: true, TakeHome: not null };
        IncomeContext = $"Basic salary: {Money(plan.BasicSalary)} · work starts {plan.EmploymentStart:d MMM yyyy}. " +
            (hasActualFullPay ? $"Using actual full-month take-home {Money(profile.Payslip!.TakeHome)}; provisional baseline {Money(plan.ProvisionalTakeHome)} kept for comparison. "
                : $"Full-month take-home estimate: {Money(plan.ProvisionalTakeHome)} until a full payslip is supplied. ") +
            "No deductions are inferred from the basic salary.";
        FullIncomeSourceLabel = hasActualFullPay ? "CURRENT-COST COMPARISON · ACTUAL FULL-MONTH NET PAY"
            : "CURRENT-COST COMPARISON · ESTIMATED PAY";
        OriginalContext = $"Original figures recorded {profile.RecordedOn:d MMM yyyy}: full-month envelope {Money(profile.Original.ProvisionalTakeHome)}, " +
            $"first-month envelope {Money(profile.Original.FirstMonthEnvelope)}. The original column is the current-cost full-month baseline, not actual spending.";
        CurrentFullSummary = Summary(PlanningCalculator.Calculate(profile, SubscriptionScenario.Current));
        ProposedFullSummary = Summary(PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed));
        CurrentFirstSummary = Summary(PlanningCalculator.Calculate(profile, SubscriptionScenario.Current, firstMonth));
        ProposedFirstSummary = Summary(PlanningCalculator.Calculate(profile, SubscriptionScenario.Proposed, firstMonth));
        FirstMonthHeading = $"{firstName} · cautious envelope";
        FirstMonthContext = $"Work starts {plan.EmploymentStart:d MMM yyyy}; this is a partial-month pay plan, not a payslip or payroll-proration calculation. " +
            "Fixed bills, the parents' allowance, funds and full calendar-month living costs stay funded. Confirm bill dates and first payday before relying on this plan.";
        var food = plan.Costs.FirstOrDefault(cost => cost.Purpose == PlanningPurpose.Food && cost.DailyRate.HasValue);
        CalendarFoodText = food is { Amount: not null, DailyRate: not null }
            ? $"Food base {Money(food.Amount)} at {Money(food.DailyRate)}/day. A 31-day month needs {Money(food.DailyRate * 31)}, " +
              $"an extra {Money(food.DailyRate * 31 - food.Amount)}. Calendar scenarios use the buffer first; the full-month template preserves your original base."
            : "Calendar food adjustments require a supplied daily rate.";
        SurplusRuleText = $"Separate recurring-income rule: above {Money(plan.SurplusThreshold)}, allocate only the extra amount " +
            "50% to emergency savings or investments, 30% to goals, and 20% to lifestyle. This is not the overall needs/wants/savings split. " +
            "Until a destination is chosen, the first share stays in emergency savings; no investment holding or travel fund is invented.";
        ActualDeductionsText = profile.Payslip?.TotalDeductions is { } deductions
            ? $"Actual gross minus actual take-home: {Money(deductions)}. Itemized deductions remain unknown unless supplied."
            : "Actual deductions: not supplied. Actual spending remains blank until recorded separately.";
        Unknowns.Clear();
        foreach (var item in plan.UnconfirmedItems) Unknowns.Add(item);
        RefreshScenario();
        OnPropertyChanged(string.Empty);
    }

    private void RefreshScenario()
    {
        if (_profile is null || SelectedChoice is null) return;
        var month = SelectedChoice.FirstMonth ? BudgetMonth.FromDate(_profile.Current.EmploymentStart) : (BudgetMonth?)null;
        var scenario = PlanningCalculator.Calculate(_profile, SelectedChoice.Subscription, month);
        var original = PlanningCalculator.Calculate(_profile, SubscriptionScenario.Current, useOriginal: true);
        Comparisons.Clear();
        foreach (var cost in scenario.Costs)
        {
            var originalAmount = original.Costs.FirstOrDefault(item => item.Id == cost.Id)?.Amount;
            decimal? actual = null;
            _profile.ActualSpending?.TryGetValue(cost.Id, out actual);
            Comparisons.Add(new PlanningComparisonRow(cost.Name, $"{cost.Bucket} · {cost.Basis}",
                originalAmount.HasValue ? Money(originalAmount) : "Unconfirmed", cost.Amount.HasValue ? Money(cost.Amount) : "Unconfirmed",
                actual.HasValue ? Money(actual) : ""));
        }
        Shares.Clear();
        foreach (var bucket in Enum.GetValues<PlanningBucket>())
        {
            if (bucket == PlanningBucket.Goals && scenario.AmountFor(bucket) == 0m) continue;
            Shares.Add(new PlanningShareRow(bucket == PlanningBucket.Buffer ? "Unassigned buffer" : bucket.ToString(),
                Money(scenario.AmountFor(bucket)), $"{scenario.PercentFor(bucket):0.00}%"));
        }
        Warnings.Clear();
        foreach (var warning in scenario.Warnings) Warnings.Add(warning);
        ScenarioHeading = SelectedChoice.FirstMonth
            ? $"{_profile.Current.EmploymentStart:MMMM yyyy} · {SelectedChoice.Subscription.ToString().ToLowerInvariant()} subscription"
            : SelectedChoice.Label;
        var source = scenario.IsFirstMonth ? "cautious envelope, not confirmed salary"
            : scenario.IsActualFullMonthPay ? "actual take-home from a full-month payslip" : "provisional full-month take-home";
        ScenarioBalanceText = $"Income {Money(scenario.Income)} ({source}) · known allocations {Money(scenario.Total)}" +
            (scenario.FundingGap > 0m ? $" · funding gap {Money(scenario.FundingGap)}" : " · balances to the envelope");
        EssentialsContextText = $"Known essential costs and firm commitments: {Money(scenario.KnownEssentialCosts)} " +
            $"(excludes subscription and sinking funds). Future essential-cost funds: {Money(scenario.FutureEssentialFunds)}. " +
            "Unconfirmed costs are not included as zero.";
        ScenarioTargetText = $"Emergency savings: {Money(scenario.EmergencyAllocation)}. Target {Money(scenario.EmergencyTarget)}" +
            (scenario.TargetShortfall > 0m ? $"; shortfall {Money(scenario.TargetShortfall)}." : ".") +
            $" Subscription difference: {Money(scenario.SubscriptionSaving)}" +
            (SelectedChoice.Subscription == SubscriptionScenario.Proposed ? " (expected only)." : ".");
        if (scenario.IsFirstMonth)
            ScenarioTargetText += $" Core monthly savings target {Money(_profile.Current.EmergencySavingsTarget)}; " +
                $"first-month gap to that core target {Money(Math.Max(0m, _profile.Current.EmergencySavingsTarget - scenario.EmergencyAllocation))}.";
        OnPropertyChanged(nameof(ScenarioHeading));
        OnPropertyChanged(nameof(ScenarioBalanceText));
        OnPropertyChanged(nameof(ScenarioTargetText));
        OnPropertyChanged(nameof(EssentialsContextText));
    }

    private string Summary(PlanningScenario scenario) =>
        $"{Money(scenario.Income)} planned take-home\n{Money(scenario.EmergencyAllocation)} emergency savings\n{Money(scenario.AmountFor(PlanningBucket.Buffer))} unassigned buffer";

    private string Money(decimal? amount) => amount.HasValue
        ? $"{(_profile?.CurrencyCode == "MYR" ? "RM" : _profile?.CurrencyCode ?? "")} {amount.Value:N2}" : "Unconfirmed";
    private static double Value(decimal? amount) => amount.HasValue ? (double)amount.Value : double.NaN;
    private static DateTimeOffset LocalDate(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
    private static decimal? Amount(double value) => double.IsNaN(value) ? null :
        double.IsFinite(value) ? (decimal)value : throw new ArgumentException("Enter a finite amount.");
    private static decimal RequiredAmount(double value, string field) => Amount(value) ?? throw new ArgumentException($"{field} is required.");
    private void SetBusy(bool value)
    {
        _busy = value;
        ReloadCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }
}
