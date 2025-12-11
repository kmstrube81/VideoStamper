using System.Text.Json;

namespace VideoStamper.Core;

public sealed class VideoMetadata
{
    public string? CreationTimeRaw { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double DurationSeconds { get; init; }
}

public static class VideoMetadataReader
{
    public static async Task<VideoMetadata> GetMetadataAsync(
        string inputPath, CancellationToken cancellationToken = default)
    {
        if (Globals.DEBUG > 2)
        {
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: Running ffprobe");
        }

        var jsonText = await FfmpegRunner.RunFfprobeAsync(inputPath, cancellationToken);

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        if (Globals.DEBUG > 2)
        {
            var debugJson = JsonSerializer.Serialize(
                root,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: Raw metadata JSON:\n{debugJson}");
        }

        var format = root.GetProperty("format");
        string? creation = null;

        // Try Apple QuickTime creationdate first (includes timezone)
        if (format.TryGetProperty("tags", out var tags) &&
            tags.TryGetProperty("com.apple.quicktime.creationdate", out var ctPropApple))
        {
            creation = ctPropApple.GetString();
        }

        // Fallback to standard creation_time if Apple one not present
        if (creation == null &&
            tags.TryGetProperty("creation_time", out var ctProp))
        {
            creation = ctProp.GetString();
        }

        int width = 0, height = 0;
        double duration = 0;
        int? rotation = null;

        if (format.TryGetProperty("duration", out var durProp) &&
            double.TryParse(durProp.GetString(), out var d))
        {
            duration = d;
        }

        // Look at the first video stream
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            string? codecType = stream.TryGetProperty("codec_type", out var ct)
                ? ct.GetString()
                : null;

            if (codecType == "video")
            {
                if (stream.TryGetProperty("width", out var wProp))
                    width = wProp.GetInt32();
                if (stream.TryGetProperty("height", out var hProp))
                    height = hProp.GetInt32();
                if (stream.TryGetProperty("duration", out var sdProp) &&
                    double.TryParse(sdProp.GetString(), out var sd))
                {
                    duration = sd;
                }

                // --- Rotation detection ---

                // 1) Try side_data_list[].rotation (as in your example)
                if (stream.TryGetProperty("side_data_list", out var sideDataList) &&
                    sideDataList.ValueKind == JsonValueKind.Array)
                {
                    foreach (var side in sideDataList.EnumerateArray())
                    {
                        if (side.TryGetProperty("rotation", out var rotProp))
                        {
                            rotation = ParseRotation(rotProp);
                            if (rotation.HasValue)
                                break;
                        }
                    }
                }

                // 2) If still null, try stream.tags.rotate or stream.tags.rotation
                if (rotation == null &&
                    stream.TryGetProperty("tags", out var streamTags))
                {
                    if (streamTags.TryGetProperty("rotate", out var rotateTag))
                    {
                        rotation = ParseRotation(rotateTag);
                    }
                    else if (streamTags.TryGetProperty("rotation", out var rotationTag))
                    {
                        rotation = ParseRotation(rotationTag);
                    }
                }

                break; // done with first video stream
            }
        }

        // If rotation indicates 90° or 270°, swap width/height
        if (rotation is int rotVal)
        {
            var absRot = Math.Abs(rotVal);
            if (absRot == 90 || absRot == 270)
            {
                if (Globals.DEBUG > 1)
                {
                    Console.WriteLine(
                        $"{Globals.DEBUG_LEVEL}: Detected rotation {rotVal}°, " +
                        $"swapping width/height ({width}x{height} -> {height}x{width}).");
                }

                (width, height) = (height, width);
            }
        }

        return new VideoMetadata
        {
            CreationTimeRaw = creation,
            Width = width,
            Height = height,
            DurationSeconds = duration
        };
    }

    private static int? ParseRotation(JsonElement elem)
    {
        try
        {
            return elem.ValueKind switch
            {
                JsonValueKind.Number => elem.GetInt32(),
                JsonValueKind.String when int.TryParse(elem.GetString(), out var val) => val,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}

