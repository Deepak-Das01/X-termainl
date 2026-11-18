using System.Windows;
using System.Windows.Controls;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Serial;
using XTerminal.Core.Services;
using XTerminal.Protocols.Serial;

namespace XTerminal.App.Views;

public partial class SerialConnectWindow : Window
{
    private readonly ISerialPortEnumerator _ports;

    public SessionDefinition? Result { get; private set; }
    public bool SaveProfile => SaveBox.IsChecked == true;

    public SerialConnectWindow() : this(new WindowsSerialPortEnumerator())
    {
    }

    public SerialConnectWindow(ISerialPortEnumerator ports)
    {
        _ports = ports;
        InitializeComponent();
        PopulateCombos();
        RefreshPorts();
    }

    private void PopulateCombos()
    {
        foreach (var b in SerialProfileValidator.CommonBaudRates)
            BaudBox.Items.Add(b.ToString());
        BaudBox.Text = "115200";

        foreach (var d in new[] { "5", "6", "7", "8" })
            DataBitsBox.Items.Add(d);
        DataBitsBox.SelectedItem = "8";

        foreach (var p in Enum.GetNames<SerialParityMode>())
            ParityBox.Items.Add(p);
        ParityBox.SelectedItem = nameof(SerialParityMode.None);

        StopBitsBox.Items.Add("1");
        StopBitsBox.Items.Add("1.5");
        StopBitsBox.Items.Add("2");
        StopBitsBox.SelectedItem = "1";

        FlowBox.Items.Add(nameof(SerialFlowControl.None));
        FlowBox.Items.Add(nameof(SerialFlowControl.RtsCts));
        FlowBox.Items.Add(nameof(SerialFlowControl.XonXoff));
        FlowBox.Items.Add(nameof(SerialFlowControl.RtsCtsAndXonXoff));
        FlowBox.SelectedItem = nameof(SerialFlowControl.None);

        EncodingBox.Items.Add("utf-8");
        EncodingBox.Items.Add("ascii");
        EncodingBox.Items.Add("utf-16");
        EncodingBox.Text = "utf-8";

        LineEndingBox.Items.Add(nameof(SerialLineEnding.Lf));
        LineEndingBox.Items.Add(nameof(SerialLineEnding.Cr));
        LineEndingBox.Items.Add(nameof(SerialLineEnding.CrLf));
        LineEndingBox.SelectedItem = nameof(SerialLineEnding.Lf);
    }

    private void RefreshPorts()
    {
        var selected = PortBox.Text;
        PortBox.Items.Clear();
        foreach (var p in _ports.ListPorts())
        {
            var label = string.IsNullOrWhiteSpace(p.FriendlyName)
                ? p.PortName
                : $"{p.PortName} — {p.FriendlyName}";
            PortBox.Items.Add(new ComboBoxItem { Content = label, Tag = p.PortName });
        }

        if (!string.IsNullOrWhiteSpace(selected))
            PortBox.Text = selected;
        else if (PortBox.Items.Count > 0)
            PortBox.SelectedIndex = 0;
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e) => RefreshPorts();

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        var portName = ResolvePortName();
        if (!int.TryParse(BaudBox.Text?.Trim(), out var baud))
        {
            ErrorText.Text = "Baud rate must be a number.";
            return;
        }

        if (!int.TryParse(DataBitsBox.Text?.Trim() ?? DataBitsBox.SelectedItem?.ToString(), out var dataBits))
        {
            ErrorText.Text = "Data bits must be 5–8.";
            return;
        }

        var stopRaw = StopBitsBox.SelectedItem?.ToString() ?? "1";
        var stopBits = stopRaw switch
        {
            "1.5" => nameof(SerialStopBitsMode.OnePointFive),
            "2" => nameof(SerialStopBitsMode.Two),
            _ => nameof(SerialStopBitsMode.One)
        };

        var def = new SessionDefinition
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? $"Serial · {portName}" : NameBox.Text.Trim(),
            Kind = SessionKind.Serial,
            Group = "Serial",
            SerialPort = portName,
            BaudRate = baud,
            DataBits = dataBits,
            Parity = ParityBox.SelectedItem?.ToString() ?? nameof(SerialParityMode.None),
            StopBits = stopBits,
            FlowControl = FlowBox.SelectedItem?.ToString() ?? nameof(SerialFlowControl.None),
            TextEncoding = string.IsNullOrWhiteSpace(EncodingBox.Text) ? "utf-8" : EncodingBox.Text.Trim(),
            LineEnding = LineEndingBox.SelectedItem?.ToString() ?? nameof(SerialLineEnding.Lf),
            LocalEcho = LocalEchoBox.IsChecked == true,
            DtrEnable = DtrBox.IsChecked == true,
            RtsEnable = RtsBox.IsChecked == true,
            AutoReconnect = false
        };

        if (!SerialProfileValidator.TryValidate(def.ToSerialProfile(), out var error))
        {
            ErrorText.Text = error;
            return;
        }

        Result = def;
        DialogResult = true;
        Close();
    }

    private string ResolvePortName()
    {
        if (PortBox.SelectedItem is ComboBoxItem { Tag: string tagged })
            return tagged;
        var text = PortBox.Text?.Trim() ?? "";
        var dash = text.IndexOf('—');
        if (dash > 0) text = text[..dash].Trim();
        return text;
    }
}
