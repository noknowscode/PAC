using PacConsole.Core.Persistence;

namespace PacConsole.Services;

/// <summary>
/// The operator record in the app's LocalFolder (<c>operator/operator.json</c>): read once at launch, before the page
/// model is built, and written after every operator action. Writes run off the UI thread one at a time, and one that a
/// later change overtook is skipped, so the file always holds the newest record. A failure is logged and reported to the
/// caller; the console keeps working from memory.
/// </summary>
public sealed class OperatorJournal(TimeProvider time, ILogger<OperatorJournal> log, Func<CancellationToken, Task<string>>? folder = null) : IOperatorJournal
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OperatorStore? _store;
    private long _written;

    public OperatorRecord? Restored { get; private set; }

    /// <summary>
    /// Finds the folder and reads the record. On WebAssembly the folder is persisted in IndexedDB and is ready only once
    /// a StorageFolder call has awaited it, so the folder comes from <see cref="ApplicationData"/>, never a bare path.
    /// Any storage fault here is logged and the console starts fresh: it must open whatever the storage does. With no
    /// folder, every save reports failure, and the log band says the record is only in memory.
    /// </summary>
    public async Task OpenAsync(CancellationToken ct)
    {
        try
        {
            _store = new OperatorStore(await (folder ?? LocalFolderAsync)(ct));
            var load = await Task.Run(() => _store.Load(time.GetUtcNow()), ct);
            Restored = load.Record;
            if (load.SetAside is { } aside) log.LogWarning("The saved operator record could not be read; it was kept as {Path} and the console starts fresh", aside);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "The saved operator record could not be opened; the console starts fresh");
        }
    }

    public async ValueTask<bool> SaveAsync(OperatorRecord record, long version, CancellationToken ct)
    {
        if (_store is not { } store) return false;
        await _gate.WaitAsync(ct);
        try
        {
            if (version <= _written) return true;
            await Task.Run(() => store.Save(record), CancellationToken.None);
            _written = version;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError(ex, "The operator record could not be saved to {Path}", store.FilePath);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<string> LocalFolderAsync(CancellationToken ct) =>
        (await ApplicationData.Current.LocalFolder.CreateFolderAsync("operator", CreationCollisionOption.OpenIfExists).AsTask(ct)).Path;
}
