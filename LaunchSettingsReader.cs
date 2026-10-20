using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ProfileRunner;

sealed record LaunchProfile(string Name, string? ApplicationUrl, bool LaunchBrowser, string? LaunchUrl)
{
    public string? FirstApplicationUrl =>
        ApplicationUrl?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    public Uri? BrowserUrl
    {
        get
        {
            if (!LaunchBrowser)
            {
                return null;
            }

            if (Uri.TryCreate(LaunchUrl, UriKind.Absolute, out var absolute))
            {
                return absolute;
            }

            var url = string.IsNullOrEmpty(LaunchUrl) ? FirstApplicationUrl : $"{FirstApplicationUrl?.TrimEnd('/')}/{LaunchUrl.TrimStart('/')}";
            return Uri.TryCreate(url, UriKind.Absolute, out var combined) ? combined : null;
        }
    }
}

static class LaunchSettingsReader
{
    static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string PathFor(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(projectPath)!, "Properties", "launchSettings.json");

    // Only "Project" profiles: dotnet run cannot start IISExpress, Docker or Executable profiles.
    public static IReadOnlyList<LaunchProfile> ReadProfiles(string projectPath)
    {
        var path = PathFor(projectPath);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), JsonOptions);
            if (!document.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return profiles.EnumerateObject()
                .Where(profile => profile.Value.ValueKind == JsonValueKind.Object && ReadString(profile.Value, "commandName") == "Project")
                .Select(profile => new LaunchProfile(
                    profile.Name,
                    ReadString(profile.Value, "applicationUrl"),
                    profile.Value.TryGetProperty("launchBrowser", out var launchBrowser) && launchBrowser.ValueKind == JsonValueKind.True,
                    ReadString(profile.Value, "launchUrl")))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // The profile last selected in Visual Studio.
    public static string? ReadActiveDebugProfile(string projectPath)
    {
        var userFile = projectPath + ".user";
        if (!File.Exists(userFile))
        {
            return null;
        }

        try
        {
            return XDocument.Load(userFile).Descendants().FirstOrDefault(element => element.Name.LocalName == "ActiveDebugProfile")?.Value;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
