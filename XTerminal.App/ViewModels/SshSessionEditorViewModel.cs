using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;
using XTerminal.Core.Settings;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.ViewModels;

public partial class SshSessionEditorViewModel : ObservableObject
{
    private readonly ISecretStore _secrets;
    private readonly SshHostKeyPolicy _hostKeys;

    [ObservableProperty] private string _selectedTab = "General";
    [ObservableProperty] private string _name = "New SSH session";
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private int _port = 22;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private SshAuthMethod _authMethod = SshAuthMethod.Password;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _privateKeyPath = string.Empty;
    [ObservableProperty] private string _passphrase = string.Empty;
    [ObservableProperty] private string _startupDirectory = string.Empty;
    [ObservableProperty] private string _startupCommand = string.Empty;
    [ObservableProperty] private string _environmentText = string.Empty;
    [ObservableProperty] private int _connectionTimeoutSeconds = 30;
    [ObservableProperty] private int _keepAliveIntervalSeconds = 30;
    [ObservableProperty] private bool _compression;
    [ObservableProperty] private string _terminalType = "xterm-256color";
    [ObservableProperty] private string _proxyJumpHost = string.Empty;
    [ObservableProperty] private int _proxyJumpPort = 22;
    [ObservableProperty] private string _proxyJumpUsername = string.Empty;
    [ObservableProperty] private SshAuthMethod _proxyJumpAuthMethod = SshAuthMethod.Password;
    [ObservableProperty] private string _proxyJumpPrivateKeyPath = string.Empty;
    [ObservableProperty] private string _jumpPassword = string.Empty;
    [ObservableProperty] private string _jumpPassphrase = string.Empty;
    [ObservableProperty] private bool _strictHostKeyChecking = true;
    [ObservableProperty] private bool _autoReconnect = true;
    [ObservableProperty] private int _maxReconnectAttempts = 3;
    [ObservableProperty] private string _tagsText = string.Empty;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private string _testResult = string.Empty;
    [ObservableProperty] private bool _isTesting;

    public Guid SessionId { get; private set; } = Guid.NewGuid();
    public Guid? FolderId { get; set; }
    public ObservableCollection<SshPortForwardRecord> PortForwards { get; } = new();
    public ObservableCollection<SshFolderOption> FolderOptions { get; } = new();
    public IReadOnlyList<SshAuthMethod> AuthMethods { get; } = Enum.GetValues<SshAuthMethod>();
    public IReadOnlyList<string> EditorTabs { get; } = ["General", "Authentication", "Advanced", "Port Forwarding", "Terminal"];

    [ObservableProperty] private SshFolderOption? _selectedFolderOption;

    public void SetFolderOptions(IReadOnlyList<SshFolderOption> folders)
    {
        FolderOptions.Clear();
        foreach (var f in folders) FolderOptions.Add(f);
        SelectedFolderOption = FolderOptions.FirstOrDefault(f => f.Id == FolderId)
                               ?? FolderOptions.FirstOrDefault();
    }

    partial void OnSelectedFolderOptionChanged(SshFolderOption? value) => FolderId = value?.Id;

    public SshSessionEditorViewModel(
        ISecretStore secrets,
        SshHostKeyPolicy hostKeys,
        SshSessionRecord? existing = null)
    {
        _secrets = secrets;
        _hostKeys = hostKeys;

        if (existing is not null)
        {
            Load(existing);
        }
        else
        {
            PortForwards.Add(new SshPortForwardRecord
            {
                Type = SshForwardType.Local,
                ListenPort = 8080,
                DestinationHost = "127.0.0.1",
                DestinationPort = 80,
                Enabled = false
            });
        }
    }

    /// <summary>Apply Settings → SSH defaults when creating a new session.</summary>
    public void ApplyAppSshDefaults(SshAppSettings ssh)
    {
        Port = ssh.DefaultPort is >= 1 and <= 65535 ? ssh.DefaultPort : 22;
        if (!string.IsNullOrWhiteSpace(ssh.DefaultUsername))
            Username = ssh.DefaultUsername;
        ConnectionTimeoutSeconds = ssh.ConnectionTimeoutSeconds;
        KeepAliveIntervalSeconds = ssh.KeepAliveIntervalSeconds;
        AutoReconnect = ssh.AutoReconnect;
        MaxReconnectAttempts = ssh.MaxReconnectAttempts;
        StrictHostKeyChecking = ssh.StrictHostKeyChecking;
    }

    public void Load(SshSessionRecord record)
    {
        SessionId = record.Id;
        FolderId = record.FolderId;
        Name = record.Name;
        Host = record.Host;
        Port = record.Port;
        Username = record.Username;
        AuthMethod = record.AuthMethod;
        PrivateKeyPath = record.PrivateKeyPath ?? string.Empty;
        StartupDirectory = record.StartupDirectory ?? string.Empty;
        StartupCommand = record.StartupCommand ?? string.Empty;
        EnvironmentText = string.Join(Environment.NewLine,
            record.EnvironmentVariables.Select(kv => $"{kv.Key}={kv.Value}"));
        ConnectionTimeoutSeconds = record.ConnectionTimeoutSeconds;
        KeepAliveIntervalSeconds = record.KeepAliveIntervalSeconds;
        Compression = record.Compression;
        TerminalType = record.TerminalType;
        ProxyJumpHost = record.ProxyJumpHost ?? string.Empty;
        ProxyJumpPort = record.ProxyJumpPort;
        ProxyJumpUsername = record.ProxyJumpUsername ?? string.Empty;
        ProxyJumpAuthMethod = record.ProxyJumpAuthMethod;
        ProxyJumpPrivateKeyPath = record.ProxyJumpPrivateKeyPath ?? string.Empty;
        StrictHostKeyChecking = record.StrictHostKeyChecking;
        AutoReconnect = record.AutoReconnect;
        MaxReconnectAttempts = record.MaxReconnectAttempts;
        TagsText = string.Join(", ", record.Tags);
        IsFavorite = record.IsFavorite;
        PortForwards.Clear();
        foreach (var f in record.PortForwards) PortForwards.Add(CloneForward(f));
        // Secrets are never loaded into the editor for display; blank means "keep existing".
        Password = string.Empty;
        Passphrase = string.Empty;
        JumpPassword = string.Empty;
        JumpPassphrase = string.Empty;
    }

