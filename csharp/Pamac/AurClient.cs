using System.Net.Http.Headers;
using System.Text.Json;

namespace Pamac;

public interface IAURPlugin
{
    string RealBuildDirectory { get; }
    void SetRealBuildDirectory(string directory);
    AURInfos? GetInfos(string packageName);
    IReadOnlyList<AURInfos> GetMultiInfos(IEnumerable<string> packageNames);
    IReadOnlyList<AURInfos> GetProviders(string dependency);
    IReadOnlyList<AURInfos> Search(string searchString);
    Task<AURInfos?> GetInfosAsync(string packageName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AURInfos>> SearchAsync(string searchString, CancellationToken cancellationToken = default);
    event EventHandler<ProgressEventArgs>? DownloadProgress;
    event EventHandler<ErrorEventArgs>? DownloadError;
}

/// <summary>Client for the AUR RPC v5 API.</summary>
public class AURClient : IAURPlugin, IDisposable
{
    private const string AurUrl = "https://aur.archlinux.org/rpc/v5";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly Dictionary<string, AURInfos> infos = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<AURInfos>> searchCache = new(StringComparer.OrdinalIgnoreCase);
    private string realBuildDirectory = Path.Combine(Path.GetTempPath(), "pamac-build-" + Environment.UserName);

    public AURClient(HttpClient? client = null)
    {
        ownsHttpClient = client is null;
        httpClient = client ?? new HttpClient();
        if (httpClient.BaseAddress is null) httpClient.BaseAddress = new Uri(AurUrl + "/");
        if (httpClient.Timeout == Timeout.InfiniteTimeSpan) httpClient.Timeout = TimeSpan.FromSeconds(20);
        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pamac", VersionInfo.Value));
    }

    public string RealBuildDirectory => realBuildDirectory;

    public void SetRealBuildDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The build directory cannot be empty.", nameof(directory));
        realBuildDirectory = Environment.UserName == "root" && (directory == "/tmp" || directory == "/var/tmp")
            ? "/var/cache/pamac"
            : directory is "/tmp" or "/var/tmp"
                ? Path.Combine(directory, "pamac-build-" + Environment.UserName)
                : directory;
    }

    public AURInfos? GetInfos(string packageName)
        => GetInfosAsync(packageName).GetAwaiter().GetResult();

    public async Task<AURInfos?> GetInfosAsync(string packageName, CancellationToken cancellationToken = default)
    {
        ValidatePackageName(packageName);
        if (infos.TryGetValue(packageName, out var cached)) return cached;

        using var response = await httpClient.GetAsync($"info/{Uri.EscapeDataString(packageName)}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            DownloadError?.Invoke(this, new ErrorEventArgs($"AUR request failed with HTTP {(int)response.StatusCode}."));
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            var result = document.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
                ? results.EnumerateArray().FirstOrDefault()
                : default;
            if (result.ValueKind != JsonValueKind.Object) return null;
            var value = ParseInfo(result);
            infos[ value.Name ] = value;
            return value;
        }
        catch (JsonException exception)
        {
            DownloadError?.Invoke(this, new ErrorEventArgs(exception.Message));
            return null;
        }
    }

