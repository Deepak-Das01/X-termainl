using XTerminal.Core.Models.Serial;

namespace XTerminal.Core.Services;

public interface ISerialPortEnumerator
{
    /// <summary>
    /// Lists COM ports known to the OS without opening them.
    /// Friendly names are best-effort and may be null.
    /// </summary>
    IReadOnlyList<SerialPortInfo> ListPorts();
}
