using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using MyBudget.Core;

namespace MyBudget.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class StatementReviewRow : ObservableObject
{
    private readonly StatementEntry _entry;
    private readonly IReadOnlyList<BudgetCategory> _categories;
    private bool _isSelected;
    private TransactionType _selectedType;
    private BudgetCategory? _category;
    private SavingsGoal? _goal;

    public StatementReviewRow(StatementEntry entry, BudgetSnapshot snapshot)
    {
        _entry = entry;
        _categories = snapshot.Categories.Where(category => !category.IsArchived).ToArray();
        AlreadyImported = snapshot.Transactions.Any(item => item.Id == entry.Id);
        PossibleDuplicate = StatementReviewRules.IsPossibleDuplicate(entry, snapshot.Transactions);
        Types = StatementReviewRules.AllowedTypes(entry);
        Goals = snapshot.Goals;
        _selectedType = Types[0];
        ChooseDefaultCategory();
        // All rows require an explicit selection; selecting a file never commits anything.
    }

    public bool AlreadyImported { get; }
    public bool CanSelect => !AlreadyImported;
    public bool PossibleDuplicate { get; }
    public bool IsCardPurchase => StatementReviewRules.IsCardPurchase(_entry);
    public string DateText => _entry.Date.ToString("dd MMM yyyy");
    public string AmountText => $"{(_entry.IsCredit ? "+" : "−")} RM {_entry.Amount:N2}";
    public string Description => _entry.Description;
    public string Notice => AlreadyImported ? "Already imported — left unchanged" : PossibleDuplicate
        ? "Possible duplicate of an existing entry — compare before selecting" : IsCardPurchase
            ? "Card purchase · choose a category" : "Review type · transfers between your own accounts are not spending or income";
    public IReadOnlyList<TransactionType> Types { get; }
    public IReadOnlyList<SavingsGoal> Goals { get; }
    public IEnumerable<BudgetCategory> Categories => _categories.Where(category => TransactionCategoryRules.IsCompatible(SelectedType, category.Kind));
    public Visibility CategoryVisibility => SelectedType == TransactionType.Transfer ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GoalVisibility => SelectedType == TransactionType.Savings ? Visibility.Visible : Visibility.Collapsed;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value && CanSelect); }
    public TransactionType SelectedType
    {
        get => _selectedType;
        set
        {
            if (!Types.Contains(value) || !SetProperty(ref _selectedType, value)) return;
            SelectedGoal = null;
            OnPropertyChanged(nameof(Categories));
            ChooseDefaultCategory();
            OnPropertyChanged(nameof(CategoryVisibility));
            OnPropertyChanged(nameof(GoalVisibility));
        }
    }
    public BudgetCategory? SelectedCategory { get => _category; set => SetProperty(ref _category, value); }
    public SavingsGoal? SelectedGoal { get => _goal; set => SetProperty(ref _goal, value); }

    private void ChooseDefaultCategory() => SelectedCategory = SelectedType == TransactionType.Transfer ? null :
        Categories.FirstOrDefault(category => category.Name is "Other" or "Other income" or "Savings") ?? Categories.FirstOrDefault();

    public BudgetTransaction ToTransaction()
    {
        if (!Types.Contains(SelectedType) || (SelectedType != TransactionType.Transfer &&
            (SelectedCategory is null || !TransactionCategoryRules.IsCompatible(SelectedType, SelectedCategory.Kind))))
            throw new InvalidOperationException("Choose a valid type and category for every selected row.");
        return new BudgetTransaction(_entry.Id, _entry.Date, SelectedType, _entry.Amount,
            SelectedType == TransactionType.Transfer ? null : SelectedCategory?.Id,
            _entry.Description, SelectedType == TransactionType.Savings ? SelectedGoal?.Id : null);
    }
}
