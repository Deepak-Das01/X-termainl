using System.Runtime.InteropServices;
using System.Text;

namespace XTerminal.Core.Security;

/// <summary>
/// Windows Credential Manager (CRED_TYPE_GENERIC) backed secret store.
/// Secrets never land in JSON session files.
/// </summary>
public sealed class WindowsCredentialStore : ISecretStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public Task SaveSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        secret ??= string.Empty;

        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = key,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = "X-Terminal secure secret",
                Flags = 0
            };

            if (!CredWrite(ref cred, 0))
            {
                throw new InvalidOperationException($"CredWrite failed: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!CredRead(key, CredTypeGeneric, 0, out var ptr) || ptr == IntPtr.Zero)
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(string.Empty);
            }

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes));
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        CredDelete(key, CredTypeGeneric, 0);
        return Task.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
