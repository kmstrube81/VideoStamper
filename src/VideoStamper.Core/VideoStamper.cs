using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VideoStamper.Core;

public sealed class ProcessResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

public static class ProjectProcessor
{
    public static async Task<ProcessResult> ProcessProjectAsync(
        string projectJson,
        string? projectFilePath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (Globals.DEBUG > 1)
        {
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: Reading project settings JSON");
        }

        // Get Settings
        ProjectSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<ProjectSettings>(
                projectJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? new ProjectSettings();

            if (Globals.DEBUG > 1)
            {
                var debugJson = JsonSerializer.Serialize(
                    settings,
                    new JsonSerializerOptions { WriteIndented = true }
                );
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Parsed ProjectSettings:\n{debugJson}");
            }
        }
        catch (Exception ex)
        {
            return new ProcessResult
            {
                Success = false,
                Message = $"Failed to parse project JSON: {ex.Message}"
            };
        }

        if (settings.Inputs.Count == 0)
        {
            return new ProcessResult
            {
                Success = false,
                Message = "No inputs defined in project JSON."
            };
        }

        if (Globals.DEBUG > 0)
        {
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: {settings.Inputs.Count} videos to process");
        }

        // Progress sink for ffmpeg
        var progress = new Progress<string>(s => Console.WriteLine($"[ffmpeg] {s}"));

        // Temp directory for all intermediate work
        var tempRoot = Path.Combine(Path.GetTempPath(), "VideoStamper");
        Directory.CreateDirectory(tempRoot);
        var tempDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // We will always stamp to temp MP4 clips first (like the PowerShell does),
        // then optionally concat and/or re-encode to webm/gif.
        var stampedFiles = new List<(string InputPath, string TempStampedPath)>();

        // 1) STAMP EACH INPUT → temp .mp4
        foreach (var input in settings.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(input.Path) || !File.Exists(input.Path))
            {
                throw new FileNotFoundException($"Input file not found: {input.Path}");
            }

            if (Globals.DEBUG > 1)
            {
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Processing {input.Path}");
            }

            if (Globals.DEBUG > 0)
            {
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Reading metadata");
            }

            var meta = await VideoMetadataReader.GetMetadataAsync(input.Path, cancellationToken);

            if (Globals.DEBUG > 1)
            {
                var debugJson = JsonSerializer.Serialize(
                    meta,
                    new JsonSerializerOptions { WriteIndented = true }
                );
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Parsed metadata:\n{debugJson}");
            }

            if (Globals.DEBUG > 0)
            {
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Generating FFmpeg filter");
            }

            var filter = FilterBuilder.BuildFilterComplexForInput(input, meta);

            if (Globals.DEBUG > 1)
            {
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: FFmpeg filter = {filter}");
            }

            // Intermediate stamped clip path (ALWAYS mp4)
            var inputNameNoExt = Path.GetFileNameWithoutExtension(input.Path);
            var tempStampedPath = Path.Combine(tempDir, $"{inputNameNoExt}.stamped.mp4");

            var stampArgs = new List<string>
            {
                "-y",
                "-i", input.Path,
                "-vf", filter,
                "-map_metadata", "0",
                "-movflags", "use_metadata_tags",
                tempStampedPath
            };

            await FfmpegRunner.RunFfmpegAsync(stampArgs, progress, cancellationToken);

            if (Globals.DEBUG > 0)
            {
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Finished stamping {input.Path} → {tempStampedPath}");
            }

            stampedFiles.Add((input.Path, tempStampedPath));
        }

        // 2) FINAL OUTPUT LOGIC: concat vs per-clip, and mp4/webm/gif conversions
        var mode = settings.Output.Mode ?? string.Empty;
        var format = (settings.Output.Format ?? "mp4").ToLowerInvariant();

        string outputExt = format switch
        {
            "mp4" => ".mp4",
            "webm" => ".webm",
            "gif" => ".gif",
            _ => "." + format
        };

