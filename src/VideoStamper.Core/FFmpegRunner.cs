using System.Diagnostics;
using System.Text;

namespace VideoStamper.Core;

public static class FfmpegRunner
{
    public static async Task<string> RunFfprobeAsync(
        string inputPath,
        CancellationToken cancellationToken = default
    )
    {
        var ffprobe = FfmpegLocator.GetFfprobePath();

        var psi = new ProcessStartInfo
        {
            FileName = ffprobe,
            ArgumentList =
            {
                "-v", Globals.DEBUG > 2 ? "info" : "quiet",
                "-print_format", "json",
                "-select_streams", "v:0",
                "-show_entries", "stream",
                "-show_entries", "format_tags",
                inputPath
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // Debug prints only if DEBUG >= 3
        if (Globals.DEBUG > 2)
        {
            string cmd = FormatCommand(ffprobe, psi.ArgumentList);
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: FFprobe command:\n{cmd}\n");
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffprobe");

        // If DEBUG < 3, suppress stderr entirely
        string error = Globals.DEBUG > 2
            ? await proc.StandardError.ReadToEndAsync(cancellationToken)
            : await DiscardStreamAsync(proc.StandardError, cancellationToken);

        // Always read stdout (we need the JSON)
        string output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);

        await proc.WaitForExitAsync(cancellationToken);

        if (proc.ExitCode != 0)
        {
            throw new Exception($"ffprobe failed: {error}");
        }

        return output;
    }

    public static async Task RunFfmpegAsync(
        IEnumerable<string> arguments,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = FfmpegLocator.GetFfmpegPath();

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        // Debug-only command echo
        if (Globals.DEBUG > 2)
        {
            string cmd = FormatCommand(ffmpeg, psi.ArgumentList);
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: FFmpeg command:\n{cmd}\n");
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");

        if (Globals.DEBUG > 2)
        {
            // Normal debug mode: report stderr to progress logger
            while (!proc.HasExited)
            {
                var line = await proc.StandardError.ReadLineAsync();
                if (line == null) break;
                log?.Report(line);
            }
        }
        else
        {
            // Silent mode: fully discard stderr so ffmpeg does not block
            _ = Task.Run(() => DiscardStreamAsync(proc.StandardError, cancellationToken));
            _ = Task.Run(() => DiscardStreamAsync(proc.StandardOutput, cancellationToken));
        }

        await proc.WaitForExitAsync(cancellationToken);

        if (proc.ExitCode != 0)
        {
            throw new Exception($"ffmpeg failed with exit code {proc.ExitCode}");
        }
    }

    private static async Task<string> DiscardStreamAsync(
        StreamReader reader,
        CancellationToken token)
    {
        char[] buffer = new char[4096];

        while (!token.IsCancellationRequested)
        {
            int read = await reader.ReadAsync(buffer, 0, buffer.Length);

            if (read == 0) // EOF reached
                break;
        }

        return "";
    }


    private static string FormatCommand(string fileName, IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        sb.Append(fileName);

        foreach (var arg in args)
        {
            if (arg.Contains(' '))
                sb.Append(" \"" + arg + "\"");
            else
                sb.Append(" " + arg);
        }

        return sb.ToString();
    }
}

