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

        // Always build a printable command string (useful for exception detail)
        string cmd = FormatCommand(ffmpeg, psi.ArgumentList);

        // Debug-only command echo (console), but ALSO nice to show in GUI log
        if (Globals.DEBUG > 2)
        {
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: FFmpeg command:\n{cmd}\n");
            log?.Report($"{Globals.DEBUG_LEVEL}: FFmpeg command:");
            log?.Report(cmd);
            log?.Report("");
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");

        // Tail buffers so we can throw a helpful exception
        var stdoutTail = new RingBuffer(200);
        var stderrTail = new RingBuffer(200);

        // Ensure cancellation stops ffmpeg
        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }
        });

        // Read stdout/stderr concurrently so neither pipe blocks ffmpeg
        var stdoutTask = Task.Run(async () =>
        {
            while (!proc.StandardOutput.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await proc.StandardOutput.ReadLineAsync();
                if (line == null) break;

                stdoutTail.Add(line);

                // In debug, you may want stdout too, but usually stderr is enough.
                // If you want stdout logged live, uncomment:
                // if (Globals.DEBUG > 2) log?.Report(line);
            }
        }, cancellationToken);

        var stderrTask = Task.Run(async () =>
        {
            while (!proc.StandardError.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await proc.StandardError.ReadLineAsync();
                if (line == null) break;

                stderrTail.Add(line);

                // In debug, stream stderr live (this is where ffmpeg progress/errors go)
                if (Globals.DEBUG > 2)
                    log?.Report(line);
            }
        }, cancellationToken);

        await proc.WaitForExitAsync(cancellationToken);

        // Ensure both reader tasks complete (drain remaining output)
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            // If canceled, let the OCE bubble up or you can wrap it.
            throw;
        }

        if (proc.ExitCode != 0)
        {
            var stderrText = stderrTail.ToString();
            var stdoutText = stdoutTail.ToString();

            throw new Exception(
                $"ffmpeg failed with exit code {proc.ExitCode}\n\n" +
                $"Command:\n{cmd}\n\n" +
                $"--- stderr (tail) ---\n{stderrText}\n\n" +
                $"--- stdout (tail) ---\n{stdoutText}\n"
            );
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

    private sealed class RingBuffer
    {
        private readonly int _capacity;
        private readonly Queue<string> _q;

        public RingBuffer(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            _q = new Queue<string>(_capacity);
        }

        public void Add(string line)
        {
            if (_q.Count >= _capacity)
                _q.Dequeue();
            _q.Enqueue(line);
        }

        public override string ToString()
            => string.Join(Environment.NewLine, _q);
    }

}

