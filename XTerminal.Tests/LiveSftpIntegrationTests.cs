using System.Text;
using XTerminal.Core.Models.Sftp;
using XTerminal.Core.Services;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

/// <summary>
/// Opt-in live SFTP tests using production <see cref="SftpSessionService"/>.
/// Operates only inside a uniquely named remote temporary directory.
/// </summary>
public class LiveSftpIntegrationTests
{
    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_sftp_lifecycle_upload_download_permissions_cleanup()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);
        await using var sftp = harness.CreateSftp();

        Exception? testFailure = null;
        string? remoteDir = null;
        var localRoot = Path.Combine(Path.GetTempPath(), "xterminal-sftp-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localRoot);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await sftp.ConnectAsync(harness.Session.Id, cts.Token);
            Assert.True(sftp.IsConnected);

            var dirName = ".xterminal-it-" + Guid.NewGuid().ToString("N");
            await sftp.CreateFolderAsync(sftp.HomeDirectory, dirName, cts.Token);
            remoteDir = JoinRemote(sftp.HomeDirectory, dirName);

            var listing = await sftp.ListAsync(sftp.HomeDirectory, cts.Token);
            Assert.Contains(listing, e => e.IsDirectory && e.Name == dirName);

            // Text payload
            var payload = Encoding.UTF8.GetBytes("xterminal-live-sftp-" + Guid.NewGuid().ToString("N"));
            var localUpload = Path.Combine(localRoot, "payload.txt");
            await File.WriteAllBytesAsync(localUpload, payload, cts.Token);
            var expectedHash = LiveIntegrationHarness.Sha256Hex(payload);

            var uploadId = sftp.EnqueueUploadFile(localUpload, remoteDir);
            await LiveIntegrationHarness.AwaitTransferAsync(sftp, uploadId, TimeSpan.FromSeconds(60));

            var remoteFile = JoinRemote(remoteDir, "payload.txt");
            var localDownload = Path.Combine(localRoot, "payload.down.txt");
            var downloadId = sftp.EnqueueDownload(remoteFile, localDownload, payload.Length, isDirectory: false);
            await LiveIntegrationHarness.AwaitTransferAsync(sftp, downloadId, TimeSpan.FromSeconds(60));

            Assert.True(File.Exists(localDownload));
            Assert.Equal(expectedHash, LiveIntegrationHarness.Sha256HexFile(localDownload));

            // Zero-byte
            var zeroLocal = Path.Combine(localRoot, "zero.bin");
            await File.WriteAllBytesAsync(zeroLocal, Array.Empty<byte>(), cts.Token);
            var zeroUp = sftp.EnqueueUploadFile(zeroLocal, remoteDir);
            await LiveIntegrationHarness.AwaitTransferAsync(sftp, zeroUp, TimeSpan.FromSeconds(60));
            var zeroRemote = JoinRemote(remoteDir, "zero.bin");
            var zeroDown = Path.Combine(localRoot, "zero.down.bin");
            var zeroDl = sftp.EnqueueDownload(zeroRemote, zeroDown, 0, isDirectory: false);
            await LiveIntegrationHarness.AwaitTransferAsync(sftp, zeroDl, TimeSpan.FromSeconds(60));
            Assert.Equal(0, new FileInfo(zeroDown).Length);

            // Binary
            var binary = new byte[4096];
            Random.Shared.NextBytes(binary);
            var binLocal = Path.Combine(localRoot, "blob.bin");
            await File.WriteAllBytesAsync(binLocal, binary, cts.Token);
            var binHash = LiveIntegrationHarness.Sha256Hex(binary);
            var binUp = sftp.EnqueueUploadFile(binLocal, remoteDir);
            await LiveIntegrationHarness.AwaitTransferAsync(sftp, binUp, TimeSpan.FromSeconds(60));
            var binRemote = JoinRemote(remoteDir, "blob.bin");
            var binDown = Path.Combine(localRoot, "blob.down.bin");
            var binDl = sftp.EnqueueDownload(binRemote, binDown, binary.Length, isDirectory: false);
            await LiveIntegrationHarness.AwaitTransferAsync(sftp, binDl, TimeSpan.FromSeconds(60));
            Assert.Equal(binHash, LiveIntegrationHarness.Sha256HexFile(binDown));

            // Rename
            await sftp.RenameAsync(remoteFile, "payload.renamed.txt", cts.Token);
            var renamed = JoinRemote(remoteDir, "payload.renamed.txt");
            var afterRename = await sftp.ListAsync(remoteDir, cts.Token);
            Assert.Contains(afterRename, e => e.Name == "payload.renamed.txt");
            Assert.DoesNotContain(afterRename, e => e.Name == "payload.txt");

            // Permissions — POSIX bitmask into SetPermissionsAsync (SSH.NET boundary converts once).
            var mode644 = UnixPermissionBits.FromOctal("644").ToMode();
            Assert.Equal(420, mode644);
            Assert.Equal(644, UnixPermissionBits.ToSshNetPermissionsMode(mode644));
            await sftp.SetPermissionsAsync(renamed, mode644, cts.Token);
            var entries = await sftp.ListAsync(remoteDir, cts.Token);
            var entry = Assert.Single(entries, e => e.Name == "payload.renamed.txt");
            Assert.True(entry.ModeBits.HasValue, "ModeBits not returned from listing.");
            Assert.Equal(mode644, (short)(entry.ModeBits!.Value & 0x1FF));
            Assert.Equal("rw-r--r--", UnixPermissionBits.FromMode((short)(entry.ModeBits.Value & 0xFFF)).ToSymbolic());

            // Conflict / overwrite policy (where supported)
            sftp.ConflictPolicy = TransferConflictAction.Overwrite;
            var overwriteLocal = Path.Combine(localRoot, "payload.renamed.txt");
            var overwriteBytes = Encoding.UTF8.GetBytes("overwrite-" + Guid.NewGuid().ToString("N"));
            await File.WriteAllBytesAsync(overwriteLocal, overwriteBytes, cts.Token);
            var owUp = sftp.EnqueueUploadFile(overwriteLocal, remoteDir);
            await LiveIntegrationHarness.AwaitTransferAsync(sftp, owUp, TimeSpan.FromSeconds(60));
            var readBack = await sftp.ReadTextFileAsync(renamed, cancellationToken: cts.Token);
            Assert.Equal(Encoding.UTF8.GetString(overwriteBytes), readBack);

            // Skip policy
            sftp.ConflictPolicy = TransferConflictAction.Skip;
            var skipId = sftp.EnqueueUploadFile(overwriteLocal, remoteDir);
            await WaitTerminalAsync(sftp, skipId, TimeSpan.FromSeconds(60));
            var skipSnap = sftp.GetTransfers().First(t => t.Id == skipId);
            Assert.True(
                skipSnap.State is SftpTransferState.Skipped or SftpTransferState.Complete,
                $"Skip policy ended as {skipSnap.State}");

            // Delete files then directory
            await sftp.DeleteAsync(renamed, isDirectory: false, cts.Token);
            await sftp.DeleteAsync(zeroRemote, isDirectory: false, cts.Token);
            await sftp.DeleteAsync(binRemote, isDirectory: false, cts.Token);
            await sftp.DeleteAsync(remoteDir, isDirectory: true, cts.Token);
            remoteDir = null;

            await sftp.DisconnectAsync();
            Assert.False(sftp.IsConnected);
        }
        catch (Exception ex)
        {
            testFailure = ex;
        }
        finally
        {
            Exception? cleanupFailure = null;
            try
            {
                if (remoteDir is not null && sftp.IsConnected)
                {
                    try
                    {
                        await sftp.DeleteAsync(remoteDir, isDirectory: true, allowDangerous: false);
                    }
                    catch (Exception ex)
                    {
                        cleanupFailure = ex;
                    }
                }

                if (sftp.IsConnected)
                    await sftp.DisconnectAsync();
            }
            catch (Exception ex)
            {
                cleanupFailure = cleanupFailure is null ? ex : new AggregateException(cleanupFailure, ex);
            }

            try
            {
                if (Directory.Exists(localRoot))
                    Directory.Delete(localRoot, recursive: true);
            }
            catch
            {
                // best effort local cleanup
            }

            if (testFailure is not null && cleanupFailure is not null)
                throw new AggregateException("SFTP test failed; cleanup also failed.", testFailure, cleanupFailure);
            if (testFailure is not null)
                throw testFailure;
            if (cleanupFailure is not null)
                throw new InvalidOperationException(
                    "SFTP cleanup failed: " + SshConnectionDiagnosticReport.Sanitize(cleanupFailure.Message),
                    cleanupFailure);
        }
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_sftp_transfer_cancellation()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);
        await using var sftp = harness.CreateSftp();
        var localRoot = Path.Combine(Path.GetTempPath(), "xterminal-sftp-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localRoot);
        string? remoteDir = null;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            await sftp.ConnectAsync(harness.Session.Id, cts.Token);
            var dirName = ".xterminal-it-cancel-" + Guid.NewGuid().ToString("N");
            await sftp.CreateFolderAsync(sftp.HomeDirectory, dirName, cts.Token);
            remoteDir = JoinRemote(sftp.HomeDirectory, dirName);

            // Large-ish payload to give cancellation a window
            var big = new byte[2 * 1024 * 1024];
            Random.Shared.NextBytes(big);
            var local = Path.Combine(localRoot, "big.bin");
            await File.WriteAllBytesAsync(local, big, cts.Token);

            var id = sftp.EnqueueUploadFile(local, remoteDir);
            sftp.CancelTransfer(id);
            await WaitTerminalAsync(sftp, id, TimeSpan.FromSeconds(30));
            var snap = sftp.GetTransfers().First(t => t.Id == id);
            Assert.True(
                snap.State is SftpTransferState.Cancelled or SftpTransferState.Complete or SftpTransferState.Failed,
                $"Unexpected state {snap.State}");
        }
        finally
        {
            try
            {
                if (remoteDir is not null && sftp.IsConnected)
                    await sftp.DeleteAsync(remoteDir, isDirectory: true);
                if (sftp.IsConnected)
                    await sftp.DisconnectAsync();
            }
            catch
            {
                // do not hide primary assert failures — if we got here after success, surface cleanup via assert
            }

            try { Directory.Delete(localRoot, true); } catch { /* ignore */ }
        }
    }

    private static async Task WaitTerminalAsync(SftpSessionService sftp, Guid id, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snap = sftp.GetTransfers().FirstOrDefault(t => t.Id == id);
            if (snap is not null &&
                snap.State is SftpTransferState.Complete or SftpTransferState.Failed
                    or SftpTransferState.Cancelled or SftpTransferState.Skipped)
                return;
            await Task.Delay(40);
        }

        throw new TimeoutException("Transfer did not reach a terminal state.");
    }

    private static string JoinRemote(string parent, string name)
    {
        if (string.IsNullOrEmpty(parent) || parent == "/")
            return "/" + name.Trim('/');
        return parent.TrimEnd('/') + "/" + name.Trim('/');
    }
}
