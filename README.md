# ProfileRunner (`prun`)

A console stand-in for Visual Studio's startup project / launch profile / configuration dropdowns
and **Start Without Debugging** (Ctrl+F5). It runs `dotnet run`, so there is no debugger.

## Install

```powershell
dotnet pack -c Release
dotnet tool install --global ProfileRunner --add-source ./nupkg
# after a change: bump <Version> in ProfileRunner.csproj, pack, then
dotnet tool update --global ProfileRunner --add-source ./nupkg
```

## Usage

```powershell
prun                                   # menu for the solution in the current directory
prun C:\repos\MTF_TOM_IGS\TradeOrderManagement.sln
prun --list                            # runnable projects, profiles, configurations
prun -p TOM.AppHost -lp "http (with DEX mock)" -c MTF   # run directly, no menu
```

In the menu, **Start without debugging** runs the selection. Ctrl+C stops the application
gracefully (it gets the signal like in a VS console window), a second Ctrl+C kills the process tree.
After the application exits, any key returns to the menu.

## What it does like Visual Studio

- Reads `.sln` and `.slnx`. Lists projects that can run: an `Exe`/`WinExe` output type, the Web,
  Worker or Aspire AppHost SDK, or a `Properties/launchSettings.json`. Test projects are left out.
- Offers the `Project` launch profiles from `launchSettings.json` (IIS Express, Docker and
  Executable profiles cannot be run by `dotnet run`). Environment variables, `applicationUrl`
  and `commandLineArgs` are applied by `dotnet run --launch-profile`.
- Maps the solution configuration to each project's configuration (`ActiveCfg` in `.sln`,
  `<BuildType>` in `.slnx`), e.g. solution `MTF` runs `TOM.AppHost` as `Debug`.
- Opens the browser for `launchBrowser: true` once the application URL accepts connections.
- Runs the application with the project directory as the working directory.
- Remembers the last project, the profile per project and the configuration per solution in
  `%LOCALAPPDATA%\ProfileRunner\state.json`. With nothing remembered it starts from the profile
  last selected in Visual Studio (`ActiveDebugProfile` in `<project>.csproj.user`).

## Not supported

- More than one startup project at a time (start another `prun` in a second terminal).
- Debugging, hot reload (`dotnet watch`), IIS Express, Docker profiles.
