namespace XTerminal.Core.Services;

/// <summary>
/// Optional shell-integration snippets. Never installed automatically —
/// callers must require explicit user approval before pasting into a shell rc/profile.
/// </summary>
public static class ShellIntegrationHooks
{
    public const string BashSnippet = """
# X-Terminal optional integration — paste only after explicit approval
__xterm_precmd() {
  local ec=$?
  printf '\033]133;D;%s\007' "$ec"
  printf '\033]7;file://%s%s\007' "${HOSTNAME:-localhost}" "$PWD"
  printf '\033]133;A\007'
}
__xterm_preexec() { printf '\033]133;C\007'; }
if [[ -z "${__XTERMINAL_HOOKS:-}" ]]; then
  __XTERMINAL_HOOKS=1
  PROMPT_COMMAND="__xterm_precmd${PROMPT_COMMAND:+;$PROMPT_COMMAND}"
  trap '__xterm_preexec' DEBUG
fi
""";

    public const string ZshSnippet = """
# X-Terminal optional integration — paste only after explicit approval
# Review conflicts with existing precmd/preexec hooks and themes before use.
if [[ -z "${__XTERMINAL_HOOKS:-}" ]]; then
  __XTERMINAL_HOOKS=1
  __xterm_precmd() {
    printf '\033]133;D;%s\007' $?
    printf '\033]7;file://%s%s\007' "${HOST:-localhost}" "$PWD"
    printf '\033]133;A\007'
  }
  __xterm_preexec() { printf '\033]133;C\007'; }
  autoload -Uz add-zsh-hook 2>/dev/null || true
  if typeset -f add-zsh-hook >/dev/null; then
    add-zsh-hook precmd __xterm_precmd
    add-zsh-hook preexec __xterm_preexec
  else
    precmd_functions+=(__xterm_precmd)
    preexec_functions+=(__xterm_preexec)
  fi
fi
""";

    public const string PowerShellSnippet = """
# X-Terminal optional integration (PowerShell 7+) — paste only after explicit approval
# Prefer VS Code / Windows Terminal OSC 633 if already enabled; do not overwrite $PROFILE blindly.
if (-not $env:__XTERMINAL_HOOKS) {
  $env:__XTERMINAL_HOOKS = '1'
  function prompt {
    $ec = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    Write-Host -NoNewline ("`e]133;D;{0}`a" -f $ec)
    Write-Host -NoNewline ("`e]633;P;Cwd={0}`a" -f (Get-Location).Path)
    Write-Host -NoNewline "`e]133;A`a"
    "PS $($executionContext.SessionState.Path.CurrentLocation)> "
  }
}
""";

    public static string GetSnippet(string shell) =>
        shell.Trim().ToLowerInvariant() switch
        {
            "bash" => BashSnippet,
            "zsh" => ZshSnippet,
            "pwsh" or "powershell" or "powershell7" => PowerShellSnippet,
            _ => BashSnippet
        };
}
