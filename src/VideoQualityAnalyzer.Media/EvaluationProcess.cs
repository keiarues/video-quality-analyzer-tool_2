using System.Diagnostics;

namespace VideoQualityAnalyzer.Media;

internal static class EvaluationProcess
{
    public static Process Start(string executable, IReadOnlyList<string> arguments, string? directory, string failureMessage)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (directory is not null) info.WorkingDirectory = directory;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException(failureMessage);
    }

    public static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
    }
}
