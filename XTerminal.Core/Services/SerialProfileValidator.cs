using System.Text;
using XTerminal.Core.Models.Serial;

namespace XTerminal.Core.Services;

public static class SerialProfileValidator
{
    public static readonly int[] CommonBaudRates =
        [110, 300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200, 128000, 230400, 256000, 460800, 921600];

    public static bool TryValidate(SerialSessionProfile profile, out string error)
    {
        error = "";
        if (profile is null)
        {
            error = "Serial profile is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(profile.PortName))
        {
            error = "COM port is required.";
            return false;
        }

        var port = profile.PortName.Trim();
        if (!port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(port.AsSpan(3), out var n) || n is < 1 or > 256)
        {
            error = "COM port must look like COM1…COM256.";
            return false;
        }

        if (profile.BaudRate is < 110 or > 4_000_000)
        {
            error = "Baud rate must be between 110 and 4000000.";
            return false;
        }

        if (profile.DataBits is not (5 or 6 or 7 or 8))
        {
            error = "Data bits must be 5, 6, 7, or 8.";
            return false;
        }

        if (!Enum.IsDefined(typeof(SerialParityMode), profile.Parity))
        {
            error = "Parity mode is invalid.";
            return false;
        }

        if (!Enum.IsDefined(typeof(SerialStopBitsMode), profile.StopBits))
        {
            error = "Stop bits setting is invalid.";
            return false;
        }

        if (!Enum.IsDefined(typeof(SerialFlowControl), profile.FlowControl))
        {
            error = "Flow control setting is invalid.";
            return false;
        }

        if (profile.ReadTimeoutMs is < 0 or > 120_000 || profile.WriteTimeoutMs is < 0 or > 120_000)
        {
            error = "Read/write timeouts must be between 0 and 120000 ms.";
            return false;
        }

        try
        {
            _ = Encoding.GetEncoding(string.IsNullOrWhiteSpace(profile.EncodingName) ? "utf-8" : profile.EncodingName);
        }
        catch
        {
            error = "Text encoding is not recognized.";
            return false;
        }

        if (!Enum.IsDefined(typeof(SerialLineEnding), profile.LineEnding))
        {
            error = "Line-ending preference is invalid.";
            return false;
        }

        return true;
    }
}
