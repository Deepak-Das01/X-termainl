namespace XTerminal.Core.Models.Ssh;

public sealed class SshPortForwardRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public SshForwardType Type { get; set; } = SshForwardType.Local;
    public string BindAddress { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; }
    public string? DestinationHost { get; set; }
    public int DestinationPort { get; set; }
    public string? DisplayName { get; set; }
}