    public SshSessionRecord ToRecord()
    {
        return new SshSessionRecord
        {
            Id = SessionId,
            Name = Name.Trim(),
            Host = Host.Trim(),
            Port = Port <= 0 ? 22 : Port,
            Username = Username.Trim(),
            AuthMethod = AuthMethod,
            PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath.Trim(),
            StartupDirectory = NullIfEmpty(StartupDirectory),
            StartupCommand = NullIfEmpty(StartupCommand),
            EnvironmentVariables = ParseEnv(EnvironmentText),
            ConnectionTimeoutSeconds = Math.Clamp(ConnectionTimeoutSeconds, 5, 300),
            KeepAliveIntervalSeconds = Math.Clamp(KeepAliveIntervalSeconds, 0, 600),
            Compression = Compression,
            TerminalType = string.IsNullOrWhiteSpace(TerminalType) ? "xterm-256color" : TerminalType.Trim(),
            ProxyJumpHost = NullIfEmpty(ProxyJumpHost),
            ProxyJumpPort = ProxyJumpPort <= 0 ? 22 : ProxyJumpPort,
            ProxyJumpUsername = NullIfEmpty(ProxyJumpUsername),
            ProxyJumpAuthMethod = ProxyJumpAuthMethod,
            ProxyJumpPrivateKeyPath = NullIfEmpty(ProxyJumpPrivateKeyPath),
            StrictHostKeyChecking = StrictHostKeyChecking,
            AutoReconnect = AutoReconnect,
            MaxReconnectAttempts = Math.Clamp(MaxReconnectAttempts, 0, 20),
            PortForwards = PortForwards.Select(CloneForward).ToList(),
            FolderId = FolderId,
            IsFavorite = IsFavorite,
            Tags = TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ModifiedUtc = DateTimeOffset.UtcNow,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task PersistSecretsAsync(bool clearFromMemory = true, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(Password))
        {
            await _secrets.SaveSecretAsync(SshSecretKeys.Password(SessionId), Password, cancellationToken);
        }

        if (!string.IsNullOrEmpty(Passphrase))
        {
            await _secrets.SaveSecretAsync(SshSecretKeys.Passphrase(SessionId), Passphrase, cancellationToken);
        }

        if (!string.IsNullOrEmpty(JumpPassword))
        {
            await _secrets.SaveSecretAsync(SshSecretKeys.JumpPassword(SessionId), JumpPassword, cancellationToken);
        }

        if (!string.IsNullOrEmpty(JumpPassphrase))
        {
            await _secrets.SaveSecretAsync(SshSecretKeys.JumpPassphrase(SessionId), JumpPassphrase, cancellationToken);
        }

        if (!clearFromMemory)
        {
            return;
        }

        // Clear from memory after final save (never log these fields).
        Password = string.Empty;
        Passphrase = string.Empty;
        JumpPassword = string.Empty;
        JumpPassphrase = string.Empty;
    }

    [RelayCommand]
    private void AddPortForward()
    {
        PortForwards.Add(new SshPortForwardRecord
        {
            Type = SshForwardType.Local,
            ListenPort = 18080 + PortForwards.Count,
            DestinationHost = "127.0.0.1",
            DestinationPort = 80,
            Enabled = true
        });
    }

    [RelayCommand]
    private void RemovePortForward(SshPortForwardRecord? forward)
    {
        if (forward is not null) PortForwards.Remove(forward);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        TestResult = "Testing connection…";
        try
        {
            // Persist secrets for the test without clearing the editor fields.
            await PersistSecretsAsync(clearFromMemory: false);
            var tester = new SshConnectionTester(_secrets, _hostKeys);
            var (ok, message) = await tester.TestAsync(ToRecord());
            TestResult = ok ? $"✔ {message}" : $"✖ {message}";
        }
        catch (Exception ex)
        {
            TestResult = $"✖ {ex.Message.Replace("password", "********", StringComparison.OrdinalIgnoreCase)}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private static Dictionary<string, string> ParseEnv(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (!string.IsNullOrEmpty(key)) map[key] = value;
        }

        return map;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SshPortForwardRecord CloneForward(SshPortForwardRecord f) => new()
    {
        Id = f.Id,
        Enabled = f.Enabled,
        Type = f.Type,
        BindAddress = f.BindAddress,
        ListenPort = f.ListenPort,
        DestinationHost = f.DestinationHost,
        DestinationPort = f.DestinationPort,
        DisplayName = f.DisplayName
    };
}
