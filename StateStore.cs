using System.Text.Json;

namespace ProfileRunner;

sealed class SolutionState
{
    public string? Project { get; set; }

    public string? Configuration { get; set; }

    public bool Profiling { get; set; }

    public bool SkipRestore { get; set; }

    public Dictionary<string, string> Profiles { get; set; } = [];
}

static class StateStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProfileRunner", "state.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static SolutionState Load(string solutionPath) => ReadAll().GetValueOrDefault(solutionPath) ?? new SolutionState();

    public static void Save(string solutionPath, SolutionState state)
    {
        var all = ReadAll();
        all[solutionPath] = state;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, JsonOptions));
    }

    static Dictionary<string, SolutionState> ReadAll()
    {
        try
        {
            var all = JsonSerializer.Deserialize<Dictionary<string, SolutionState>>(File.ReadAllText(FilePath));
            return new Dictionary<string, SolutionState>(all ?? new Dictionary<string, SolutionState>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new Dictionary<string, SolutionState>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
