using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Persistence;

/// <summary>What a launch found on disk: the record, or none; and the path a file that could not be read was moved to.</summary>
public sealed record OperatorLoad(OperatorRecord? Record, string? SetAside)
{
    public static readonly OperatorLoad Empty = new(null, null);
}

/// <summary>
/// The operator record as one JSON file in a folder the app owns (its LocalFolder). Every save writes a whole new file
/// beside the old one and moves it over the old in one step, so a crash or a full disk mid-write leaves the last good
/// record, never half of one. A file that cannot be read (damaged, or from another version) is kept aside under a new
/// name and the console starts fresh, rather than failing to open.
/// </summary>
public sealed class OperatorStore(string directory)
{
    public const string FileName = "operator.json";

    public string FilePath { get; } = Path.Combine(directory, FileName);

    private string TempPath => FilePath + ".tmp";

    /// <summary>Reads the record. <paramref name="now"/> names a file set aside.</summary>
    public OperatorLoad Load(DateTimeOffset now)
    {
        if (!File.Exists(FilePath)) return OperatorLoad.Empty;
        OperatorRecord? record;
        try
        {
            using var stream = File.OpenRead(FilePath);
            record = JsonSerializer.Deserialize(stream, OperatorJson.Default.OperatorFile)?.ToRecord();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or KeyNotFoundException)
        {
            record = null;
        }
        if (record is not null) return new(record, null);

        var aside = $"{FilePath}.unreadable-{now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        File.Move(FilePath, aside, overwrite: true);
        return new(null, aside);
    }

    /// <summary>Writes the record whole, then puts it in place of the last one.</summary>
    public void Save(OperatorRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, OperatorFile.From(record), OperatorJson.Default.OperatorFile);
            stream.Flush(flushToDisk: true);
        }
        File.Move(TempPath, FilePath, overwrite: true);
    }
}

/// <summary>
/// The file's shape, version 1. Plain lists and dictionaries rather than the domain's immutable collections, so the
/// format is stated here once and a change to <see cref="PlantOps"/> shows up as a change to this record.
/// </summary>
internal sealed record OperatorFile(
    int Version,
    long Tick,
    DateTimeOffset SavedAt,
    ControlMode Mode,
    Setpoints Setpoints,
    List<Device> Devices,
    List<Valve> Valves,
    Dictionary<int, long> ValveMoves,
    Ramp FanSpeed,
    Ramp Load,
    Ramp Reactive,
    SiloMark Silo,
    Dictionary<string, long> Acks,
    Dictionary<string, long> Shelves,
    List<LogEntry> Log,
    List<SetpointChange> SetpointChanges)
{
    public const int CurrentVersion = 1;

    public static OperatorFile From(OperatorRecord r)
    {
        var o = r.Ops;
        return new(CurrentVersion, r.Tick, r.SavedAt, r.Mode, o.Setpoints, [.. o.Devices.Values.OrderBy(d => d.Id, StringComparer.Ordinal)], [.. o.Valves],
            new(o.ValveMoves), o.FanSpeed, o.Load, o.Reactive, o.Silo, new(o.Acks), new(o.Shelves), [.. o.Log], [.. o.SetpointChanges]);
    }

    /// <summary>The record, or null when the file is from another version or does not describe this plant.</summary>
    public OperatorRecord? ToRecord()
    {
        var initial = PlantOps.Initial;
        if (Version != CurrentVersion
            || Devices is null || Valves is null || Log is null || SetpointChanges is null || ValveMoves is null || Acks is null || Shelves is null
            || Setpoints is null || FanSpeed is null || Load is null || Reactive is null || Silo is null
            || !Devices.Select(d => d.Id).Order(StringComparer.Ordinal).SequenceEqual(initial.Devices.Keys.Order(StringComparer.Ordinal))
            || !Valves.Select(v => v.Id).SequenceEqual(initial.Valves.Select(v => v.Id)))
        {
            return null;
        }
        var ops = initial with
        {
            Devices = Devices.ToImmutableDictionary(d => d.Id),
            Valves = [.. Valves],
            ValveMoves = ValveMoves.ToImmutableDictionary(),
            FanSpeed = FanSpeed,
            Load = Load,
            Reactive = Reactive,
            Silo = Silo,
            Acks = Acks.ToImmutableDictionary(),
            Shelves = Shelves.ToImmutableDictionary(),
            Log = [.. Log],
            Pending = null,
            SetpointChanges = [.. SetpointChanges],
            Setpoints = Setpoints,
        };
        return new OperatorRecord(Tick, SavedAt, Mode, ops);
    }
}

/// <summary>
/// Source-generated, so the trimmed heads (WebAssembly, iOS, Android) keep every member the file needs. Enums are
/// written by name; a Manual step's rate is +Infinity, which plain JSON numbers cannot carry.
/// </summary>
[JsonSourceGenerationOptions(
    UseStringEnumConverter = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OperatorFile))]
internal sealed partial class OperatorJson : JsonSerializerContext;
