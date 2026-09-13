using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using ShopApp.Reports;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

public record ReportGroup(string Name, IReadOnlyList<ReportDefinition> Reports);

public partial class ReportsViewModel : ObservableObject
{
    private readonly ReportService _reports;
    private readonly PartyService _parties;
    private readonly ItemService _items;
    private readonly AppDbContext _db;

    [ObservableProperty] private ReportDefinition? selectedReport;
    [ObservableProperty] private DateTime fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private PartyRow? selectedParty;
    [ObservableProperty] private Item? selectedItem;
    [ObservableProperty] private ItemCategory? selectedCategory;
    [ObservableProperty] private ReportResult? result;
    [ObservableProperty] private string statusMessage = "Choose a report on the left.";
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<ReportGroup> Groups { get; } = new();
    public ObservableCollection<PartyRow> Parties { get; } = new();
    public ObservableCollection<Item> Items { get; } = new();

    public IReadOnlyList<ItemCategory> Categories { get; } = Enum.GetValues<ItemCategory>().ToList();

    /// <summary>Set by the view so the grid can rebuild its columns.</summary>
    public Action<ReportResult>? OnResultReady { get; set; }

    /// <summary>
    /// Held while several filter properties are being set together, so the
    /// report runs once at the end instead of once per property. Without it,
    /// switching report clears three filters and fires three reports.
    /// </summary>
    private bool _suspendAutoRun;

    public ReportsViewModel(ReportService reports, PartyService parties,
                            ItemService items, AppDbContext db)
    {
        _reports = reports;
        _parties = parties;
        _items = items;
        _db = db;

        foreach (var g in ReportService.Catalogue.GroupBy(r => r.Group))
            Groups.Add(new ReportGroup(g.Key, g.ToList()));

        LoadLookups();
    }

    // ------------------------------------ what this report asks for

    // A report declares its own filters, so the bar shows three controls for
    // a day book and none at all for All parties. One shape for twenty-six
    // reports was the thing that made every screen feel padded.
    private ReportFilters Wants => SelectedReport?.Filters ?? ReportFilters.None;

    public Visibility DateFilterVisible => Show(ReportFilters.DateRange);
    public Visibility PartyFilterVisible => Show(ReportFilters.Party);
    public Visibility ItemFilterVisible => Show(ReportFilters.Item);
    public Visibility CategoryFilterVisible => Show(ReportFilters.Category);

    /// <summary>Hide the whole strip when a report takes no filters at all.</summary>
    public Visibility FilterBarVisible =>
        Wants == ReportFilters.None ? Visibility.Collapsed : Visibility.Visible;

    private Visibility Show(ReportFilters f) =>
        Wants.HasFlag(f) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// A single line under the table for reports that want it. Profit and
    /// Loss already ends in a net figure; repeating it would be noise.
    /// </summary>
    public Visibility TotalsVisible =>
        SelectedReport?.ShowTotals == true && Result?.Totals.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

    public string TotalsLine
    {
        get
        {
            if (Result is null) return "";

            var parts = Result.Columns
                .Where(c => c.ShowTotal && Result.Totals.ContainsKey(c.Field))
                .Select(c =>
                {
                    var v = Result.Totals[c.Field];
                    var text = v == decimal.Truncate(v) ? v.ToString("N0") : v.ToString("N2");
                    return $"{c.Header}  {text}";
                });

            return string.Join("      ", parts);
        }
    }

    private void RaiseChromeChanged()
    {
        OnPropertyChanged(nameof(DateFilterVisible));
        OnPropertyChanged(nameof(PartyFilterVisible));
        OnPropertyChanged(nameof(ItemFilterVisible));
        OnPropertyChanged(nameof(CategoryFilterVisible));
        OnPropertyChanged(nameof(FilterBarVisible));
        OnPropertyChanged(nameof(TotalsVisible));
        OnPropertyChanged(nameof(TotalsLine));
    }

    // ------------------------------------------------ screen space

    /// <summary>Zero hides the report list and gives the table the full width.</summary>
    [ObservableProperty] private bool listCollapsed;

    public GridLength ListWidth => ListCollapsed ? new GridLength(0) : new GridLength(252);

    partial void OnListCollapsedChanged(bool value) => OnPropertyChanged(nameof(ListWidth));

    [RelayCommand] private void ToggleList() => ListCollapsed = !ListCollapsed;

    // ---------------------------------------------------- date preset

    /// <summary>
    /// The five quick ranges as one picker rather than five buttons. Five
    /// buttons is most of a filter row spent on something used once.
    /// </summary>
    public IReadOnlyList<string> Presets { get; } =
        new[] { "Today", "Yesterday", "This month", "Last month", "This year", "Custom" };

    [ObservableProperty] private string preset = "This month";

    partial void OnPresetChanged(string value)
    {
        switch (value)
        {
            case "Today": Today(); break;
            case "Yesterday": Yesterday(); break;
            case "This month": ThisMonth(); break;
            case "Last month": LastMonth(); break;
            case "This year": ThisYear(); break;
            // Custom leaves the dates exactly where he put them.
        }
    }

