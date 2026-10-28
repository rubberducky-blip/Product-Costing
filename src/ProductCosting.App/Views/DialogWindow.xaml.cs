using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProductCosting.App.Infrastructure;

namespace ProductCosting.App.Views;

public enum DialogKind { Info, Question, Warning, Danger, Error, Success }

public partial class DialogWindow : Window
{
    public string? Result { get; private set; }
    public string InputText => InputBox.Text;

    public DialogWindow(string title, string message, DialogKind kind, IReadOnlyList<(string Id, string Text, string Style)> buttons, string? input = null)
    {
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        (IconText.Text, var fg, var bg) = kind switch
        {
            DialogKind.Warning => ("\uE7BA", "WarningTextBrush", "WarningSoftBrush"),
            DialogKind.Danger => ("\uE74D", "DangerTextBrush", "DangerSoftBrush"),
            DialogKind.Error => ("\uEA39", "DangerTextBrush", "DangerSoftBrush"),
            DialogKind.Success => ("\uE73E", "SuccessTextBrush", "SuccessSoftBrush"),
            DialogKind.Question => ("\uE897", "AccentTextBrush", "AccentSoftBrush"),
            _ => ("\uE946", "AccentTextBrush", "AccentSoftBrush"),
        };
        IconText.Foreground = (Brush)FindResource(fg);
        IconBox.Background = (Brush)FindResource(bg);

        if (input is not null)
        {
            InputBox.Visibility = Visibility.Visible;
            InputBox.Text = input;
            Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
        }

        foreach (var (id, text, style) in buttons)
        {
            var b = new Button { Content = text, Style = (Style)FindResource(style), Margin = new Thickness(8, 0, 0, 0), MinWidth = 90 };
            if (style is "PrimaryButton" or "DangerSolidButton")
            {
                b.IsDefault = true;
                if (input is null) Loaded += (_, _) => b.Focus();
            }
            b.Click += (_, _) => { Result = id; DialogResult = true; };
            ButtonsPanel.Children.Add(b);
        }
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Result = null; DialogResult = false; }
        };
    }
}

/// <summary>Dark, localized replacements for MessageBox.</summary>
public static class Dialogs
{
    static Window? Owner => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    static string? Show(string title, string message, DialogKind kind, params (string, string, string)[] buttons)
    {
        var d = new DialogWindow(title, message, kind, buttons);
        if (Owner is { IsLoaded: true } o && o != d) d.Owner = o;
        else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        d.ShowDialog();
        return d.Result;
    }

    public static void Info(string message, string? title = null) =>
        Show(title ?? Loc.T("app.title"), message, DialogKind.Info, ("ok", Loc.T("common.ok"), "PrimaryButton"));

    public static void Success(string message) =>
        Show(Loc.T("common.done"), message, DialogKind.Success, ("ok", Loc.T("common.ok"), "PrimaryButton"));

    public static void Error(string message) =>
        Show(Loc.T("common.error"), message, DialogKind.Error, ("ok", Loc.T("common.ok"), "PrimaryButton"));

    public static bool Confirm(string message, string? okText = null, bool danger = false, string? title = null) =>
        Show(title ?? Loc.T("common.confirm"), message, danger ? DialogKind.Danger : DialogKind.Question,
            ("cancel", Loc.T("common.cancel"), "SecondaryButton"),
            ("ok", okText ?? Loc.T("common.ok"), danger ? "DangerSolidButton" : "PrimaryButton")) == "ok";

    public static bool ConfirmDelete(string name, string? extra = null) =>
        Confirm(Loc.F("common.confirmDelete", name) + (extra is null ? "" : "\n\n" + extra), Loc.T("common.delete"), danger: true);

    public enum LeaveChoice { Stay, Discard, Save }

    public static LeaveChoice Unsaved() => Show(Loc.T("leave.title"), Loc.T("leave.body"), DialogKind.Warning,
            ("stay", Loc.T("leave.stay"), "GhostButton"),
            ("discard", Loc.T("leave.discard"), "DangerButton"),
            ("save", Loc.T("leave.save"), "PrimaryButton")) switch
    {
        "discard" => LeaveChoice.Discard,
        "save" => LeaveChoice.Save,
        _ => LeaveChoice.Stay,
    };

    public static string? Prompt(string title, string initial = "")
    {
        var d = new DialogWindow(title, "", DialogKind.Question,
            [("cancel", Loc.T("common.cancel"), "SecondaryButton"), ("ok", Loc.T("common.ok"), "PrimaryButton")], initial);
        if (Owner is { IsLoaded: true } o) d.Owner = o;
        d.ShowDialog();
        return d.Result == "ok" && !string.IsNullOrWhiteSpace(d.InputText) ? d.InputText.Trim() : null;
    }
}
