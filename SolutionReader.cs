using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ProfileRunner;

sealed record SolutionInfo(string Path, IReadOnlyList<ProjectInfo> Projects, IReadOnlyList<string> Configurations)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

sealed record ProjectInfo(
    string Name,
    string Path,
    IReadOnlyList<LaunchProfile> Profiles,
    string? ActiveDebugProfile,
    IReadOnlyDictionary<string, string> ConfigurationMap)
{
    public string Directory => System.IO.Path.GetDirectoryName(Path)!;

    public string ProjectConfiguration(string solutionConfiguration) =>
        ConfigurationMap.GetValueOrDefault(solutionConfiguration) ?? ConfigurationMap.GetValueOrDefault("*") ?? solutionConfiguration;
}

static partial class SolutionReader
{
    const string WildcardConfiguration = "*";

    public static SolutionInfo Read(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var (entries, configurations) = solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ReadSlnx(solutionPath)
            : ReadSln(solutionPath);

        var projects = entries
            .Select(entry => entry with { Path = Path.GetFullPath(Path.Combine(solutionDirectory, entry.Path.Replace('\\', Path.DirectorySeparatorChar))) })
            .Where(entry => File.Exists(entry.Path) && IsRunnable(entry.Path))
            .Select(entry => new ProjectInfo(
                Path.GetFileNameWithoutExtension(entry.Path),
                entry.Path,
                LaunchSettingsReader.ReadProfiles(entry.Path),
                LaunchSettingsReader.ReadActiveDebugProfile(entry.Path),
                entry.ConfigurationMap))
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SolutionInfo(solutionPath, projects, configurations.Count > 0 ? configurations : ["Debug", "Release"]);
    }

    static (List<ProjectEntry> Projects, List<string> Configurations) ReadSln(string path)
    {
        var text = File.ReadAllText(path);

        var configurationMaps = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ActiveConfigurationLine().Matches(text))
        {
            var projectGuid = match.Groups["guid"].Value;
            if (!configurationMaps.TryGetValue(projectGuid, out var map))
            {
                configurationMaps[projectGuid] = map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var solutionConfiguration = match.Groups["solution"].Value;
            if (!map.ContainsKey(solutionConfiguration) || match.Groups["platform"].Value == "Any CPU")
            {
                map[solutionConfiguration] = match.Groups["project"].Value.Trim();
            }
        }

        var projects = ProjectLine().Matches(text)
            .Where(match => match.Groups["path"].Value.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(match => new ProjectEntry(
                match.Groups["path"].Value,
                configurationMaps.GetValueOrDefault(match.Groups["guid"].Value) ?? new Dictionary<string, string>()))
            .ToList();

        var configurations = SolutionConfigurationLine().Matches(SolutionConfigurationSection().Match(text).Value)
            .Select(match => match.Groups["name"].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (projects, configurations);
    }

    static (List<ProjectEntry> Projects, List<string> Configurations) ReadSlnx(string path)
    {
        var document = XDocument.Load(path);

        var projects = document.Descendants("Project")
            .Select(project => (Element: project, Path: (string?)project.Attribute("Path")))
            .Where(project => project.Path?.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) == true)
            .Select(project => new ProjectEntry(project.Path!, project.Element.Elements("BuildType")
                .Where(buildType => buildType.Attribute("Project") is not null)
                .GroupBy(buildType => ((string?)buildType.Attribute("Solution") ?? WildcardConfiguration).Split('|')[0], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => (string)group.First().Attribute("Project")!, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        var configurations = document.Root?.Element("Configurations")?.Elements("BuildType")
            .Select(buildType => (string?)buildType.Attribute("Name"))
            .OfType<string>()
            .ToList() ?? [];

        return (projects, configurations);
    }

    // Heuristic instead of evaluating MSBuild per project (seconds each on a large solution).
    static bool IsRunnable(string projectPath)
    {
        var content = File.ReadAllText(projectPath);
        if (content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) || TestProjectProperty().IsMatch(content))
        {
            return false;
        }

        return File.Exists(LaunchSettingsReader.PathFor(projectPath))
            || ExecutableOutputType().IsMatch(content)
            || RunnableSdk().IsMatch(content);
    }

    sealed record ProjectEntry(string Path, IReadOnlyDictionary<string, string> ConfigurationMap);

    [GeneratedRegex(@"^Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*""\s*,\s*""(?<path>[^""]+)""\s*,\s*""(?<guid>\{[^}]+\})""", RegexOptions.Multiline)]
    private static partial Regex ProjectLine();

    [GeneratedRegex(@"^\s*(?<guid>\{[0-9A-Fa-f-]+\})\.(?<solution>[^|\r\n]+)\|(?<platform>[^.\r\n]+)\.ActiveCfg\s*=\s*(?<project>[^|\r\n]+)", RegexOptions.Multiline)]
    private static partial Regex ActiveConfigurationLine();

    [GeneratedRegex(@"GlobalSection\(SolutionConfigurationPlatforms\).*?EndGlobalSection", RegexOptions.Singleline)]
    private static partial Regex SolutionConfigurationSection();

    [GeneratedRegex(@"^\s*(?<name>[^|\r\n=]+)\|[^=\r\n]+=", RegexOptions.Multiline)]
    private static partial Regex SolutionConfigurationLine();

    [GeneratedRegex(@"<IsTestProject>\s*true\s*</IsTestProject>", RegexOptions.IgnoreCase)]
    private static partial Regex TestProjectProperty();

    [GeneratedRegex(@"<OutputType>\s*(Win)?Exe\s*</OutputType>", RegexOptions.IgnoreCase)]
    private static partial Regex ExecutableOutputType();

    [GeneratedRegex(@"Sdk=""(Microsoft\.NET\.Sdk\.(Web|Worker)|Aspire\.AppHost\.Sdk)", RegexOptions.IgnoreCase)]
    private static partial Regex RunnableSdk();
}
