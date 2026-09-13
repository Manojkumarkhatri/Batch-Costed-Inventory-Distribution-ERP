using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Domain.Entities;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

public partial class PartiesViewModel : ObservableObject
{
    private readonly PartyService _parties;

    [ObservableProperty] private string? searchTerm;
    [ObservableProperty] private string? groupFilter;
    [ObservableProperty] private bool showInactive;
    [ObservableProperty] private string statusMessage = "";

    [ObservableProperty] private PartyRow? selectedRow;

    // Detail pane. Read from the database when a row is picked, so the panel
    // never shows a stale copy of something changed in the dialog.
    [ObservableProperty] private Party? selectedParty;
    [ObservableProperty] private decimal selectedBalance;

    public ObservableCollection<PartyRow> Parties { get; } = new();
    public ObservableCollection<string> Groups { get; } = new();
    public ObservableCollection<PartyTxnRow> Ledger { get; } = new();

    /// <summary>
    /// Set by the view. Opens the Add/Edit dialog on the party passed in and
    /// returns true if the user pressed Save. The dialog edits the object
    /// directly, so a rejected save reopens with the typed values intact.
    /// </summary>
    public Func<Party, IReadOnlyList<string>, bool>? ShowEditor { get; set; }

    public PartiesViewModel(PartyService parties)
    {
        _parties = parties;
        Load();
    }

    public bool HasSelection => SelectedParty is not null;

    /// <summary>"To Receive" / "To Pay" wording, as on his screen.</summary>
    public string BalanceCaption => SelectedBalance switch
    {
        > 0 => "TO RECEIVE",
        < 0 => "TO PAY",
        _ => "SETTLED"
    };

    partial void OnSearchTermChanged(string? value) => Load();
    partial void OnGroupFilterChanged(string? value) => Load();
    partial void OnShowInactiveChanged(bool value) => Load();

    partial void OnSelectedRowChanged(PartyRow? value)
    {
        if (value is null) { ClearDetail(); return; }
        ShowDetail(value.Id);
    }

    partial void OnSelectedBalanceChanged(decimal value) =>
        OnPropertyChanged(nameof(BalanceCaption));

    private void ClearDetail()
    {
        SelectedParty = null;
        SelectedBalance = 0m;
        Ledger.Clear();
        OnPropertyChanged(nameof(HasSelection));
    }

    private void ShowDetail(int partyId)
    {
        SelectedParty = _parties.GetById(partyId);
        if (SelectedParty is null) { ClearDetail(); return; }

        Ledger.Clear();
        foreach (var row in _parties.Ledger(partyId)) Ledger.Add(row);

        // The closing line of the statement IS the balance. Taking it from the
        // ledger rather than recomputing means the header can never disagree
        // with the rows underneath it.
        SelectedBalance = Ledger.Count > 0
            ? Ledger[^1].Balance
            : SelectedParty.SignedOpeningBalance;

        OnPropertyChanged(nameof(HasSelection));
    }

    public void Load()
    {
        var keepId = SelectedRow?.Id ?? SelectedParty?.Id ?? 0;

        var rows = _parties.Search(SearchTerm, GroupFilter, ShowInactive);
        Parties.Clear();
        foreach (var r in rows) Parties.Add(r);

        Groups.Clear();
        foreach (var g in _parties.Groups()) Groups.Add(g);

        SelectedRow = keepId > 0 ? Parties.FirstOrDefault(p => p.Id == keepId) : null;
        if (SelectedRow is null) ClearDetail();

        var toReceive = rows.Where(r => r.Balance > 0).Sum(r => r.Balance);
        var toPay = rows.Where(r => r.Balance < 0).Sum(r => -r.Balance);
        StatusMessage =
            $"{rows.Count} part{(rows.Count == 1 ? "y" : "ies")}   |   " +
            $"To receive {toReceive:N0}   |   To pay {toPay:N0}";
    }

    [RelayCommand]
    private void New() => Persist(new Party
    {
        OpeningBalanceDate = DateTime.Today,
        OpeningIsReceivable = true,
        IsActive = true
    });

    [RelayCommand]
    private void Edit()
    {
        if (SelectedParty is not { Id: > 0 } current) return;

        // A fresh copy from disk, so a cancelled dialog leaves nothing behind
        // and the form never edits the object the list is displaying.
        var draft = _parties.GetById(current.Id);
        if (draft is not null) Persist(draft);
    }

    /// <summary>
    /// Opens the editor, saves, and reopens it on failure with the user's
    /// typing still in place rather than throwing the whole form away.
    /// </summary>
    private void Persist(Party draft)
    {
        if (ShowEditor is null) return;

        while (true)
        {
            if (!ShowEditor(draft, Groups.ToList())) return;   // cancelled

            var outcome = _parties.Save(draft);

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
            SelectedRow = Parties.FirstOrDefault(p => p.Id == outcome.Id);
            StatusMessage = $"Saved {draft.Name}.";
            return;
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedParty is not { Id: > 0 } party) return;

        var confirm = MessageBox.Show($"Remove {party.Name}?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var outcome = _parties.Remove(party.Id);
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

    [RelayCommand]
    private void ClearFilters()
    {
        SearchTerm = null;
        GroupFilter = null;
        ShowInactive = false;
    }
}
