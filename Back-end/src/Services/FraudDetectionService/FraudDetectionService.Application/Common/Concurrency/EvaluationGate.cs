namespace FraudDetectionService.Application;

public sealed class EvaluationGate
{
    private readonly object sync = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References;
    }
    public async Task<IDisposable> EnterAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        var held = new List<(string Key, Entry Entry)>();
        try
        {
            // Lock every shared feature resource, in a consistent order to avoid deadlocks.
            foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                Entry entry;
                lock (sync)
                {
                    if (!entries.TryGetValue(key, out entry!)) entries[key] = entry = new();
                    entry.References++;
                }
                try { await entry.Semaphore.WaitAsync(ct); }
                catch { Dereference(key, entry); throw; }
                held.Add((key, entry));
            }
            return new Lease(this, held);
        }
        catch { Release(held); throw; }
    }
    private void Dereference(string key, Entry entry)
    {
        lock (sync)
        {
            if (--entry.References == 0)
            {
                entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }
    private void Release(List<(string Key, Entry Entry)> held)
    {
        for (var i = held.Count - 1; i >= 0; i--)
        {
            held[i].Entry.Semaphore.Release();
            Dereference(held[i].Key, held[i].Entry);
        }
    }
    private sealed class Lease(EvaluationGate owner, List<(string Key, Entry Entry)> held) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Release(held);
        }
    }
}
