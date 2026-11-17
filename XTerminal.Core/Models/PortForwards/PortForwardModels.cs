using XTerminal.Core.Models.Ssh;

namespace XTerminal.Core.Models.PortForwards;

public sealed class PortForwardDocument
{
    public int Version { get; set; } = 1;
    public List<PortForwardDefinition> Forwards { get; set; } = new();
}

public sealed class PortForwardDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New forward";
    public Guid SshSessionId { get; set; }
    public SshForwardType Type { get; set; } = SshForwardType.Local;
    public string BindAddress { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; } = 8080;
    public string? DestinationHost { get; set; } = "127.0.0.1";
    public int DestinationPort { get; set; } = 80;
    public bool AutoStart { get; set; }
    public bool ReconnectWithSsh { get; set; } = true;
    public bool AllowLan { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum PortForwardRuntimeState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
    Disconnected,
    // Legacy aliases kept for UI compatibility
    Active = Running,
    Error = Failed
}
