using ProfileRunner;
using Spectre.Console;

// UTF-8 so Spectre.Console draws Unicode borders and icons instead of ASCII fallbacks.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Internal: the profiling window that Runner opens next to the running application.
if (args is ["--monitor", var monitorPort, var appProcessId, var monitorTitle])
{
    return await CounterMonitor.RunAsync(monitorPort, int.Parse(appProcessId), monitorTitle);
}

string? target = null;
string? projectName = null;
string? profileName = null;
string? configurationName = null;
var listOnly = false;

for (var i = 0; i < args.Length; i++)
{
    var argument = args[i];
    switch (argument)
    {
        case "-h" or "--help":
            PrintUsage();
            return 0;
        case "--list":
            listOnly = true;
            continue;
        case "-p" or "--project" or "-lp" or "--launch-profile" or "-c" or "--configuration":
            if (++i >= args.Length)
            {
                return Fail($"Missing value for {argument}.");
            }

            if (argument is "-p" or "--project")
            {
                projectName = args[i];
            }
            else if (argument is "-c" or "--configuration")
            {
                configurationName = args[i];
            }
            else
            {
                profileName = args[i];
            }

            continue;
    }

    if (target is not null || argument.StartsWith('-'))
    {
        return Fail($"Unknown argument '{argument}'. Use --help.");
    }

    target = argument;
}

if (projectName is null && (profileName ?? configurationName) is not null)
{
    return Fail("--launch-profile and --configuration require --project.");
}

var interactive = AnsiConsole.Profile.Capabilities.Interactive;
var solutionPath = ResolveSolution(Path.GetFullPath(target ?? Directory.GetCurrentDirectory()), interactive);
if (solutionPath is null)
{
    return 1;
}

var solution = SolutionReader.Read(solutionPath);
if (solution.Projects.Count == 0)
{
    return Fail($"No runnable C# project found in {solution.Name}.");
}

if (listOnly)
{
    PrintList(solution);
    return 0;
}

var state = StateStore.Load(solution.Path);

if (projectName is not null)
{
    var project = FindProject(solution, projectName);
    if (project is null)
    {
        return Fail($"Project '{projectName}' not found. Runnable projects: {string.Join(", ", solution.Projects.Select(p => p.Name))}.");
    }

    var profile = profileName is null ? DefaultProfile(project, state) : FindProfile(project, profileName);
    if (profileName is not null && profile is null)
    {
        return Fail($"Launch profile '{profileName}' not found in {project.Name}. Profiles: {string.Join(", ", project.Profiles.Select(p => p.Name))}.");
    }

    return await Runner.RunAsync(project, profile, configurationName ?? DefaultConfiguration(solution, state));
}

if (!interactive)
{
    return Fail("The terminal is not interactive. Use --project to run directly or --list to show the projects.");
}

return await RunMenuAsync(solution, state);

