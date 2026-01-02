using System.Reflection;
using System.Runtime.InteropServices;

namespace VideoStamper.Core;

public static class FfmpegLocator
{
    public static string GetFfmpegPath() => GetToolPath("ffmpeg");
    public static string GetFfprobePath() => GetToolPath("ffprobe");

    public static string? CustomFfmpegPath { get; set; }
    public static string? CustomFfprobePath { get; set; }

    public static string GetToolPath(string toolName)
    {
        switch(toolName) {
            case "ffmpeg":
                if (!string.IsNullOrWhiteSpace(CustomFfmpegPath))
                {
                    return CustomFfmpegPath!;
                }
                return GetDefaultToolPath("ffmpeg");
            case "ffprobe":
                if (!string.IsNullOrWhiteSpace(CustomFfprobePath))
                {
                    return CustomFfprobePath!;
                }
                return GetDefaultToolPath("ffprobe");
        }
        return GetDefaultToolPath(toolName);
    }

    private static string GetDefaultToolPath(string toolName)
    {
        string platformSubdir;
        string exe;

        if (OperatingSystem.IsWindows())
        {
            platformSubdir = "win-x64";
            exe = toolName + ".exe";
        }
        else if (OperatingSystem.IsLinux())
        {
            platformSubdir = "linux-x64";
            exe = toolName;
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Distinguish Intel vs Apple Silicon
            platformSubdir = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "macos-arm64"
                : "macos-x64";
            exe = toolName;
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported OS for FFmpeg location.");
        }

        // You can switch this back to assembly location if you prefer
        var baseDir = Directory.GetCurrentDirectory();
        var path = Path.Combine(baseDir, "bin", platformSubdir, exe);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Could not find {toolName} at {path}");

        return path;
    }
}

