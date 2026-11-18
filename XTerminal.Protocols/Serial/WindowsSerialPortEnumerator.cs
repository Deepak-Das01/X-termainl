using System.IO.Ports;
using XTerminal.Core.Models.Serial;
using XTerminal.Core.Services;

namespace XTerminal.Protocols.Serial;

/// <summary>
/// Discovers COM ports via <see cref="SerialPort.GetPortNames"/> without opening them.
/// Does not fabricate device metadata; friendly names are omitted unless provided by a richer enumerator.
/// </summary>
public sealed class WindowsSerialPortEnumerator : ISerialPortEnumerator
{
    public IReadOnlyList<SerialPortInfo> ListPorts()
    {
        string[] names;
        try
        {
            names = SerialPort.GetPortNames();
        }
        catch
        {
            return Array.Empty<SerialPortInfo>();
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new SerialPortInfo
            {
                PortName = n,
                FriendlyName = null,
                IsAvailable = true // presence ≠ free; open attempt determines exclusivity
            })
            .ToList();
    }
}