static async Task<int> RunMenuAsync(SolutionInfo solution, SolutionState state)
{
    var project = FindProject(solution, state.Project) ?? solution.Projects.FirstOrDefault(p => p.Profiles.Count > 0) ?? solution.Projects[0];
    var profile = DefaultProfile(project, state);
    var configuration = DefaultConfiguration(solution, state);

    while (true)
    {
        // Re-read on every pass so edits to launchSettings.json or the solution show up, as in VS.
        solution = SolutionReader.Read(solution.Path);
        project = FindProject(solution, project.Name) ?? solution.Projects[0];
        profile = FindProfile(project, profile?.Name) ?? DefaultProfile(project, state);

        AnsiConsole.Clear();
        RenderSelection(solution, project, profile, configuration, state.Profiling);

        // The profiling window is opened through the Windows shell.
        var actions = Enum.GetValues<MenuAction>().Where(action =>
            (action != MenuAction.SelectProfile || project.Profiles.Count > 0)
            && (action != MenuAction.ToggleProfiling || OperatingSystem.IsWindows()));
        switch (AnsiConsole.Prompt(Menu<MenuAction>("[grey]What next?[/]").UseConverter(action => Label(action, state.Profiling)).AddChoices(actions)))
        {
            case MenuAction.Start:
                Remember();
                AnsiConsole.Clear();
                var exitCode = await Runner.RunAsync(project, profile, configuration, state.Profiling && OperatingSystem.IsWindows());
                AnsiConsole.WriteLine();
                var status = exitCode == 0 ? "[green]:check_mark_button: Exited with code 0[/]" : $"[red]:cross_mark: Exited with code {exitCode}[/]";
                AnsiConsole.Write(new Rule(status).LeftJustified().RuleStyle("grey"));
                AnsiConsole.MarkupLine("[grey]Press any key to return to the menu.[/]");
                while (Console.KeyAvailable)
                {
                    Console.ReadKey(intercept: true);
                }

                Console.ReadKey(intercept: true);
                break;

            case MenuAction.SelectProject:
                var current = project;
                project = AnsiConsole.Prompt(Menu<ProjectInfo>(":package: Startup project")
                    .PageSize(20)
                    .EnableSearch()
                    .UseConverter(p => $"{Marker(p.Name == current.Name)}{Markup.Escape(p.Name)} [grey]{p.Profiles.Count} profile(s)[/]")
                    .AddChoices(solution.Projects));
                profile = DefaultProfile(project, state);
                break;

            case MenuAction.SelectProfile:
                var currentProfile = profile;
                profile = AnsiConsole.Prompt(Menu<LaunchProfile>($":compass: Launch profile of [aqua]{Markup.Escape(project.Name)}[/]")
                    .PageSize(20)
                    .UseConverter(p => $"{Marker(p.Name == currentProfile?.Name)}{Markup.Escape(p.Name)} [grey]{Markup.Escape(p.FirstApplicationUrl ?? "")}[/]")
                    .AddChoices(project.Profiles));
                break;

            case MenuAction.SelectConfiguration:
                var currentConfiguration = configuration;
                configuration = AnsiConsole.Prompt(Menu<string>(":wrench: Solution configuration")
                    .UseConverter(c => $"{Marker(c == currentConfiguration)}{Markup.Escape(c)}")
                    .AddChoices(solution.Configurations));
                break;

            case MenuAction.ToggleProfiling:
                state.Profiling = !state.Profiling;
                break;

            case MenuAction.Exit:
                return 0;
        }

        Remember();
    }

    void Remember()
    {
        state.Project = project.Name;
        state.Configuration = configuration;
        if (profile is not null)
        {
            state.Profiles[project.Name] = profile.Name;
        }

        StateStore.Save(solution.Path, state);
    }
}

static void RenderSelection(SolutionInfo solution, ProjectInfo project, LaunchProfile? profile, string configuration, bool profiling)
{
    AnsiConsole.Write(Banner.Create("PRUN"));
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule(":rocket: [bold aqua]ProfileRunner[/]").LeftJustified().RuleStyle("grey"));
    AnsiConsole.WriteLine();

    var projectConfiguration = project.ProjectConfiguration(configuration);
    var grid = new Grid().AddColumn(new GridColumn().PadRight(3)).AddColumn();
    grid.AddRow(":package: [grey]Startup project[/]", $"[bold aqua]{Markup.Escape(project.Name)}[/]");
    grid.AddRow(":compass: [grey]Launch profile[/]", profile is null ? "[grey](none)[/]" : $"[yellow]{Markup.Escape(profile.Name)}[/]");
    grid.AddRow(":wrench: [grey]Configuration[/]", projectConfiguration == configuration
        ? $"[magenta]{Markup.Escape(configuration)}[/]"
        : $"[magenta]{Markup.Escape(configuration)}[/] [grey](project: {Markup.Escape(projectConfiguration)})[/]");
    if (profile?.FirstApplicationUrl is { } url)
    {
        grid.AddRow(":globe_with_meridians: [grey]URL[/]", $"[link]{Markup.Escape(url)}[/]");
    }

    if (OperatingSystem.IsWindows())
    {
        grid.AddRow(":bar_chart: [grey]Profiling[/]", profiling ? "[green]On[/] [grey](counters in a separate window)[/]" : "[grey]Off[/]");
    }

    AnsiConsole.Write(new Panel(grid)
        .Header($" :file_folder: [bold]{Markup.Escape(solution.Name)}[/] ")
        .RoundedBorder()
        .BorderColor(Color.Grey)
        .Padding(2, 1));
    AnsiConsole.WriteLine();
}