        bool isConcat =
            string.Equals(mode, "concatenate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "concat", StringComparison.OrdinalIgnoreCase);

        if (isConcat)
        {
            // === CONCATENATE MODE (project-level output name) ===

            if (stampedFiles.Count == 0)
            {
                return new ProcessResult
                {
                    Success = false,
                    Message = "No stamped clips produced."
                };
            }

            // Determine final output directory & name from the project JSON path
            string finalDir;
            string finalBaseName;

            if (!string.IsNullOrWhiteSpace(projectFilePath))
            {
                finalDir = Path.GetDirectoryName(projectFilePath) ?? Directory.GetCurrentDirectory();
                finalBaseName = Path.GetFileNameWithoutExtension(projectFilePath);
            }
            else
            {
                // Fallback: use first input's folder if projectFilePath is not provided
                finalDir = Path.GetDirectoryName(stampedFiles[0].InputPath) ?? Directory.GetCurrentDirectory();
                finalBaseName = Path.GetFileNameWithoutExtension(stampedFiles[0].InputPath);
            }

            var finalOut = Path.Combine(finalDir, finalBaseName + outputExt);

            if (Globals.DEBUG > 0)
            {
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Concatenate mode, final output will be {finalOut}");
            }

            // If only one stamped clip, we can treat it as the "concat" result
            string tempConcatMp4;
            if (stampedFiles.Count == 1)
            {
                tempConcatMp4 = stampedFiles[0].TempStampedPath;
            }
            else
            {
                // Build concat list file
                var listFile = Path.Combine(tempDir, "concat_list.txt");
                var lines = stampedFiles.Select(s => $"file '{s.TempStampedPath}'");
                await File.WriteAllLinesAsync(listFile, lines, cancellationToken);

                tempConcatMp4 = Path.Combine(tempDir, "concat_temp.mp4");

                var concatArgs = new List<string>
                {
                    "-hide_banner",
                    "-loglevel", "error",
                    "-f", "concat",
                    "-safe", "0",
                    "-i", listFile,
                    "-c", "copy",
                    "-y", tempConcatMp4
                };

                await FfmpegRunner.RunFfmpegAsync(concatArgs, progress, cancellationToken);

                if (Globals.DEBUG > 0)
                {
                    Console.WriteLine($"{Globals.DEBUG_LEVEL}: Finished concat → {tempConcatMp4}");
                }
            }

            // Re-encode / move based on requested final format
            switch (format)
            {
                case "mp4":
                    {
                        Directory.CreateDirectory(finalDir);
                        if (!string.Equals(tempConcatMp4, finalOut, StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(finalOut)) File.Delete(finalOut);
                            File.Move(tempConcatMp4, finalOut);
                        }

                        if (Globals.DEBUG > 0)
                        {
                            Console.WriteLine($"{Globals.DEBUG_LEVEL}: Finished - stamped mp4 saved to: {finalOut}");
                        }
                        break;
                    }

                case "webm":
                    {
                        var args = new List<string>
                        {
                            "-y",
                            "-i", tempConcatMp4,
                            "-c:v", "libvpx-vp9",
                            "-b:v", "2000k",
                            "-vf", "scale='min(iw,720)':-1",
                            "-movflags", "use_metadata_tags",
                            "-preset", "ultrafast",
                            "-r", "10",
                            "-map_metadata", "0",
                            finalOut
                        };
                        await FfmpegRunner.RunFfmpegAsync(args, progress, cancellationToken);

                        if (Globals.DEBUG > 0)
                        {
                            Console.WriteLine($"{Globals.DEBUG_LEVEL}: Finished - stamped webm video saved to: {finalOut}");
                        }
                        break;
                    }

                case "gif":
                    {
                        var args = new List<string>
                        {
                            "-y",
                            "-i", tempConcatMp4,
                            "-vf", "fps=10,scale='min(iw,720)':-1:flags=lanczos",
                            "-loop", "0",
                            finalOut
                        };
                        await FfmpegRunner.RunFfmpegAsync(args, progress, cancellationToken);

                        if (Globals.DEBUG > 0)
                        {
                            Console.WriteLine($"{Globals.DEBUG_LEVEL}: Finished - stamped gif saved to: {finalOut}");
                        }
                        break;
                    }

                default:
                    throw new NotSupportedException($"Unsupported output format: {format}");
            }

            return new ProcessResult
            {
                Success = true,
                Message = $"Processed {stampedFiles.Count} clip(s) into {finalOut}."
            };
        }
        else
        {
            // === PER-CLIP MODE (each input → {name}-stamped.<ext> in same folder) ===

            var finalOutputs = new List<string>();

            foreach (var (inputPath, tempStampedPath) in stampedFiles)
            {
                var dir = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
                var baseName = Path.GetFileNameWithoutExtension(inputPath);
                var finalOut = Path.Combine(dir, $"{baseName}-stamped{outputExt}");

                Directory.CreateDirectory(dir);

                switch (format)
                {
                    case "mp4":
                        {
                            if (File.Exists(finalOut)) File.Delete(finalOut);
                            File.Move(tempStampedPath, finalOut);

                            if (Globals.DEBUG > 0)
                            {
                                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Finished - stamped mp4 saved to: {finalOut}");
                            }
                            break;
                        }

                    case "webm":
                        {
                            var args = new List<string>
                            {
                                "-y",
                                "-i", tempStampedPath,
                                "-c:v", "libvpx-vp9",
                                "-b:v", "2000k",
                                "-vf", "scale='min(iw,720)':-1",
                                "-movflags", "use_metadata_tags",
                                "-preset", "ultrafast",
                                "-r", "10",
                                "-map_metadata", "0",
                                finalOut
                            };
                            await FfmpegRunner.RunFfmpegAsync(args, progress, cancellationToken);

                            if (Globals.DEBUG > 0)
                            {
                                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Finished - stamped webm video saved to: {finalOut}");
                            }
                            break;
                        }

                    case "gif":
                        {
                            var args = new List<string>
                            {
                                "-y",
                                "-i", tempStampedPath,
                                "-vf", "fps=10,scale='min(iw,720)':-1:flags=lanczos",
                                "-loop", "0",
                                finalOut
                            };
                            await FfmpegRunner.RunFfmpegAsync(args, progress, cancellationToken);

                            if (Globals.DEBUG > 0)
                            {
                                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Finished - stamped gif saved to: {finalOut}");
                            }
                            break;
                        }

                    default:
                        throw new NotSupportedException($"Unsupported output format: {format}");
                }

                finalOutputs.Add(finalOut);
            }

            return new ProcessResult
            {
                Success = true,
                Message = $"Processed {finalOutputs.Count} file(s)."
            };
        }
    }
}

