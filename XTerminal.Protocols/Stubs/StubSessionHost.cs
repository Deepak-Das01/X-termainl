using System.Text;
using XTerminal.Core.Models;

namespace XTerminal.Protocols.Stubs;

public sealed class StubSessionHost : ISessionHost
{
    private readonly string _feature;
    private readonly string _phaseNote;

    public StubSessionHost(SessionKind kind, string feature, string phaseNote)
    {
        Kind = kind;
        _feature = feature;
        _phaseNote = phaseNote;
    }

    public SessionKind Kind { get; }
    public string Title => _feature;
    public bool IsConnected { get; private set; }
    public bool IsImplemented => false;
    public string? StatusMessage { get; private set; }
    public ConnectionState State { get; private set; } = ConnectionState.Idle;

    public event Action<byte[]>? OutputReceived;
    public event Action? Closed;
    public event Action<ConnectionState, string?>? StateChanged;

    public Task ConnectAsync(SessionDefinition definition, CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        StatusMessage = _phaseNote;
        State = ConnectionState.Connected;
        StateChanged?.Invoke(State, StatusMessage);
        var message =
            $"\u001b[38;2;120;200;255mX-Terminal\u001b[0m\r\n" +
            $"\u001b[38;2;160;160;170m{_feature}\u001b[0m session for \u001b[1m{definition.Name}\u001b[0m\r\n\r\n" +
            $"{_phaseNote}\r\n";
        OutputReceived?.Invoke(Encoding.UTF8.GetBytes(message));
        return Task.CompletedTask;
    }

    public Task ReconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void CancelConnect() { }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length > 0 && data[0] is (byte)'\r' or (byte)'\n')
        {
            OutputReceived?.Invoke(Encoding.UTF8.GetBytes("\r\n\u001b[90mFeature pending implementation.\u001b[0m\r\n"));
        }
    }

    public void Resize(short columns, short rows) { }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        State = ConnectionState.Disconnected;
        Closed?.Invoke();
        return ValueTask.CompletedTask;
    }
}
