using System.Security.Cryptography;

namespace XTerminal.Core.Services.X11;

/// <summary>
/// Session-scoped MIT-MAGIC-COOKIE-1 material. Never log or persist the cookie bytes.
/// </summary>
public sealed class X11CookieMaterial : IDisposable
{
    public const string MitMagicCookie1 = "MIT-MAGIC-COOKIE-1";
    public const int CookieLength = 16;

    private byte[]? _cookie;
    private readonly Guid _sessionScope;
    private int _disposed;

    private X11CookieMaterial(Guid sessionScope, byte[] cookie)
    {
        _sessionScope = sessionScope;
        _cookie = cookie;
    }

    public Guid SessionScope => _sessionScope;
    public string ProtocolName => MitMagicCookie1;

    public static X11CookieMaterial Generate(Guid sessionScope)
    {
        var cookie = new byte[CookieLength];
        RandomNumberGenerator.Fill(cookie);
        return new X11CookieMaterial(sessionScope, cookie);
    }

    /// <summary>Constant-time compare against presented temporary cookie.</summary>
    public bool Matches(ReadOnlySpan<byte> presented)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var local = _cookie ?? throw new ObjectDisposedException(nameof(X11CookieMaterial));
        if (presented.Length != local.Length) return false;
        return CryptographicOperations.FixedTimeEquals(local, presented);
    }

    /// <summary>Copy for protocol use within the owning session only.</summary>
    public byte[] GetCookieCopy()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var local = _cookie ?? throw new ObjectDisposedException(nameof(X11CookieMaterial));
        var copy = new byte[local.Length];
        Buffer.BlockCopy(local, 0, copy, 0, local.Length);
        return copy;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_cookie is not null)
        {
            CryptographicOperations.ZeroMemory(_cookie);
            _cookie = null;
        }
    }
}

/// <summary>Prevents cross-session cookie reuse.</summary>
public sealed class X11CookieRegistry : IDisposable
{
    private readonly Dictionary<Guid, X11CookieMaterial> _bySession = new();
    private readonly object _gate = new();
    private int _disposed;

    public X11CookieMaterial CreateForSession(Guid sessionId)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        lock (_gate)
        {
            if (_bySession.TryGetValue(sessionId, out var existing))
            {
                existing.Dispose();
                _bySession.Remove(sessionId);
            }

            var material = X11CookieMaterial.Generate(sessionId);
            _bySession[sessionId] = material;
            return material;
        }
    }

    public bool TryGet(Guid sessionId, out X11CookieMaterial? material)
    {
        lock (_gate)
            return _bySession.TryGetValue(sessionId, out material);
    }

    public bool ValidateForSession(Guid sessionId, ReadOnlySpan<byte> presented)
    {
        lock (_gate)
        {
            if (!_bySession.TryGetValue(sessionId, out var material))
                return false;
            return material.Matches(presented);
        }
    }

    public void Release(Guid sessionId)
    {
        lock (_gate)
        {
            if (_bySession.Remove(sessionId, out var material))
                material.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate)
        {
            foreach (var m in _bySession.Values)
                m.Dispose();
            _bySession.Clear();
        }
    }
}
