using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Romestead.MapWorkshop;

/// <summary>
/// Runs an external process and streams stdout/stderr lines to the sink.
/// Standard .NET async event pattern - safe because we're in C# (the PowerShell
/// version of this same code crashed because PS scriptblock event handlers
/// fire on threadpool threads outside any runspace).
/// </summary>
internal static class ProcessRunner
{
    public static async Task<int> RunAsync(
        string fileName,
        string? arguments,
        string? workingDirectory,
        IProgressSink sink,
        bool quietStdout = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? "",
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null && !quietStdout) sink.Log(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) sink.Log("! " + e.Data);
        };

        if (!proc.Start())
            throw new InvalidOperationException($"Could not start {fileName}");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(sink.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        // Ensure the async output/error reader handlers have fully drained.
        proc.WaitForExit();
        return proc.ExitCode;
    }

    /// <summary>
    /// Quote a single argument so it survives the Windows command line round-trip
    /// (handles spaces, quotes, parens).
    /// </summary>
    public static string Quote(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.IndexOfAny(new[] { ' ', '\t', '"', '(', ')' }) < 0) return s;
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
