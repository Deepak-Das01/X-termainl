namespace XTerminal.Tests;

public class ShellOutputMarkerTests
{
    [Fact]
    public void Rejects_quoted_echo_of_printf_command()
    {
        var marker = "XTOK-abc123";
        var echo = $"user@host:~$ printf '%s\\n' '{marker}'; printf '\\x1b]7;file://localhost/tmp\\x07'\r\n";
        Assert.False(ShellOutputMarker.ContainsUnquotedLine(echo, marker));
    }

    [Fact]
    public void Accepts_printf_output_line_with_crlf()
    {
        var marker = "XTOK-abc123";
        var text = $"user@host:~$ printf '%s\\n' '{marker}'\r\n{marker}\r\n";
        Assert.True(ShellOutputMarker.ContainsUnquotedLine(text, marker));
    }

    [Fact]
    public void Accepts_printf_output_line_with_lf()
    {
        var marker = "XTFAIL-deadbeef";
        var text = $"prompt$ printf '%s\\n' '{marker}'\n{marker}\n";
        Assert.True(ShellOutputMarker.ContainsUnquotedLine(text, marker));
    }
}
