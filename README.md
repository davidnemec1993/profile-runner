# ProfileRunner (`prun`)

A console tool for selecting a C# solution's startup project, launch profile, and configuration,
similar to Visual Studio's **Start Without Debugging**. It runs `dotnet run` without a debugger.

## Requirements

- .NET 10
- A C# solution in `.sln` or `.slnx` format

## Install

From the repository directory, pack and install the tool:

```sh
dotnet pack -c Release
dotnet tool install --global ProfileRunner --add-source ./nupkg
```

After changing the tool, increment `<Version>` in `ProfileRunner.csproj`, then pack and update:

```sh
dotnet pack -c Release
dotnet tool update --global ProfileRunner --add-source ./nupkg
```

## Usage

```sh
prun                                              # menu for solution in current directory
prun path/to/solution.sln                         # choose a solution file
prun path/to/solution-directory                   # choose a solution directory
prun --list                                       # list runnable projects and configurations
prun --project ProjectName --launch-profile "Profile Name" --configuration Release
```

Direct-run options also have short forms: `-p` for `--project`, `-lp` for `--launch-profile`,
and `-c` for `--configuration`. Launch profile and configuration options require a project.
If omitted, the tool uses the last selection or its default. Use `prun --help` for usage details.

In the menu, **Start without debugging** runs the selection. Ctrl+C stops the application gracefully;
a second Ctrl+C kills the process tree. After the application exits, press any key to return to the menu.

## Behavior

- Reads `.sln` and `.slnx` files. Lists runnable projects with an `Exe`/`WinExe` output type, the
  Web, Worker, or Aspire AppHost SDK, or a `Properties/launchSettings.json`. Test projects are left out.
- Offers `Project` launch profiles from `launchSettings.json`. IIS Express, Docker, and Executable
  profiles cannot run through `dotnet run`. Environment variables, `applicationUrl`, and
  `commandLineArgs` are applied by `dotnet run --launch-profile`.
- Maps each solution configuration to the project's configuration (`ActiveCfg` in `.sln`,
  `<BuildType>` in `.slnx`).
- Opens the browser for `launchBrowser: true` once the application URL accepts connections.
- Runs the application with the project directory as its working directory.
- Remembers the last project, profile per project, and configuration per solution in
  `LocalApplicationData/ProfileRunner/state.json` (OS-specific local app-data directory). With no
  saved selection, it uses the profile last selected in Visual Studio (`ActiveDebugProfile` in
  `<project>.csproj.user`) or the first available profile.

## Limitations

- Runs one startup project at a time. Start another `prun` instance in a separate terminal to run more.
- Does not support debugging, hot reload (`dotnet watch`), IIS Express, or Docker profiles.
