using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

public partial class BanksViewModel : ObservableObject
{
    private readonly BankService _banks;

    [ObservableProperty] private string? searchTerm;
    [ObservableProperty] private bool showInactive;
    [ObservableProperty] private string statusMessage = "";

    [ObservableProperty] private BankAccountRow? selectedRow;
    [ObservableProperty] private BankAccount? selectedAccount;
    [ObservableProperty] private decimal selectedBalance;

    public ObservableCollection<BankAccountRow> Accounts { get; } = new();
    public ObservableCollection<BankTxnRow> Statement { get; } = new();

    /// <summary>Set by the view. Opens the add/edit dialog.</summary>
    public Func<BankAccount, bool>? ShowEditor { get; set; }

    /// <summary>
    /// Set by the view. Shows the movement dialog for one kind of transfer and
    /// returns what to record, or null if cancelled.
    /// </summary>
    public Func<BankTxnType, BankAccount, IReadOnlyList<BankAccountRow>, BankTransferInput?>? ShowTransfer { get; set; }

    public BanksViewModel(BankService banks)
    {
        _banks = banks;
        Load();
    }

    public bool HasSelection => SelectedAccount is not null;

    /// <summary>Reads "IN THIS ACCOUNT" or "OVERDRAWN", as the figure warrants.</summary>
    public string BalanceCaption => SelectedBalance < 0 ? "OVERDRAWN" : "IN THIS ACCOUNT";

    partial void OnSearchTermChanged(string? value) => Load();
    partial void OnShowInactiveChanged(bool value) => Load();
    partial void OnSelectedBalanceChanged(decimal value) =>
        OnPropertyChanged(nameof(BalanceCaption));

    partial void OnSelectedRowChanged(BankAccountRow? value)
    {
        if (value is null) { ClearDetail(); return; }
        ShowDetail(value.Id);
    }

    private void ClearDetail()
    {
        SelectedAccount = null;
        SelectedBalance = 0m;
        Statement.Clear();
        OnPropertyChanged(nameof(HasSelection));
    }

    private void ShowDetail(int accountId)
    {
        SelectedAccount = _banks.GetById(accountId);
        if (SelectedAccount is null) { ClearDetail(); return; }

        Statement.Clear();
        foreach (var r in _banks.Statement(accountId)) Statement.Add(r);

        // The closing line of the statement IS the balance, so the header can
        // never disagree with the rows beneath it.
        SelectedBalance = Statement.Count > 0
            ? Statement[^1].Balance
            : SelectedAccount.OpeningBalance;

        OnPropertyChanged(nameof(HasSelection));
    }

    public void Load()
    {
        var keepId = SelectedRow?.Id ?? SelectedAccount?.Id ?? 0;

        var rows = _banks.Accounts(ShowInactive).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var t = SearchTerm.Trim();
            rows = rows.Where(r =>
                r.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                (r.BankName ?? "").Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows.ToList();

        Accounts.Clear();
        foreach (var r in list) Accounts.Add(r);

        SelectedRow = keepId > 0 ? Accounts.FirstOrDefault(a => a.Id == keepId) : null;
        if (SelectedRow is null) ClearDetail();

        var total = Money.Round(list.Sum(a => a.Balance));
        StatusMessage = $"{list.Count} account{(list.Count == 1 ? "" : "s")}   |   " +
                        $"Total {total:N0}";
    }

    [RelayCommand]
    private void New() => Persist(new BankAccount { OpeningBalanceDate = DateTime.Today });

    [RelayCommand]
    private void Edit()
    {
        if (SelectedAccount is not { Id: > 0 } current) return;

        // A fresh copy from disk, so a cancelled dialog leaves nothing behind.
        var draft = _banks.GetById(current.Id);
        if (draft is not null) Persist(draft);
    }

    private void Persist(BankAccount draft)
    {
        if (ShowEditor is null) return;

        while (true)
        {
            if (!ShowEditor(draft)) return;

            var outcome = _banks.Save(draft);

            if (!outcome.Success)
            {
                MessageBox.Show(outcome.ErrorText, "Cannot save",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(outcome.WarningText))
                MessageBox.Show(outcome.WarningText, "Saved - please check",
                    MessageBoxButton.OK, MessageBoxImage.Information);

            Load();
            SelectedRow = Accounts.FirstOrDefault(a => a.Id == outcome.Id);
            StatusMessage = $"Saved {draft.Name}.";
            return;
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedAccount is not { Id: > 0 } account) return;

        var confirm = MessageBox.Show($"Remove {account.Name}?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var outcome = _banks.Remove(account.Id);

        if (!outcome.Success)
        {
            MessageBox.Show(outcome.ErrorText, "Cannot remove",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(outcome.WarningText))
            MessageBox.Show(outcome.WarningText, "Done",
                MessageBoxButton.OK, MessageBoxImage.Information);

        SelectedRow = null;
        Load();
    }

    // ------------------------------------------------------- movements

    [RelayCommand] private void Deposit() => Move(BankTxnType.Deposit);
    [RelayCommand] private void Withdraw() => Move(BankTxnType.Withdraw);
    [RelayCommand] private void BankToCash() => Move(BankTxnType.BankToCash);
    [RelayCommand] private void CashToBank() => Move(BankTxnType.CashToBank);
    [RelayCommand] private void BankToBank() => Move(BankTxnType.BankToBank);
    [RelayCommand] private void Adjust() => Move(BankTxnType.Adjustment);

    private void Move(BankTxnType type)
    {
        if (SelectedAccount is not { } account) return;
        if (ShowTransfer is null) return;

        if (type == BankTxnType.BankToBank && Accounts.Count < 2)
        {
            MessageBox.Show("A transfer needs a second account to move the money into.",
                "Only one account", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = ShowTransfer(type, account, Accounts.ToList());
        if (input is null) return;

        var outcome = _banks.Record(input);

        if (!outcome.Success)
        {
            MessageBox.Show(outcome.ErrorText, "Cannot record",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Load();
        StatusMessage = $"Recorded {input.Amount:N0} on {account.Name}.";
    }

    [RelayCommand]
    private void DeleteTransaction(BankTxnRow? row)
    {
        if (row is null || row.Id == 0) return;

        var confirm = MessageBox.Show(
            $"Delete the {Math.Abs(row.Amount):N0} {row.TypeLabel.ToLowerInvariant()} " +
            $"dated {row.Date:dd-MMM-yyyy}?\n\n" +
            "If it is a transfer, both halves go.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _banks.DeleteTransaction(row.Id);
        Load();
        StatusMessage = "Movement deleted.";
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchTerm = null;
        ShowInactive = false;
    }
}
