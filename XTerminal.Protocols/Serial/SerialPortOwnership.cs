using System.Collections.Concurrent;

namespace XTerminal.Protocols.Serial;

/// <summary>Tracks exclusive COM port ownership within this process.</summary>
public static class SerialPortOwnership
{
    private static readonly ConcurrentDictionary<string, Guid> Owners =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool TryAcquire(string portName, Guid sessionId, out string? conflictOwner)
    {
        conflictOwner = null;
        var key = Normalize(portName);
        if (Owners.TryGetValue(key, out var existing) && existing != sessionId)
        {
            conflictOwner = existing.ToString("N");
            return false;
        }

        Owners[key] = sessionId;
        return true;
    }

    public static void Release(string portName, Guid sessionId)
    {
        var key = Normalize(portName);
        if (Owners.TryGetValue(key, out var existing) && existing == sessionId)
            Owners.TryRemove(key, out _);
    }

    public static bool IsOwned(string portName) =>
        Owners.ContainsKey(Normalize(portName));

    /// <summary>Test helper — clears process-local ownership map.</summary>
    public static void ResetForTests() => Owners.Clear();

    private static string Normalize(string portName) => portName.Trim().ToUpperInvariant();
}
