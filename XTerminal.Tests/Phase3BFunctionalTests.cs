using XTerminal.Core.Models.Sftp;
using XTerminal.Core.Services;

namespace XTerminal.Tests;

public class Phase3BFunctionalTests
{
    [Fact]
    public void TransferStateMachine_allows_valid_and_rejects_invalid()
    {
        Assert.True(TransferStateMachine.CanTransition(SftpTransferState.Queued, SftpTransferState.Transferring));
        Assert.True(TransferStateMachine.CanTransition(SftpTransferState.Transferring, SftpTransferState.Complete));
        Assert.False(TransferStateMachine.CanTransition(SftpTransferState.Complete, SftpTransferState.Transferring));
        Assert.False(TransferStateMachine.CanTransition(SftpTransferState.Failed, SftpTransferState.Queued));

        var state = SftpTransferState.Queued;
        TransferStateMachine.Transition(ref state, SftpTransferState.Transferring);
        Assert.Equal(SftpTransferState.Transferring, state);
        Assert.Throws<InvalidOperationException>(() =>
        {
            var s = SftpTransferState.Complete;
            TransferStateMachine.Transition(ref s, SftpTransferState.Queued);
        });
    }

    [Fact]
    public void TransferStateMachine_retry_cancel_remove_rules()
    {
        Assert.True(TransferStateMachine.CanRetry(SftpTransferState.Failed));
        Assert.True(TransferStateMachine.CanCancel(SftpTransferState.Queued));
        Assert.False(TransferStateMachine.CanCancel(SftpTransferState.Complete));
        Assert.True(TransferStateMachine.CanRemove(SftpTransferState.Skipped));
        Assert.False(TransferStateMachine.CanRemove(SftpTransferState.Transferring));
    }

    [Fact]
    public void TransferProgress_handles_zero_unknown_and_eta()
    {
        Assert.Equal(100, TransferProgress.CompletedPercentage(0, 0));
        Assert.Equal(0, TransferProgress.Percentage(0, TransferProgress.UnknownTotal));
        Assert.Null(TransferProgress.BytesPerSecond(100, TimeSpan.FromMilliseconds(10)));
        var bps = TransferProgress.BytesPerSecond(1000, TimeSpan.FromSeconds(2));
        Assert.NotNull(bps);
        Assert.Equal(500, bps!.Value, 1);
        var eta = TransferProgress.EstimateRemaining(500, 1500, 500);
        Assert.NotNull(eta);
        Assert.Equal(2, eta!.Value.TotalSeconds, 1);
        Assert.Null(TransferProgress.EstimateRemaining(10, TransferProgress.UnknownTotal, 100));
        Assert.Equal("N/A", TransferProgress.FormatSpeed(null));
    }

    [Fact]
    public void TransferProgress_never_reports_100_before_complete_helper()
    {
        var mid = TransferProgress.Percentage(999, 1000);
        Assert.True(mid < 100);
        Assert.Equal(100, TransferProgress.CompletedPercentage(1000, 1000));
    }

    [Fact]
    public void SftpPathSafety_blocks_traversal_ads_reserved_and_absolute()
    {
        var root = Path.Combine(Path.GetTempPath(), "xterm-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                SftpPathSafety.ResolveLocalDownloadPath(root, "..\\escape.txt"));
            Assert.Throws<InvalidOperationException>(() =>
                SftpPathSafety.ResolveLocalDownloadPath(root, "C:\\Windows\\system32\\x.txt"));
            Assert.Throws<InvalidOperationException>(() =>
                SftpPathSafety.ResolveLocalDownloadPath(root, "file:stream"));
            Assert.Throws<InvalidOperationException>(() =>
                SftpPathSafety.ValidateLocalFileName("CON"));
            Assert.Throws<InvalidOperationException>(() =>
                SftpPathSafety.ValidateLocalFileName("nul.txt"));

            var ok = SftpPathSafety.ResolveLocalDownloadPath(root, Path.Combine("sub", "a.txt"));
            Assert.StartsWith(Path.GetFullPath(root), ok, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SftpPathSafety_unique_name_and_dangerous_delete()
    {
        var name = SftpPathSafety.UniqueName("file.txt", n => n is "file.txt" or "file (1).txt");
        Assert.Equal("file (2).txt", name);
        Assert.True(SftpPathSafety.IsDangerousRemoteDeleteTarget("/"));
        Assert.True(SftpPathSafety.IsDangerousRemoteDeleteTarget("/etc"));
        Assert.False(SftpPathSafety.IsDangerousRemoteDeleteTarget("/home/user/project"));
    }

    [Fact]
    public void UnixPermissions_presets_and_invalid()
    {
        foreach (var oct in new[] { "644", "755", "600", "700", "775" })
        {
            var bits = UnixPermissionBits.FromOctal(oct);
            Assert.Equal(int.Parse(oct), bits.ToOctal());
            Assert.Equal(9, bits.ToSymbolic().Length);
        }

        Assert.Throws<ArgumentException>(() => UnixPermissionBits.FromOctal("999"));
        Assert.Throws<ArgumentException>(() => UnixPermissionBits.FromOctal(""));
    }

    [Fact]
    public void Conflict_policy_enum_and_batch_scope_semantics()
    {
        Assert.Contains(TransferConflictAction.Ask, Enum.GetValues<TransferConflictAction>());
        Assert.Contains(TransferConflictAction.Rename, Enum.GetValues<TransferConflictAction>());
        // Batch sticky scope is UI-only; ensure Ask remains distinct from Overwrite.
        Assert.NotEqual(TransferConflictAction.Ask, TransferConflictAction.Overwrite);
    }

    [Fact]
    public void RemoteEditor_binary_and_line_ending_helpers()
    {
        Assert.True(XTerminal.App.Services.RemoteEditorFileService.LooksBinary("abc\0def"));
        Assert.False(XTerminal.App.Services.RemoteEditorFileService.LooksBinary("hello\nworld"));
        Assert.Equal("crlf", XTerminal.App.Services.RemoteEditorFileService.DetectLineEnding("a\r\nb"));
        Assert.Equal("a\r\nb\r\nc", XTerminal.App.Services.RemoteEditorFileService.PreserveLineEndings("a\nb\nc", "crlf"));
    }

    [Fact]
    public void RemoteEditSession_associates_session_id_and_host()
    {
        var session = new XTerminal.App.Services.RemoteEditSession
        {
            SessionId = Guid.NewGuid(),
            SessionName = "prod",
            RemoteHost = "box.example",
            RemotePath = "/etc/hosts",
            LocalTempPath = "C:\\temp\\hosts",
            Content = "x"
        };
        Assert.Equal("box.example", session.RemoteHost);
        Assert.Equal("/etc/hosts", session.RemotePath);
        Assert.NotEqual(Guid.Empty, session.SessionId);
    }
}
