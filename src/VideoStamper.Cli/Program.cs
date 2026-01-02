using VideoStamper.Core;

if (args.Length < 1)
{
    Console.WriteLine("Usage: VideoStamper.Cli <project.json>");
    Console.WriteLine("For documentation on proper .json format, visit github: https://github.com/kmstrube81/VideoStamper");
    return 1;
}

var projectPath = args[0];

var debugLevel = (args.Length > 1)
    ? args[1]
    : "none";


switch (debugLevel) {
    case "-i":
    case "--info":
    case "info":
        Globals.DEBUG = 1;
        Globals.DEBUG_LEVEL = "INFO";
        break;
    case "-v":
    case "--verbose":
    case "verbose":
        Globals.DEBUG = 2;
        Globals.DEBUG_LEVEL = "VERBOSE";
        break;
    case "-d":
    case "--debug":
    case "debug":
        Globals.DEBUG = 3;
        Globals.DEBUG_LEVEL = "DEBUG";
        break;
    default:
        Globals.DEBUG = 0;
        Globals.DEBUG_LEVEL = "none";
        break;
}

if(Globals.DEBUG > 0) {
    Console.WriteLine($"Debug level set to {Globals.DEBUG_LEVEL}");
}

switch (projectPath) {
    case "help":
    case "-help":
    case "--help":
    case "-h":
    case "?":
    case "-?":
    case "/?":
        Console.WriteLine("Usage: VideoStamper.Cli <project.json>");
        Console.WriteLine("For documentation on proper .json format, visit github: https://github.com/kmstrube81/VideoStamper");
        return 1;
    default:
        if (!File.Exists(projectPath))
        {
            Console.WriteLine($"Project file not found: {projectPath}");
            return 1;
        }

        Console.WriteLine(" __      __ _     _           _____                           ");
        Console.WriteLine(" \\ \\    / /(_)   | |         /  ___\\  _                       ");
        Console.WriteLine("  \\ \\  / /  _  __| | ___  __ | (___  | |  __ _ _ __ ___  _ __   ___ _ __ ");
        Console.WriteLine("   \\ \\/ /  | |/ _  |/ _ \\/  \\\\___  \\[   ]/ _' | '_ ' _ \\| '_ \\ / _ \\ '__|");
        Console.WriteLine("    \\  /   | ||(_| || __/|()| ___) | | | |(_| | | | | | | |_) |  __/ |   ");
        Console.WriteLine("     \\/    |_|\\___.|\\___|\\__/|_____/ |_| \\___.|_| |_| |_| .__/ \\___|_|   ");
        Console.WriteLine("                                                        | |              ");
        Console.WriteLine("                                                        |_|");

        var projectJson = await File.ReadAllTextAsync(projectPath);

        try
        {
            var progress = new Progress<string>(s => Console.WriteLine(s));
            var result = await ProjectProcessor.ProcessProjectAsync(projectJson, projectPath, CancellationToken.None, progress);

            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex);
            return 1;
        }
}



