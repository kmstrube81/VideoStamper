using System.Reflection;

namespace VideoStamper.Core;

public static class FfmpegLocator
{
    public static string GetFfmpegPath()
        => GetToolPath("ffmpeg");

    public static string GetFfprobePath()
        => GetToolPath("ffprobe");

    private static string GetToolPath(string toolName)
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var baseDir = Directory.GetCurrentDirectory();
//
//#if WINDOWS
//        var platformSubdir = "win-x64";
//        var exe = toolName + ".exe";
//#else
//        var platformSubdir = "linux-x64";
//        var exe = toolName; // no .exe on Linux
//#endif

        var platformSubdir = "macos-arm64";
        var exe = toolName;

        var path = Path.Combine(baseDir, "bin", platformSubdir, exe);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Could not find {toolName} at {path}");

        return path;
    }
}

