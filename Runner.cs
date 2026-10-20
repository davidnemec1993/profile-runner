using System.Diagnostics;
using System.Net.Sockets;
using Spectre.Console;

namespace ProfileRunner;

static class Runner
{
    // The child shares this console, so Ctrl+C reaches it directly and it shuts down gracefully;
    // this process only ignores the first Ctrl+C and kills the process tree on the second.
    public static async Task<int> RunAsync(ProjectInfo project, LaunchProfile? profile, string solutionConfiguration)
    {
        var startInfo = new ProcessStartInfo("dotnet") { WorkingDirectory = project.Directory };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project.Path);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(project.ProjectConfiguration(solutionConfiguration));
        if (profile is not null)
        {
            startInfo.ArgumentList.Add("--launch-profile");
            startInfo.ArgumentList.Add(profile.Name);
        }

        AnsiConsole.MarkupLine($"[grey]> dotnet {Markup.Escape(string.Join(' ', startInfo.ArgumentList.Select(Quote)))}[/]");
        AnsiConsole.MarkupLine("[grey]Ctrl+C stops the application, a second Ctrl+C kills it.[/]");
        AnsiConsole.WriteLine();

        using var process = Process.Start(startInfo)!;
        var stopRequests = 0;

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            if (Interlocked.Increment(ref stopRequests) > 1 && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            if (profile?.BrowserUrl is { } browserUrl)
            {
                _ = OpenBrowserWhenListeningAsync(browserUrl, process);
            }

            await process.WaitForExitAsync();
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }

        return process.ExitCode;
    }

    static async Task OpenBrowserWhenListeningAsync(Uri url, Process process)
    {
        while (!process.HasExited)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(url.Host, url.Port);
                Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    static string Quote(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;
}
