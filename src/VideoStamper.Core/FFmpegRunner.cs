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
                "-v", "quiet",
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

        if (Globals.DEBUG > 2)
        {
            string cmd = FormatCommand(ffprobe, psi.ArgumentList);
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: FFprobe command:\n{cmd}\n");
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffprobe");

        var output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await proc.StandardError.ReadToEndAsync(cancellationToken);

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

        if (Globals.DEBUG > 2)
        {
            string cmd = FormatCommand(ffmpeg, psi.ArgumentList);
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: FFmpeg command:\n{cmd}\n");
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");

        // read stderr for progress/messages
        while (!proc.HasExited)
        {
            var line = await proc.StandardError.ReadLineAsync();
            if (line == null) break;
            log?.Report(line);
        }

        await proc.WaitForExitAsync(cancellationToken);

        if (proc.ExitCode != 0)
        {
            throw new Exception($"ffmpeg failed with exit code {proc.ExitCode}");
        }
    }

    private static string FormatCommand(string fileName, IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        sb.Append(fileName);

        foreach (var arg in args)
        {
            // Quote args with spaces
            if (arg.Contains(' '))
                sb.Append(" \"" + arg + "\"");
            else
                sb.Append(" " + arg);
        }

        return sb.ToString();
    }
}

