using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;           
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;


namespace VideoStamper.Gui.Models;

public class VideoStamperProject
{
    public string? Name { get; set; }
    public ToolSettings? Tools { get; set; }
    public OutputSettings Output { get; set; } = new();
    public List<InputSettings> Inputs { get; set; } = new();
}

public class OutputSettings
{
    // "separate" or "concat"
    public string Mode { get; set; } = "separate";

    // "mp4", "webm", "gif"
    public string Format { get; set; } = "mp4";
}

public class InputSettings
{
    public string Path { get; set; } = string.Empty;

    public bool AutomaticallyFixOverlappingText { get; set; } = true;

    public TimestampSettings? Timestamp { get; set; }

    public ObservableCollection<SubtitleSettings>? Subtitles { get; set; }

    public string FileName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
                return string.Empty;

            return System.IO.Path.GetFileName(Path);
        }
    }

     [JsonIgnore]
    public string? TimestampMonth { get; set; }

    [JsonIgnore]
    public string? TimestampDay { get; set; }

    [JsonIgnore]
    public string? TimestampYear { get; set; }

    [JsonIgnore]
    public string? TimestampHour { get; set; }

    [JsonIgnore]
    public string? TimestampMinute { get; set; }

    [JsonIgnore]
    public string? TimestampSecond { get; set; }

    [JsonIgnore]
    public string? TimestampAmPm { get; set; }

    [JsonIgnore]
    public DateTimeOffset? MetadataCreationTime { get; set; }

}

public class ToolSettings
{
    public string? FfmpegPath { get; set; }
    public string? FfprobePath { get; set; }
}

public class TimestampSettings
{
    public bool Enabled { get; set; } = true;

    public bool UseMetadataCreationTime { get; set; } = true;

    public int TimeOffset { get; set; } = 0;

    // e.g. "yyyy-MM-dd HH:mm:ss"
    public string Format { get; set; } = "yyyy-MM-dd HH:mm:ss";

    public FontSettings Font { get; set; } = new();

    public PositionSettings Position { get; set; } = new();
}

public class FontSettings
{
    public string FontFile { get; set; } =
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";

    public int Size { get; set; } = 32;

    public string Color { get; set; } = "white";

    public string? BorderColor { get; set; } = "black";

    public int? BorderWidth { get; set; } = 2;

    [System.Text.Json.Serialization.JsonIgnore]
    public object? SelectedFontOption { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public object? SelectedBorderColorOption { get; set; }

}

public class PositionSettings
{
    // "bottomRight", etc.
    public string Anchor { get; set; } = "bottomRight";

    public int XPad { get; set; } = 5;
    public int YPad { get; set; } = 5;

    public int XOffset { get; set; } = 16;
    public int YOffset { get; set; } = 16;
}


public class SubtitleSettings
{
    // Row 1: the actual subtitle text
    public string Text { get; set; } = string.Empty;

    // Start time (seconds from beginning of video)
    public double Start { get; set; }

    // Duration in seconds (matches "Duration" label in the UI)
    public double Duration { get; set; }

    public double End => Start + Duration;

    // Font + position (same types you already use for timestamp)
    public FontSettings Font { get; set; } = new();
    public PositionSettings Position { get; set; } = new();

    // Animations – names match the XAML bindings
    public string InAnimation { get; set; } = "None";
    public string OutAnimation { get; set; } = "None";

    public double InAnimationDuration { get; set; } = 0.5;
    public double OutAnimationDuration { get; set; } = 0.5;

    [System.Text.Json.Serialization.JsonIgnore]
    public int Index { get;set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFirst { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLast { get; set; }
}