    public void LoadLookups()
    {
        var partySelection = SelectedParty?.Id;
        var itemSelection = SelectedItem?.Id;

        // Refilling the lists nulls the bound selections on the way through.
        // That is bookkeeping, not the user changing a filter.
        _suspendAutoRun = true;

        Parties.Clear();
        foreach (var p in _parties.Search(null, null, includeInactive: true)) Parties.Add(p);

        Items.Clear();
        foreach (var i in _items.Search(null, null, includeInactive: true)) Items.Add(i);

        if (partySelection is int pid)
            SelectedParty = Parties.FirstOrDefault(p => p.Id == pid);
        if (itemSelection is int iid)
            SelectedItem = Items.FirstOrDefault(i => i.Id == iid);

        _suspendAutoRun = false;
    }

    partial void OnSelectedReportChanged(ReportDefinition? value)
    {
        if (value is null) return;

        // Filters belong to the report you set them on. Carrying a party or an
        // item across to the next report silently narrows it, and there is
        // nothing on screen to say why the figures look wrong.
        _suspendAutoRun = true;
        SelectedParty = null;
        SelectedItem = null;
        SelectedCategory = null;
        _suspendAutoRun = false;

        RaiseChromeChanged();
        RunReport();
    }

    // Every filter re-runs the report as soon as it changes. There is no Run
    // button any more - a control that changes nothing until you press
    // something else reads as broken.
    // Editing a date by hand means he wants that exact span, so the preset
    // drops to Custom rather than snapping the dates back on the next change.
    partial void OnFromDateChanged(DateTime value) => DateEdited();
    partial void OnToDateChanged(DateTime value) => DateEdited();

    private void DateEdited()
    {
        if (_suspendAutoRun) return;
        if (Preset != "Custom") { _suspendAutoRun = true; Preset = "Custom"; _suspendAutoRun = false; }
        AutoRun();
    }
    partial void OnSelectedPartyChanged(PartyRow? value) => AutoRun();
    partial void OnSelectedItemChanged(Item? value) => AutoRun();
    partial void OnSelectedCategoryChanged(ItemCategory? value) => AutoRun();

    private void AutoRun()
    {
        if (_suspendAutoRun) return;
        if (SelectedReport is null) return;
        RunReport();
    }

    private ReportFilter BuildFilter() => new(
        FromDate, ToDate,
        SelectedParty?.Id, SelectedItem?.Id,
        null, SelectedCategory);

    [RelayCommand]
    private void RunReport()
    {
        if (SelectedReport is null)
        {
            StatusMessage = "Choose a report on the left.";
            return;
        }

        try
        {
            IsBusy = true;

            // The context outlives this screen, so make sure reads are fresh.
            _db.ChangeTracker.Clear();

            var report = _reports.Run(SelectedReport.Id, BuildFilter());
            Result = report;
            OnResultReady?.Invoke(report);
            RaiseChromeChanged();


            StatusMessage = report.HasRows
                ? $"{report.Rows.Count} row(s)."
                : report.Subtitle ?? "Nothing found for this period.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not run the report:\n{ex.Message}", "Report",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusMessage = "Report failed.";
        }
        finally { IsBusy = false; }
    }

    // ---- quick date ranges: he will use these far more than the pickers ----

    private void Today() => SetRange(DateTime.Today, DateTime.Today);

    private void Yesterday() =>
        SetRange(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(-1));

    private void ThisMonth() =>
        SetRange(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today);

    [RelayCommand]
    private void LastMonth()
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        SetRange(first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>Pakistan financial year: July to June.</summary>
    [RelayCommand]
    private void ThisYear()
    {
        var today = DateTime.Today;
        var startYear = today.Month >= 7 ? today.Year : today.Year - 1;
        SetRange(new DateTime(startYear, 7, 1), today);
    }

    private void SetRange(DateTime from, DateTime to)
    {
        // Two properties, one report.
        _suspendAutoRun = true;
        FromDate = from;
        ToDate = to;
        _suspendAutoRun = false;

        RunReport();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suspendAutoRun = true;
        SelectedParty = null;
        SelectedItem = null;
        SelectedCategory = null;
        _suspendAutoRun = false;

        RunReport();
    }

    [RelayCommand] private void ExportExcel() => Export(excel: true);
    [RelayCommand] private void ExportPdf() => Export(excel: false);

    private void Export(bool excel)
    {
        if (Result is null || !Result.HasRows)
        {
            MessageBox.Show("Run a report with some rows first.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ShopApp Reports");
            Directory.CreateDirectory(folder);

            var safe = new string(Result.Title
                .Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            var name = $"{safe} {FromDate:yyyy-MM-dd} to {ToDate:yyyy-MM-dd}" +
                       (excel ? ".xlsx" : ".pdf");
            var path = Path.Combine(folder, name);

            var business = _db.Settings.Single(s => s.Id == 1).BusinessName;

            if (excel) ReportExporter.ToExcel(Result, path, business);
            else ReportExporter.ToPdf(Result, path, business);

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Export",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
