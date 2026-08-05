using System.ComponentModel;
using System.Diagnostics;

namespace Pamac;

/// <summary>Result returned by a process started by the managed backend.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

internal static class ProcessRunner
{
    public static bool IsAvailable(string executable)
    {
        if (Path.IsPathRooted(executable))
            return File.Exists(executable);

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
                return true;
        }

        return false;
    }

    public static CommandResult Run(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
        => RunAsync(executable, arguments, workingDirectory, environment, cancellationToken)
            .GetAwaiter().GetResult();

    public static async Task<CommandResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        using var process = CreateProcess(executable, arguments, workingDirectory, environment);
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Unable to start {executable}.");
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException)
        {
            return new CommandResult(-1, string.Empty, exception.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new CommandResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    public static async Task<int> RunStreamingAsync(
        string executable,
        IEnumerable<string> arguments,
        Action<string>? standardOutput,
        Action<string>? standardError,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        using var process = CreateProcess(executable, arguments, workingDirectory, environment);
        try
        {
            if (!process.Start())
                return -1;
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException)
        {
            standardError?.Invoke(exception.Message);
            return -1;
        }

        var stdoutTask = ReadLinesAsync(process.StandardOutput, standardOutput, cancellationToken);
        var stderrTask = ReadLinesAsync(process.StandardError, standardError, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string>? callback, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            callback?.Invoke(line);
    }

    private static Process CreateProcess(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null)
                    startInfo.Environment.Remove(pair.Key);
                else
                    startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Best effort only. The cancellation has already been observed.
        }
    }
}
