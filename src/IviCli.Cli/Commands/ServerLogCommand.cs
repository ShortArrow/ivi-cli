using System.CommandLine;
using IviCli.Cli.Paths;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires <c>ivicli server log</c> — tails the per-server structured log
/// file produced by the gateway (ADR 0027 §5). Read-only; no IPC with
/// any running gateway is involved.
/// </summary>
public static class ServerLogCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build()
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "Server alias whose log file should be tailed.",
        };
        var followOpt = new Option<bool>("--follow", "-f")
        {
            Description = "Keep reading appended lines until cancelled (default: dump and exit).",
        };
        var tailOpt = new Option<int>("--tail")
        {
            Description = "Show the last N lines before following (default 50).",
            DefaultValueFactory = _ => 50,
        };

        var command = new Command("log", "Tail the structured log file for a gateway server.");
        command.Arguments.Add(nameArg);
        command.Options.Add(followOpt);
        command.Options.Add(tailOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var follow = parseResult.GetValue(followOpt);
                var tail = parseResult.GetValue(tailOpt);

                var dir = IviPaths.ResolveLogDirectory();
                // Serilog rolling file uses ivi-cli-<date>.log; per-server log
                // files follow the same pattern with the server name prefix.
                var pattern = $"{name}-*.log";
                var matching = Directory.EnumerateFiles(dir, pattern).OrderBy(f => f).ToList();
                if (matching.Count == 0)
                {
                    // Fall back to the global log if no per-server file exists.
                    var fallback = Path.Combine(dir, $"ivicli-server-{name}.log");
                    if (File.Exists(fallback))
                    {
                        matching = new List<string> { fallback };
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            $"error: no log file for server '{name}' under {dir}."
                        );
                        return ExitCodeMapper.UsageError;
                    }
                }
                var path = matching[^1];

                await TailAsync(path, tail, follow, ct);
                return ExitCodeMapper.Success;
            }
        );

        return command;
    }

    private static async Task TailAsync(string path, int tail, bool follow, CancellationToken ct)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        // Show the tail-window first.
        if (tail > 0)
        {
            stream.Seek(0, SeekOrigin.Begin);
            using var dump = new StreamReader(stream, leaveOpen: true);
            var buffered = new Queue<string>(tail);
            string? line;
            while ((line = await dump.ReadLineAsync(ct)) is not null)
            {
                if (buffered.Count == tail)
                {
                    buffered.Dequeue();
                }
                buffered.Enqueue(line);
            }
            foreach (var l in buffered)
            {
                Console.WriteLine(l);
            }
        }

        if (!follow)
        {
            return;
        }

        // Now follow appended lines until cancellation.
        stream.Seek(0, SeekOrigin.End);
        using var follower = new StreamReader(stream);
        while (!ct.IsCancellationRequested)
        {
            var line = await follower.ReadLineAsync(ct);
            if (line is null)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }
            Console.WriteLine(line);
        }
    }
}
