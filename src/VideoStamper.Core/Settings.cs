using System.Text.Json.Serialization;

namespace VideoStamper.Core;

/* ****************************************************************************************************
*   FontSettings
*   ---
*   FontFile - string of the path to the font file for which font should be used for text generation
*   Size - int for the font size
*   Color - string, text color. Can be hex code for color or names as defined here:
*           https://ffmpeg.org/ffmpeg-utils.html#color-syntax
*   BoderColor (optional)- string, text outline color. Can be hex code for color or names as defined here:
*           https://ffmpeg.org/ffmpeg-utils.html#color-syntax
*   BorderWidth (optional)- int, the width of the text outline in pixels
******************************************************************************************************* */
public sealed class FontSettings
{
    public string FontFile { get; set; } = "";
    public int Size { get; set; } = 32;
    public string Color { get; set; } = "white";
    public string? BorderColor { get; set; } = "black";
    public int? BorderWidth { get; set; } = 2;
}

/* ****************************************************************************************************
*   PositionSettings
*   ---
*   Anchor - string, preset X,Y coordinates for position hardcoded to be padded by 5% by default
*           origin (0,0)
*           topLeft ( (w-((w/100)*(100-5))), (height-((height/100)*(100-5))) )
*           topMiddle ( (w-(w/2)-(text_w/2)), (height-((height/100)*(100-5))) )
*           topRight ( (w-(w*5/100))-text_w), (height-((height/100)*(100-5))) )
*           middleLeft ( ((w-((w/100)*(100-5))), (height-(height/2)-(fontsize/2)) )
*           middle ( (w-(w/2)-(text_w/2)), (height-(height/2)-(fontsize/2)) )
*           middleRight ( (w-(w*5/100))-text_w), (height-(height/2)-(fontsize/2)) )
*           bottomLeft ( (w-((w/100)*(100-5))), (height-(height*(5/100))-fontsize) )
*           bottomMiddle ( (w-(w/2)-(text_w/2)), (height-(height*(5/100))-fontsize) )
*           bottomRight ( (w-(w*5/100))-text_w), (height-(height*(5/100))-fontsize) )
*   XOffset (optional) - int for the number of pixels the X coordinate should be adjusted by
*   YOffset (optional) - int for the number of pixels the Y coordinate should be adjusted by
*   XPad (optional) - double for the percentage of video width the X coordinate should be adjusted by
*   YPad (optional) - double for the percentage of video height Y coordinate should be adjusted by
******************************************************************************************************* */
public sealed class PositionSettings
{
    public string Anchor { get; set; } = "bottomRight"; // "topLeft", "bottomCenter", etc.
    public int XOffset { get; set; } = 0;
    public int YOffset { get; set; } = 0;
    public double XPad { get; set; } = 5.0;
    public double YPad { get; set; } = 5.0;
}

/* ****************************************************************************************************
*   TimerstampSettings
*   ---
*   Enabled - boolean, add Timestamp to video?
*   UseMetadataCreationTime - boolean, use the creationTime metadata for date?
*   TimeOffset - int, offset from creation time (or unix epoch if above value false) in milliseconds
*   Format - string, the date time format string as specified here:
*        https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings
*   Font- FontSettings, font related settings for timestamp-
*           FontFile, Size, Color, BorderColor, and BorderWidth
*   Position- PositionSettings, positioning settings for timestamp-
*           Anchor, XOffset, YOffset
******************************************************************************************************* */
public sealed class TimestampSettings
{
    public bool Enabled { get; set; } = true;
    public bool UseMetadataCreationTime { get; set; } = true;
    public long? TimeOffset { get; set; } 
    public string Format { get; set; } = "yyyy-MM-dd HH:mm:ss"; 
    public FontSettings Font { get; set; } = new();
    public PositionSettings Position { get; set; } = new();
}

/* ****************************************************************************************************
*   SubtitleSettings
*   ---
*   Text - string, the subtitle text
*   Start - double, start time for text to appear in the video
*   End - double, end time for the text to appear in the video
*   Font- FontSettings, font related settings for timestamp-
*           FontFile, Size, Color, BorderColor, and BorderWidth
*   Position- PositionSettings, positioning settings for timestamp-
*           Anchor, XOffset, YOffset
*   AnimationIn (optional) - string, animation used for drawtext filter-
*       Slide (control x, y position over time)
*       Fade (control opacity over time)
*   AnimationOut (optional) - string, animation used for drawtext filter-
*       Slide (control x, y position over time)
*       Fade (control opacity over time)
*   AnimationInDur (optional) - double, time incoming animation lasts in seconds
*   AnimationIn (optional) - double, time outgoing animation lasts in seconds-
******************************************************************************************************* */
public sealed class SubtitleSettings
{
    public string Text { get; set; } = "";
    public double Start { get; set; }
    public double End { get; set; }
    public FontSettings Font { get; set; } = new();
    public PositionSettings Position { get; set; } = new();
    /* TODO: Implement, fade in, slide in logic for ffmpeg
    public string? AnimationIn { get; set; }
    public string? AnimationOut { get; set; }
    public double? AnimationInDur { get; set; } = 1.0;
    public double? AnimationOutDur {get; set; } = 1.0;
    */
}

/* ****************************************************************************************************
*   OutputSettings
*   ---
*   Mode - string for the processing settings, seperate output or concat into single file
*       "seperate" - output stamped file seperately
*       "concat" - combine files into single clip
*   Format - Output format (mp4, webm, gif)
******************************************************************************************************* */
public sealed class OutputSettings
{
    public string Mode { get; set; } = "separate"; // or "concat"
    public string Format { get; set; } = "mp4";    // "mp4", "webm", "gif"
}

/* ****************************************************************************************************
*   InputSettings
*   ---
*   Path - string for the input file paths
******************************************************************************************************* */
public sealed class InputSettings
{
    public string Path { get; set; } = "";
    public bool AutomaticallyFixOverlappingText { get; set; } = true;
    public TimestampSettings Timestamp { get; set; } = new();
    public List<SubtitleSettings> Subtitles { get; set; } = new();
}

/* ****************************************************************************************************
*   ProjectSettings
*   ---
*   Output - OutputSettings, the output settings for the files to be processed
*   Inputs - List, list of inputs to be processed
******************************************************************************************************* */
public sealed class ProjectSettings
{
    public OutputSettings Output { get; set; } = new();
    public List<InputSettings> Inputs { get; set; } = new();
}
