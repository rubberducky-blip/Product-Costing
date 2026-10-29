using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;
using ProductCosting.Core.Pdf;

namespace ProductCosting.App.ViewModels;

public sealed class NotificationItem(PriceNotification n)
{
    public PriceNotification Model { get; } = n;
    public long Id => Model.Id;
    public string Code => Model.Code;
    public string Name => Model.Name;
    public string KindText => Strings.Kind(Model.Kind);
    public bool IsIncrease => Model.IsIncrease;
    public string Glyph => IsIncrease ? "\uE74A" : "\uE74B";
    public string DirectionText => Loc.T(IsIncrease ? "notify.up" : "notify.down");
    public string OldText => Fmt.Money(Model.OldPrice);
    public string NewText => Fmt.Money(Model.NewPrice);
    public string ChangeText => Fmt.Signed(Model.ChangePct);
    public string ReasonsText => Strings.Reasons(Model.Reasons);
    public string TimeText => DateTime.TryParse(Model.UpdatedAt, out var d) ? d.ToString("g", Loc.Culture) : "";
    public bool Adjusted => Model.Adjusted;
    public bool CanAdjust => !Model.Adjusted && Model.Cost > 0;
    public string AdjustTip => Loc.F("notify.adjustHelp", Fmt.Money(Model.OldPrice));
    public string AdjustedText => Loc.F("notify.adjusted", Fmt.Money(Model.CurrentPrice));
}

public sealed partial class NotificationsViewModel(MainViewModel main) : ObservableObject
{
    public ObservableCollection<NotificationItem> Items { get; } = [];

    [ObservableProperty] private int _count;
    [ObservableProperty] private int _expansionCount;
    [ObservableProperty] private bool _isOpen;

    public bool CanExpand => ExpansionCount > 0;
    public bool HasItems => Count > 0;

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(HasItems));

    partial void OnExpansionCountChanged(int value) => OnPropertyChanged(nameof(CanExpand));

    public void Refresh()
    {
        var list = AppState.Store.GetNotifications();
        Items.Clear();
        foreach (var n in list) Items.Add(new NotificationItem(n));
        Count = list.Count;
        ExpansionCount = list.Count(n => n.NewPrice < n.OldPrice && !n.Adjusted);
    }

    [RelayCommand]
    void Toggle()
    {
        if (!IsOpen) Refresh();
        IsOpen = !IsOpen;
    }

    [RelayCommand]
    void Hide(NotificationItem item) => AppState.Store.HideNotification(item.Id);

    [RelayCommand]
    void HideAll()
    {
        IsOpen = false;
        if (Count > 0) AppState.Store.HideAllNotifications();
    }

    [RelayCommand]
    void Adjust(NotificationItem item) => AppState.Store.AdjustMargin(item.Id);

    [RelayCommand]
    void Expansion()
    {
        if (ExpansionCount == 0) return;
        IsOpen = false;
        if (!Dialogs.Confirm(Loc.F("notify.expansionConfirm", ExpansionCount), Loc.T("notify.expansion"))) return;
        var n = AppState.Store.MarginExpansion();
        main.ShowToast(Loc.F("notify.expanded", n));
    }

    [RelayCommand]
    void Open(NotificationItem item)
    {
        IsOpen = false;
        main.OpenItem(item.Model.ItemId);
    }

    [RelayCommand]
    void Export()
    {
        Refresh();
        if (Count == 0) return;
        IsOpen = false;
        var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{Loc.T("notify.pdfTitle")} {DateTime.Now:yyyy-MM-dd}.pdf",
        };
        if (dlg.ShowDialog() != true) return;
        var bytes = PdfReports.PriceChanges(Items.Select(i => i.Model).ToList(), AppState.Settings.CurrencySymbol, DateTime.Now);
        File.WriteAllBytes(dlg.FileName, bytes);
        main.ShowToast(Loc.T("pdf.saved"));
        OpenFile(dlg.FileName);
    }

    internal static void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { AppState.LogError(ex); }
    }
}
