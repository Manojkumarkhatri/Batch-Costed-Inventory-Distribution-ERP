using System.IO;
using Microsoft.Win32;
using System.Windows;
using ShopApp.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.AskPassphrase = AskPassphrase;
        vm.PickFolder = PickFolder;
        vm.PickBackupFile = PickBackupFile;
        vm.ShowPasscodeSetup = ShowPasscodeSetup;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.Load();
        };
    }

    /// <summary>
    /// The same window the app opens with. Setting a passcode for the first
    /// time and changing an existing one are the same job, so they run through
    /// one implementation rather than a second copy that can drift.
    /// </summary>
    private bool ShowPasscodeSetup()
    {
        var passcodes = App.Services.GetRequiredService<PasscodeService>();
        var window = new LockWindow(passcodes, forceSetup: true)
        {
            Owner = Window.GetWindow(this)
        };
        return window.ShowDialog() == true;
    }

    private string? AskPassphrase(string heading, bool confirm)
    {
        var dialog = new PassphraseDialog(heading, confirm) { Owner = Window.GetWindow(this) };
        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
    }

    private string? PickFolder(string? current)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where backups are written",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            dialog.InitialDirectory = current;

        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;
    }

    private string? PickBackupFile(string? folder)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a backup to restore",
            Filter = "ShopApp backups (*.db.enc)|*.db.enc|All files (*.*)|*.*"
        };

        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            dialog.InitialDirectory = folder;

        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }
}
