using System.IO.Compression;
using System.Xml.Linq;

namespace Pamac;

public interface IAppstreamPlugin
{
    void Load(IEnumerable<string> repositoryNames);
    IReadOnlyList<AppInfo> Apps { get; }
    IReadOnlyList<AppInfo> Search(IEnumerable<string> searchTokens);
    IReadOnlyList<AppInfo> GetPackageNameApps(string packageName);
    IReadOnlyList<AppInfo> GetCategoryApps(string category);
}

/// <summary>AppStream catalog reader that uses the XML catalogs installed by pacman.</summary>
public class AppstreamClient : IAppstreamPlugin
{
    private readonly Dictionary<string, AppInfo> apps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AppInfo>> packageApps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AppInfo>> categoryApps = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AppInfo> Apps => apps.Values.ToArray();

    public void Load(IEnumerable<string> repositoryNames)
    {
        apps.Clear();
        packageApps.Clear();
        categoryApps.Clear();
        foreach (var repository in repositoryNames)
        {
            foreach (var path in FindCatalogs(repository))
                LoadCatalog(path, repository);
        }
    }

    public IReadOnlyList<AppInfo> Search(IEnumerable<string> searchTokens)
    {
        var tokens = searchTokens.Where(token => !string.IsNullOrWhiteSpace(token)).ToArray();
        if (tokens.Length == 0) return Array.Empty<AppInfo>();
        return apps.Values.Where(app => tokens.All(token => Matches(app, token))).ToArray();
    }

    public IReadOnlyList<AppInfo> GetPackageNameApps(string packageName)
        => packageApps.TryGetValue(packageName, out var values) ? values : Array.Empty<AppInfo>();

    public IReadOnlyList<AppInfo> GetCategoryApps(string category)
        => categoryApps.TryGetValue(category, out var values) ? values : Array.Empty<AppInfo>();

    public AppInfo? GetById(string id) => apps.TryGetValue(id, out var app) ? app : null;

    private void LoadCatalog(string path, string repository)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var input = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new GZipStream(file, CompressionMode.Decompress)
                : file;
            var document = XDocument.Load(input, LoadOptions.None);
            foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "component"))
            {
                var type = (string?)element.Attribute("type");
                if (type is not null && !type.Equals("desktop-application", StringComparison.OrdinalIgnoreCase) &&
                    !type.Equals("desktop", StringComparison.OrdinalIgnoreCase)) continue;

                var id = ChildText(element, "id");
                var packageName = ChildText(element, "pkgname") ?? ChildText(element, "pkg-name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(packageName)) continue;
                var app = new AppInfo
                {
                    Id = id,
                    PackageName = packageName,
                    Name = ChildText(element, "name"),
                    Summary = ChildText(element, "summary"),
                    LongDescription = ChildText(element, "description"),
                    Repository = repository,
                    Launchable = element.Descendants().FirstOrDefault(item => item.Name.LocalName == "launchable")?.Value.Trim(),
                    Icon = FindIcon(element, repository),
                    Screenshots = element.Descendants().Where(item => item.Name.LocalName == "image")
                        .Select(item => item.Value.Trim()).Where(value => value.Length > 0).Distinct().ToArray(),
                    Categories = element.Descendants().Where(item => item.Name.LocalName == "category")
                        .Select(item => item.Value.Trim()).Where(value => value.Length > 0).Distinct().ToArray()
                };
                if (!apps.ContainsKey(id))
                {
                    apps.Add(id, app);
                    packageApps.TryAdd(packageName, new List<AppInfo>());
                    packageApps[packageName].Add(app);
                    AddCategories(app);
                }
            }
        }
        catch (IOException)
        {
            // Catalogs are optional and may be removed while a package database
            // is being refreshed.
        }
        catch (InvalidDataException)
        {
            // Ignore a corrupt optional gzip catalog and keep other repositories.
        }
        catch (XmlException)
        {
            // Same policy for malformed third-party catalogs.
        }
    }

    private static IEnumerable<string> FindCatalogs(string repository)
    {
        var roots = new[]
        {
            "/usr/share/swcatalog/xml",
            "/usr/share/app-info/xml",
            "/var/cache/app-info/xml"
        };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                         .Where(path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xml.gz", StringComparison.OrdinalIgnoreCase)))
            {
                if (repository.Length == 0 || Path.GetFileName(path).Contains(repository, StringComparison.OrdinalIgnoreCase))
                    yield return path;
            }
        }
    }

    private void AddCategories(AppInfo app)
    {
        foreach (var category in app.Categories)
        {
            var normalized = category switch
            {
                "Photography" or "ImageProcessing" or "Graphics" or "Video" or "VectorGraphics" or "2DGraphics" or "3DGraphics" => "Photo & Video",
                "Audio" or "Music" or "Midi" or "Mixer" or "Multimedia" => "Music & Audio",
                "WebBrowser" or "Email" or "Office" or "Calculator" or "Calendar" or "Productivity" => "Productivity",
                "Network" or "Chat" or "News" or "Feed" or "Communication" or "InstantMessaging" => "Communication & News",
                "Education" or "Science" or "Astronomy" or "Chemistry" or "ComputerScience" or "Physics" => "Education & Science",
                "Games" or "Game" or "ActionGame" or "ArcadeGame" or "StrategyGame" => "Games",
                "Utility" or "Monitor" or "Accessibility" or "Archiving" or "Compression" => "Utilities",
                "Development" or "IDE" or "Debugger" => "Development",
                _ => null
            };
            if (normalized is null) continue;
            categoryApps.TryAdd(normalized, new List<AppInfo>());
            if (!categoryApps[normalized].Contains(app)) categoryApps[normalized].Add(app);
        }
    }

    private static bool Matches(AppInfo app, string token)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        return (app.Name?.Contains(token, comparison) ?? false) ||
               (app.Id?.Contains(token, comparison) ?? false) ||
               (app.PackageName?.Contains(token, comparison) ?? false) ||
               (app.Summary?.Contains(token, comparison) ?? false) ||
               (app.LongDescription?.Contains(token, comparison) ?? false);
    }

    private static string? ChildText(XElement parent, string name)
        => parent.Elements().FirstOrDefault(item => item.Name.LocalName == name)?.Value.Trim();

    private static string? FindIcon(XElement component, string repository)
    {
        var icon = component.Descendants().FirstOrDefault(item => item.Name.LocalName == "icon" &&
            string.Equals((string?)item.Attribute("type"), "cached", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        if (string.IsNullOrEmpty(icon)) return null;
        return $"/usr/share/swcatalog/icons/{repository}/64x64/{icon}";
    }
}