static void PrintList(SolutionInfo solution)
{
    var solutionDirectory = Path.GetDirectoryName(solution.Path)!;
    var table = new Table()
        .Title($":file_folder: [bold]{Markup.Escape(solution.Name)}[/]")
        .Border(TableBorder.Rounded)
        .BorderColor(Color.Grey)
        .AddColumns(":package: [bold]Project[/]", ":compass: [bold]Launch profiles[/]", "[bold]Path[/]");
    foreach (var project in solution.Projects)
    {
        table.AddRow(
            $"[aqua]{Markup.Escape(project.Name)}[/]",
            $"[yellow]{Markup.Escape(string.Join(", ", project.Profiles.Select(p => p.Name)))}[/]",
            $"[grey]{Markup.Escape(Path.GetRelativePath(solutionDirectory, project.Path))}[/]");
    }

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine($":wrench: [grey]Configurations:[/] [magenta]{Markup.Escape(string.Join(", ", solution.Configurations))}[/]");
}

static string? ResolveSolution(string target, bool interactive)
{
    if (File.Exists(target))
    {
        return target;
    }

    if (!Directory.Exists(target))
    {
        Fail($"'{target}' is neither a solution file nor a directory.");
        return null;
    }

    // Filtered by hand: on Windows the pattern "*.sln" also matches ".slnx".
    var solutions = Directory.EnumerateFiles(target)
        .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".sln" or ".slnx")
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    switch (solutions.Count)
    {
        case 0:
            Fail($"No .sln or .slnx file in '{target}'.");
            return null;
        case 1:
            return solutions[0];
        case > 1 when interactive:
            return AnsiConsole.Prompt(Menu<string>(":file_folder: Solution")
                .UseConverter(file => Markup.Escape(Path.GetFileName(file)))
                .AddChoices(solutions));
        default:
            Fail($"More than one solution in '{target}', pass one: {string.Join(", ", solutions.Select(Path.GetFileName))}.");
            return null;
    }
}

static ProjectInfo? FindProject(SolutionInfo solution, string? name) =>
    solution.Projects.FirstOrDefault(project => string.Equals(project.Name, name, StringComparison.OrdinalIgnoreCase));

static LaunchProfile? FindProfile(ProjectInfo project, string? name) =>
    project.Profiles.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));

static LaunchProfile? DefaultProfile(ProjectInfo project, SolutionState state) =>
    FindProfile(project, state.Profiles.GetValueOrDefault(project.Name))
    ?? FindProfile(project, project.ActiveDebugProfile)
    ?? project.Profiles.FirstOrDefault();

static string DefaultConfiguration(SolutionInfo solution, SolutionState state) =>
    solution.Configurations.FirstOrDefault(c => string.Equals(c, state.Configuration, StringComparison.OrdinalIgnoreCase))
    ?? solution.Configurations.FirstOrDefault(c => c == "Debug")
    ?? solution.Configurations[0];

static SelectionPrompt<T> Menu<T>(string title) where T : notnull =>
    new SelectionPrompt<T>().Title(title).HighlightStyle(new Style(Color.Aqua, decoration: Decoration.Bold));

static string Marker(bool selected) => selected ? "[green]●[/] " : "[grey]○[/] ";

static string Label(MenuAction action, bool profiling) => action switch
{
    MenuAction.Start => ":rocket: [green]Start without debugging[/]",
    MenuAction.SelectProject => ":package: Change startup project",
    MenuAction.SelectProfile => ":compass: Change launch profile",
    MenuAction.SelectConfiguration => ":wrench: Change configuration",
    MenuAction.ToggleProfiling => profiling ? ":bar_chart: Disable profiling" : ":bar_chart: Enable profiling",
    _ => ":door: Exit",
};

static int Fail(string message)
{
    AnsiConsole.MarkupLine($"[red]:cross_mark: {Markup.Escape(message)}[/]");
    return 1;
}

static void PrintUsage() => Console.WriteLine("""
    Selects a startup project and launch profile of a C# solution and runs it without debugging.

    Usage:
      prun [solution|directory]                     interactive menu (default: current directory)
      prun [solution|directory] --list              list runnable projects, profiles and configurations
      prun [solution|directory] -p <project> [-lp <launch profile>] [-c <configuration>]
                                                    run directly; missing values = last selection

    Ctrl+C stops the running application, a second Ctrl+C kills it.
    """);

enum MenuAction
{
    Start,
    SelectProject,
    SelectProfile,
    SelectConfiguration,
    ToggleProfiling,
    Exit,
}
