namespace PacConsole.Core.Persistence;

/// <summary>Where the console's operator record lives between launches.</summary>
public interface IOperatorJournal
{
    /// <summary>The record found at launch; null on a first launch, or when the file there could not be read.</summary>
    OperatorRecord? Restored { get; }

    /// <summary>
    /// Writes <paramref name="record"/> unless a later one (a higher <paramref name="version"/>) is already written.
    /// False when it could not be written; the journal logs why, and the console keeps the record in memory.
    /// </summary>
    ValueTask<bool> SaveAsync(OperatorRecord record, long version, CancellationToken ct);
}
