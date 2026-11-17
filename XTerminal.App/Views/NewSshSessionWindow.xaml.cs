using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using XTerminal.App.ViewModels;
using XTerminal.App;

namespace XTerminal.App.Views;

public partial class NewSshSessionWindow : Window
{
    public bool Saved { get; private set; }
    public bool ConnectAfterSave { get; private set; }
    public SshSessionEditorViewModel Editor => (SshSessionEditorViewModel)DataContext;

    public NewSshSessionWindow(SshSessionEditorViewModel editor)
    {
        InitializeComponent();
        DataContext = editor;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        Editor.Password = PasswordBox.Password;

    private void PassphraseBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        Editor.Passphrase = PassphraseBox.Password;

    private void JumpPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        Editor.JumpPassword = JumpPasswordBox.Password;

    private void JumpPassphraseBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        Editor.JumpPassphrase = JumpPassphraseBox.Password;

    private void BrowseKey_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Private key files|*.*;*id_rsa*;*id_ed25519*;*.pem|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            Editor.PrivateKeyPath = dlg.FileName;
        }
    }

    private void CreateFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var prompt = new InputPromptWindow("New folder", "New folder") { Owner = this };
        if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.Value)) return;
        var name = prompt.Value.Trim();
        var existing = Editor.FolderOptions.FirstOrDefault(f =>
            f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Editor.SelectedFolderOption = existing;
            return;
        }

        var option = new SshFolderOption { Id = Guid.NewGuid(), Name = name };
        Editor.FolderOptions.Add(option);
        Editor.SelectedFolderOption = option;
        // Persist folder into library immediately so Save keeps a valid FolderId.
        if (System.Windows.Application.Current?.MainWindow is MainWindow { DataContext: MainViewModel vm })
            _ = vm.Sessions.EnsureFolderWithIdAsync(option.Id!.Value, name);
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        Saved = false;
        DialogResult = false;
        Close();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e) => TrySave(connect: false);

    private void SaveAndConnect_OnClick(object sender, RoutedEventArgs e) => TrySave(connect: true);

    private void TrySave(bool connect)
    {
        if (string.IsNullOrWhiteSpace(Editor.Name) ||
            string.IsNullOrWhiteSpace(Editor.Host) ||
            string.IsNullOrWhiteSpace(Editor.Username))
        {
            MessageBox.Show(this, "Name, host, and username are required.", "X-Terminal",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Editor.Port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Port must be between 1 and 65535.", "X-Terminal",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Saved = true;
        ConnectAfterSave = connect;
        DialogResult = true;
        Close();
    }
}