    public IReadOnlyList<AURInfos> GetMultiInfos(IEnumerable<string> packageNames)
        => GetMultiInfosAsync(packageNames).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<AURInfos>> GetMultiInfosAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken = default)
    {
        var result = new List<AURInfos>();
        foreach (var name in packageNames.Distinct(StringComparer.Ordinal))
        {
            var info = await GetInfosAsync(name, cancellationToken).ConfigureAwait(false);
            if (info is not null) result.Add(info);
        }
        return result;
    }

    public IReadOnlyList<AURInfos> GetProviders(string dependency)
        => GetProvidersAsync(dependency).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<AURInfos>> GetProvidersAsync(string dependency, CancellationToken cancellationToken = default)
    {
        var dependencyName = AlpmVersionComparer.GetName(dependency);
        var candidates = await SearchAsync(dependencyName, cancellationToken).ConfigureAwait(false);
        return candidates.Where(info => info.Provides.Any(provide =>
            AlpmVersionComparer.GetName(provide) == dependencyName)).ToArray();
    }

    public IReadOnlyList<AURInfos> Search(string searchString)
        => SearchAsync(searchString).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<AURInfos>> SearchAsync(string searchString, CancellationToken cancellationToken = default)
    {
        var query = searchString.Trim();
        if (query.Length == 0) return Array.Empty<AURInfos>();
        if (searchCache.TryGetValue(query, out var cached)) return cached;

        using var response = await httpClient.GetAsync($"search/{Uri.EscapeDataString(query)}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            DownloadError?.Invoke(this, new ErrorEventArgs($"AUR search failed with HTTP {(int)response.StatusCode}."));
            return Array.Empty<AURInfos>();
        }

        var parsed = new List<AURInfos>();
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            if (document.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in results.EnumerateArray())
                {
                    var info = ParseInfo(result);
                    infos[info.Name] = info;
                    parsed.Add(info);
                }
            }
        }
        catch (JsonException exception)
        {
            DownloadError?.Invoke(this, new ErrorEventArgs(exception.Message));
        }

        // The Vala backend uses AND semantics for multiple search terms. The
        // RPC endpoint is queried once per term and results are intersected.
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 1)
        {
            var sets = new List<HashSet<string>>(terms.Length);
            foreach (var term in terms)
            {
                var matching = parsed.Where(info => Matches(info, term)).Select(info => info.Name);
                sets.Add(matching.ToHashSet(StringComparer.Ordinal));
            }
            var names = sets.Aggregate((left, right) => { left.IntersectWith(right); return left; });
            parsed = parsed.Where(info => names.Contains(info.Name)).ToList();
        }

        searchCache[query] = parsed;
        return parsed;
    }

    public void Dispose()
    {
        if (ownsHttpClient) httpClient.Dispose();
    }

    private static AURInfos ParseInfo(JsonElement json)
    {
        return new AURInfos
        {
            Name = String(json, "Name") ?? string.Empty,
            Version = String(json, "Version") ?? string.Empty,
            Description = String(json, "Description"),
            License = String(json, "License"),
            Url = String(json, "URL"),
            Groups = Array(json, "Groups"),
            Depends = Array(json, "Depends"),
            OptionalDependencies = Array(json, "OptDepends"),
            MakeDependencies = Array(json, "MakeDepends"),
            CheckDependencies = Array(json, "CheckDepends"),
            Provides = Array(json, "Provides"),
            Replaces = Array(json, "Replaces"),
            Conflicts = Array(json, "Conflicts"),
            PackageBase = String(json, "PackageBase"),
            Maintainer = String(json, "Maintainer"),
            Popularity = Number(json, "Popularity"),
            LastModified = UnixDate(json, "LastModified"),
            OutOfDate = UnixDate(json, "OutOfDate"),
            FirstSubmitted = UnixDate(json, "FirstSubmitted"),
            NumVotes = (ulong)Math.Max(0, Number(json, "NumVotes"))
        };
    }

    private static bool Matches(AURInfos info, string term)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        return info.Name.Contains(term, comparison) ||
               (info.Description?.Contains(term, comparison) ?? false) ||
               info.Provides.Any(value => value.Contains(term, comparison)) ||
               info.Groups.Any(value => value.Contains(term, comparison));
    }

    private static string? String(JsonElement json, string name)
        => json.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static double Number(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var stringNumber)) return stringNumber;
        return 0;
    }

    private static DateTimeOffset? UnixDate(JsonElement json, string name)
    {
        var number = Number(json, name);
        return number <= 0 ? null : DateTimeOffset.FromUnixTimeSeconds((long)number);
    }

    private static IReadOnlyList<string> Array(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return System.Array.Empty<string>();
        return value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!).ToArray();
    }

    private static void ValidatePackageName(string packageName)
    {
        if (packageName is "." or ".." || string.IsNullOrWhiteSpace(packageName) || packageName.Any(character => character is '/' or '\\' or ' '))
            throw new ArgumentException("Invalid AUR package name.", nameof(packageName));
    }
}

public sealed class ProgressEventArgs : EventArgs
{
    public ProgressEventArgs(string status, double progress) { Status = status; Progress = progress; }
    public string Status { get; }
    public double Progress { get; }
}

public sealed class ErrorEventArgs : EventArgs
{
    public ErrorEventArgs(string message) { Message = message; }
    public string Message { get; }
}
