using XTerminal.App.ViewModels;
using XTerminal.Core.Models;
using XTerminal.Core.Models.History;
using XTerminal.Core.Services;

namespace XTerminal.App.Services;

/// <summary>
/// Captures commands from Enter-based input and optional shell-integration finish events.
/// Private Input Mode suppresses all application-managed capture (including integration command text).
/// Exit codes/durations are recorded only when shell integration provides them — never invented.
/// </summary>
public sealed class CommandHistoryTracker
{
    private readonly Dictionary<Guid, CommandLineCaptureBuffer> _buffers = new();
    private readonly Dictionary<Guid, DateTimeOffset> _recentShellIntegration = new();
    private readonly HistoryViewModel _history;

    public CommandHistoryTracker(HistoryViewModel history) => _history = history;

    public void OnTerminalInput(TerminalTabViewModel tab, string data)
    {
        if (string.IsNullOrEmpty(data)) return;
        var buffer = GetBuffer(tab.Id);
        buffer.Suppress = tab.PrivateInputMode;
        foreach (var command in buffer.Process(data))
        {
            // Prefer shell-integration record when a finish event arrived recently for this pane.
            if (_recentShellIntegration.TryGetValue(tab.Id, out var at) &&
                DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(2))
                continue;

            var isSsh = tab.Definition.Kind == SessionKind.Ssh;
            _ = _history.RecordAsync(new HistoryEntry
            {
                Command = command,
                SessionName = tab.Title,
                ShellType = isSsh ? "SSH" : tab.Definition.Shell.ToString(),
                WorkingDirectory = tab.Definition.WorkingDirectory,
                Hostname = tab.RemoteHost,
                IsSsh = isSsh,
                PaneId = tab.Id,
                SessionId = tab.Definition.SshSessionId,
                CaptureSource = isSsh ? "SshInferred" : "ConPtyInferred"
            });
        }
    }

    public void OnShellIntegrationEvent(TerminalTabViewModel tab, ShellIntegrationEvent ev)
    {
        if (tab.PrivateInputMode) return;
        if (ev.Kind != ShellIntegrationEventKind.CommandFinished) return;

        var (pending, started) = tab.ConsumePendingIntegratedCommand();
        var command = pending;
        if (string.IsNullOrWhiteSpace(command)) return;

        _recentShellIntegration[tab.Id] = DateTimeOffset.UtcNow;
        var isSsh = tab.Definition.Kind == SessionKind.Ssh;
        _ = _history.RecordAsync(new HistoryEntry
        {
            Command = command,
            SessionName = tab.Title,
            ShellType = isSsh ? "SSH" : tab.Definition.Shell.ToString(),
            WorkingDirectory = tab.TrackedWorkingDirectory ?? tab.Definition.WorkingDirectory,
            Hostname = tab.RemoteHost,
            IsSsh = isSsh,
            ExitCode = ev.ExitCode,
            PaneId = tab.Id,
            SessionId = tab.Definition.SshSessionId,
            StartedUtc = started ?? ev.Utc - (ev.Duration ?? TimeSpan.Zero),
            CompletedUtc = ev.Utc,
            DurationMs = ev.Duration?.TotalMilliseconds,
            CaptureSource = "ShellIntegration"
        });
    }

    public void ForgetTab(Guid tabId)
    {
        _buffers.Remove(tabId);
        _recentShellIntegration.Remove(tabId);
    }

    private CommandLineCaptureBuffer GetBuffer(Guid tabId)
    {
        if (!_buffers.TryGetValue(tabId, out var buffer))
        {
            buffer = new CommandLineCaptureBuffer();
            _buffers[tabId] = buffer;
        }

        return buffer;
    }
}
