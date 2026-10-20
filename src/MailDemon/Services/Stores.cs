using System.Text.Json;
using MailDemon.Models;

namespace MailDemon.Services;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}

/// <summary>Basit, thread-safe JSON dosyası saklama.</summary>
public class JsonFile<T> where T : new()
{
    private readonly string _path;
    private readonly object _lock = new();
    private T _value;

    public JsonFile(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _value = File.Exists(_path)
            ? JsonSerializer.Deserialize<T>(File.ReadAllText(_path), Json.Options) ?? new T()
            : new T();
    }

    public T Read()
    {
        lock (_lock)
        {
            // Çağıranın paylaşılan nesneyi değiştirmemesi için kopya döndür.
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(_value, Json.Options), Json.Options)!;
        }
    }

    /// <summary>Kopyalamadan, kilit altında salt-okunur sorgu.</summary>
    public TResult Query<TResult>(Func<T, TResult> query)
    {
        lock (_lock) return query(_value);
    }

    public void Update(Action<T> change)
    {
        lock (_lock)
        {
            change(_value);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_value, Json.Options));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public void Write(T value) => Update(_ => _value = value);
}

public class SettingsStore(IConfiguration config)
    : JsonFile<AppSettings>(Path.Combine(DataDir.Get(config), "settings.json"));

public class AnalysisState
{
    public List<MailAnalysis> Analyses { get; set; } = new();
    public List<Alert> Alerts { get; set; } = new();
    public DateTimeOffset? LastScanAt { get; set; }
}

public class AnalysisStore(IConfiguration config)
    : JsonFile<AnalysisState>(Path.Combine(DataDir.Get(config), "analyses.json"))
{
    private const int MaxAnalyses = 2000;

    public bool Contains(string mailId) => Query(s => s.Analyses.Any(a => a.Id == mailId));

    public void Add(MailAnalysis analysis, Alert? alert)
    {
        Update(s =>
        {
            s.Analyses.RemoveAll(a => a.Id == analysis.Id);
            s.Analyses.Insert(0, analysis);
            if (s.Analyses.Count > MaxAnalyses)
                s.Analyses.RemoveRange(MaxAnalyses, s.Analyses.Count - MaxAnalyses);
            if (alert is not null)
                s.Alerts.Insert(0, alert);
            s.Alerts.RemoveAll(a => a.Acknowledged && a.CreatedAt < DateTimeOffset.UtcNow.AddDays(-7));
        });
    }
}

public static class DataDir
{
    public static string Get(IConfiguration config) =>
        Path.GetFullPath(config["MailDemon:DataDirectory"] ?? "data");
}
