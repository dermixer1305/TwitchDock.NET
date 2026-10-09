using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TwitchSdk.IntegrationTests;

/// <summary>Locates and runs the Twitch CLI (https://dev.twitch.tv/docs/cli/).</summary>
internal static class TwitchCli
{
    public static string? Executable { get; } = Locate();

    private static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable("TWITCH_CLI");
        if (!string.IsNullOrEmpty(configured)) return File.Exists(configured) ? configured : null;
        var name = OperatingSystem.IsWindows() ? "twitch.exe" : "twitch";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        // winget installs the CLI into its package folder and only adds an alias to PATH for new shells.
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        return Directory.Exists(winget)
            ? Directory.EnumerateDirectories(winget, "Twitch.TwitchCLI*").SelectMany(d => Directory.EnumerateFiles(d, name, SearchOption.AllDirectories)).FirstOrDefault()
            : null;
    }

    public static async Task<(int ExitCode, string Output)> RunAsync(TimeSpan timeout, params string[] arguments)
    {
        using var process = Process.Start(Info(arguments)) ?? throw new InvalidOperationException("The Twitch CLI did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"twitch {string.Join(' ', arguments)} did not finish within {timeout}.");
        }
        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>Starts a long-running CLI server; dispose to stop it.</summary>
    public static CliServer StartServer(params string[] arguments) => new(Process.Start(Info(arguments)) ?? throw new InvalidOperationException("The Twitch CLI did not start."));

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Serializes CLI use across test processes (both target frameworks run in parallel): the CLI keeps one event database
    /// and routes WebSocket triggers to the most recently started mock server.
    /// </summary>
    public static async Task<IDisposable> LockAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "twitchsdk-twitch-cli.lock");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) { await Task.Delay(250, deadline.Token); }
        }
    }

    private static ProcessStartInfo Info(string[] arguments)
    {
        var info = new ProcessStartInfo(Executable ?? throw new InvalidOperationException("The Twitch CLI is not installed."))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }
}

internal sealed class CliServer : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();

    public CliServer(Process process)
    {
        _process = process;
        _process.OutputDataReceived += (_, e) => { lock (_output) _output.AppendLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { lock (_output) _output.AppendLine(e.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public string Output { get { lock (_output) return _output.ToString(); } }

    public void Dispose()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        _process.WaitForExit(10_000);
        _process.Dispose();
    }
}

/// <summary>Skips the test when the Twitch CLI is unavailable.</summary>
internal sealed class TwitchCliFactAttribute : FactAttribute
{
    public TwitchCliFactAttribute()
    {
        if (TwitchCli.Executable is null) Skip = "The Twitch CLI is not installed; set TWITCH_CLI to run the integration tests.";
    }
}
