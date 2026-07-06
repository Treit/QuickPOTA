using System.Net.Http;
using System.Text;

namespace QuickPOTA;

internal sealed class Parks
{
    private static readonly Uri SourceUrl = new("https://pota.app/all_parks_ext.csv");
    private const string EmbeddedName = "QuickPOTA.all_parks_ext.csv";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private static readonly HttpClient Http = CreateHttpClient();

    private volatile Dictionary<string, string> _byRef = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoaded => _byRef.Count > 0;
    public int Count => _byRef.Count;
    public string Source { get; private set; } = "none";

    public string? Lookup(string reference)
        => _byRef.TryGetValue(reference.Trim(), out var name) ? name : null;

    public static string CacheDir
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".quickpota");
            }
            var dir = Path.Combine(baseDir, "QuickPOTA");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string CachePath => Path.Combine(CacheDir, "all_parks_ext.csv");

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuickPOTA/1.0");
        return client;
    }

    public async Task LoadAsync(bool forceRefresh = false)
    {
        var path = CachePath;
        var cacheFresh = File.Exists(path) &&
                         (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)) <= MaxAge;

        if (forceRefresh || !cacheFresh)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(SourceUrl);
                await File.WriteAllBytesAsync(path, bytes);
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            catch (IOException)
            {
            }
        }

        if (File.Exists(path))
        {
            await using var fs = File.OpenRead(path);
            if (await LoadFromStreamAsync(fs))
            {
                Source = "cache";
                return;
            }
        }

        await using var embedded = typeof(Parks).Assembly.GetManifestResourceStream(EmbeddedName);
        if (embedded is not null && await LoadFromStreamAsync(embedded))
        {
            Source = "embedded";
        }
    }

    private async Task<bool> LoadFromStreamAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync();
        if (header is null)
        {
            return false;
        }

        var cols = ParseCsvLine(header);
        var refIdx = FindIndex(cols, "reference");
        var nameIdx = FindIndex(cols, "name");
        if (refIdx < 0 || nameIdx < 0)
        {
            return false;
        }

        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }
            var fields = ParseCsvLine(line);
            if (fields.Count <= Math.Max(refIdx, nameIdx))
            {
                continue;
            }
            var r = fields[refIdx].Trim();
            var n = fields[nameIdx].Trim();
            if (r.Length > 0 && !next.ContainsKey(r))
            {
                next[r] = n;
            }
        }

        if (next.Count == 0)
        {
            return false;
        }

        _byRef = next;
        return true;
    }

    private static int FindIndex(List<string> cols, string target)
    {
        for (var i = 0; i < cols.Count; i++)
        {
            if (string.Equals(cols[i].Trim(), target, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == ',')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '"' && sb.Length == 0)
                {
                    inQuotes = true;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        result.Add(sb.ToString());
        return result;
    }
}
