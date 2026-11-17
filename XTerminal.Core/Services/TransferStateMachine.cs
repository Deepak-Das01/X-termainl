using XTerminal.Core.Models.Sftp;

namespace XTerminal.Core.Services;

public static class TransferStateMachine
{
    private static readonly Dictionary<SftpTransferState, HashSet<SftpTransferState>> Allowed = new()
    {
        [SftpTransferState.Queued] =
        [
            SftpTransferState.Transferring,
            SftpTransferState.Cancelled,
            SftpTransferState.Skipped
        ],
        [SftpTransferState.Transferring] =
        [
            SftpTransferState.Complete,
            SftpTransferState.Failed,
            SftpTransferState.Cancelled,
            SftpTransferState.Skipped
        ],
        [SftpTransferState.Complete] = [],
        [SftpTransferState.Failed] = [],
        [SftpTransferState.Cancelled] = [],
        [SftpTransferState.Skipped] = []
    };

    public static bool CanTransition(SftpTransferState from, SftpTransferState to) =>
        from == to || (Allowed.TryGetValue(from, out var set) && set.Contains(to));

    public static void Transition(ref SftpTransferState state, SftpTransferState next)
    {
        if (!CanTransition(state, next))
            throw new InvalidOperationException($"Invalid transfer state transition: {state} → {next}");
        state = next;
    }

    public static bool CanRetry(SftpTransferState state) =>
        state is SftpTransferState.Failed or SftpTransferState.Cancelled or SftpTransferState.Skipped;

    public static bool CanCancel(SftpTransferState state) =>
        state is SftpTransferState.Queued or SftpTransferState.Transferring;

    public static bool CanRemove(SftpTransferState state) =>
        state is SftpTransferState.Queued
            or SftpTransferState.Complete
            or SftpTransferState.Failed
            or SftpTransferState.Cancelled
            or SftpTransferState.Skipped;
}
