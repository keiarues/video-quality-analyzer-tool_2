namespace VideoQualityAnalyzer.App;

// 動画登録はUIスレッドで予約し、全削除・終了後の古い結果を無効にする。
internal sealed class LibraryLoadState : IDisposable
{
    private readonly Dictionary<string, long> reservations = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource cancellation = new();
    private bool disposed;

    public long Generation { get; private set; }
    public CancellationToken Token => cancellation.Token;
    public int ReservedCount => reservations.Count;
    public bool IsCurrent(long generation) => !disposed && generation == Generation;

    public bool TryReserve(string path, long generation)
    {
        if (!IsCurrent(generation) || reservations.ContainsKey(path)) return false;
        reservations.Add(path, generation);
        return true;
    }

    public void Release(string path, long generation)
    {
        if (reservations.TryGetValue(path, out var reservedGeneration) && reservedGeneration == generation)
            reservations.Remove(path);
    }

    public void CancelPending()
    {
        if (disposed) return;
        Generation++;
        cancellation.Cancel();
        cancellation.Dispose();
        cancellation = new CancellationTokenSource();
        reservations.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Generation++;
        cancellation.Cancel();
        cancellation.Dispose();
        reservations.Clear();
    }
}
