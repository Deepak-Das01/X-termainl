using XTerminal.App.ViewModels;
using XTerminal.Core.Models;
using XTerminal.Core.Models.History;
using XTerminal.Core.Services;

namespace XTerminal.App.Services;

/// <summary>
/// Conservatively captures submitted commands from terminal input (Enter), not raw keystrokes.
/// Private Input Mode suppresses history via <see cref="CommandLineCaptureBuffer.Suppress"/>.
/// Does not claim shell-integration exit codes.
/// </summary>
public sealed class CommandHistoryTracker
{
    private readonly Dictionary<Guid, CommandLineCaptureBuffer> _buffers = new();
    private readonly HistoryViewModel _history;

    public CommandHistoryTracker(HistoryViewModel history) => _history = history;

    public void OnTerminalInput(TerminalTabViewModel tab, string data)
    {
        if (string.IsNullOrEmpty(data)) return;
        var buffer = GetBuffer(tab.Id);
        buffer.Suppress = tab.PrivateInputMode;
        foreach (var command in buffer.Process(data))
        {
            var isSsh = tab.Definition.Kind == SessionKind.Ssh;
            _ = _history.RecordAsync(new HistoryEntry
            {
                Command = command,
                SessionName = tab.Title,
                ShellType = isSsh ? "SSH" : tab.Definition.Shell.ToString(),
                WorkingDirectory = tab.Definition.WorkingDirectory,
                Hostname = tab.RemoteHost,
                IsSsh = isSsh,
                CaptureSource = isSsh ? "SshInferred" : "ConPtyInferred"
            });
        }
    }

    public void ForgetTab(Guid tabId) => _buffers.Remove(tabId);

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
