using System.IO;

namespace XTerminal.App.Services;

public static class EditorLanguageCatalog
{
    public static readonly IReadOnlyList<string> Supported =
    [
        "plaintext", "markdown", "json", "yaml", "xml", "ini", "powershell", "shell",
        "python", "javascript", "typescript", "html", "css", "sql", "dockerfile"
    ];

    public static string FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "plaintext";
        }

        var name = Path.GetFileName(path);
        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase))
        {
            return "dockerfile";
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" => "markdown",
            ".json" or ".jsonc" => "json",
            ".yml" or ".yaml" => "yaml",
            ".xml" or ".xsl" or ".csproj" or ".svg" => "xml",
            ".ini" or ".cfg" or ".conf" => "ini",
            ".ps1" or ".psm1" or ".psd1" => "powershell",
            ".sh" or ".bash" or ".zsh" => "shell",
            ".py" or ".pyw" => "python",
            ".js" or ".mjs" or ".cjs" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".html" or ".htm" => "html",
            ".css" or ".scss" or ".less" => "css",
            ".sql" => "sql",
            ".dockerfile" => "dockerfile",
            _ => "plaintext"
        };
    }

    public static string DisplayName(string languageId) => languageId switch
    {
        "plaintext" => "Plain Text",
        "markdown" => "Markdown",
        "json" => "JSON",
        "yaml" => "YAML",
        "xml" => "XML",
        "ini" => "INI",
        "powershell" => "PowerShell",
        "shell" => "Shell",
        "python" => "Python",
        "javascript" => "JavaScript",
        "typescript" => "TypeScript",
        "html" => "HTML",
        "css" => "CSS",
        "sql" => "SQL",
        "dockerfile" => "Dockerfile",
        _ => languageId
    };
}
