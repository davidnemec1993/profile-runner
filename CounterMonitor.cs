using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ProfileRunner;

// Runs in its own console window. The app connects to the diagnostic port (DOTNET_DiagnosticPorts),
// then this process streams its EventCounters over EventPipe and redraws them every second.
static class CounterMonitor
{
    static readonly Dictionary<string, string> CounterArguments = new() { ["EventCounterIntervalSec"] = "1" };

    static readonly EventPipeProvider[] Providers =
    [
        new("System.Runtime", EventLevel.Informational, -1, CounterArguments),
        new("Microsoft.AspNetCore.Hosting", EventLevel.Informational, -1, CounterArguments),
    ];

    public static string NewPortName() => $"prun-{Guid.NewGuid():N}";

    // Opens a new console window running "prun --monitor"; on Windows a console app started through the shell gets its own window.
    public static void OpenWindow(string portName, Process app, string title) =>
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["--monitor", portName, app.Id.ToString(), title]) { UseShellExecute = true });

    public static async Task<int> RunAsync(string portName, int appProcessId, string title)
    {
        Console.Title = $"prun monitor · {title}";
        AnsiConsole.MarkupLine($":hourglass_not_done: [grey]Waiting for[/] [bold aqua]{Markup.Escape(title)}[/] [grey]to start...[/]");

        // Closes the window when the app exits before it connects, for example after a failed build.
        using var appExited = new CancellationTokenSource();
        _ = WaitForExitAsync(appProcessId, appExited);

        DiagnosticsClientConnector? connector;
        try
        {
            connector = await DiagnosticsClientConnector.FromDiagnosticPort(portName, appExited.Token);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }

        // Cancellation usually returns null rather than throwing.
        if (connector is null)
        {
            return 0;
        }

        await using (connector)
        {
            using var session = connector.Instance.StartEventPipeSession(Providers, requestRundown: false);
            using var source = new EventPipeEventSource(session.EventStream);
            var counters = new ConcurrentDictionary<string, Counter>();
            source.Dynamic.All += traceEvent =>
            {
                if (traceEvent.EventName == "EventCounters"
                    && traceEvent.PayloadValue(0) is IDictionary<string, object> outer
                    && outer["Payload"] is IDictionary<string, object> payload)
                {
                    var rate = (string)payload["CounterType"] == "Sum";
                    var value = Convert.ToDouble(payload[rate ? "Increment" : "Mean"]);
                    counters[$"{traceEvent.ProviderName}/{payload["Name"]}"] =
                        new Counter(traceEvent.ProviderName, (string)payload["DisplayName"], value, (string)payload["DisplayUnits"], rate);
                }
            };

            var processing = Task.Run(source.Process);
            // Crops the bottom when the window is too small, so the banner, title and header stay visible.
            await AnsiConsole.Live(Render(title, counters, running: true))
                .Overflow(VerticalOverflow.Ellipsis)
                .Cropping(VerticalOverflowCropping.Bottom)
                .StartAsync(async context =>
            {
                while (!processing.IsCompleted)
                {
                    await Task.WhenAny(processing, Task.Delay(TimeSpan.FromSeconds(1)));
                    context.UpdateTarget(Render(title, counters, running: !processing.IsCompleted));
                }
            });
        }

        AnsiConsole.MarkupLine("[grey]The application exited. Press any key to close this window.[/]");
        Console.ReadKey(intercept: true);
        return 0;
    }

    static async Task WaitForExitAsync(int processId, CancellationTokenSource exited)
    {
        try
        {
            await Process.GetProcessById(processId).WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            // Already exited.
        }

        exited.Cancel();
    }

    static IRenderable Render(string title, ConcurrentDictionary<string, Counter> counters, bool running)
    {
        var status = running ? "[green]● live[/]" : "[grey]○ exited[/]";
        // Two counters per row keep System.Runtime (about 30 counters) within a default-sized window.
        var table = new Table()
            .Title($":bar_chart: [bold aqua]{Markup.Escape(title)}[/] {status}")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[bold]Counter[/]")
            .AddColumn(new TableColumn("[bold]Value[/]").RightAligned())
            .AddColumn("[bold]Counter[/]")
            .AddColumn(new TableColumn("[bold]Value[/]").RightAligned());

        foreach (var group in counters.Values.GroupBy(counter => counter.Provider).OrderBy(group => group.Key))
        {
            table.AddRow($"[yellow]{Markup.Escape(group.Key)}[/]", "", "", "");
            foreach (var pair in group.OrderBy(counter => counter.DisplayName).Chunk(2))
            {
                table.AddRow(Name(pair[0]), Value(pair[0]), pair.Length > 1 ? Name(pair[1]) : "", pair.Length > 1 ? Value(pair[1]) : "");
            }
        }

        if (counters.IsEmpty)
        {
            table.AddRow("[grey]Waiting for the first values...[/]", "", "", "");
        }

        return Banner.Create("PROFILER") is { } banner ? new Rows(banner, table) : table;

        static string Name(Counter counter) => Markup.Escape(counter.DisplayName);

        static string Value(Counter counter)
        {
            var unit = counter.Rate ? $"{counter.Units}/s".TrimStart() : counter.Units;
            return $"[bold]{counter.Value:#,0.##}[/] [grey]{Markup.Escape(unit)}[/]";
        }
    }

    sealed record Counter(string Provider, string DisplayName, double Value, string Units, bool Rate);
}
