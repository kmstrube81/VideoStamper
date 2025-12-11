using System.Text;

namespace VideoStamper.Core;

/* ****************************************************************************************************
*   DrawText
*   ---
*   FontFile - string of the path to the font file for which font should be used for text generation
*   FontSize - int for the font size
*   FontColor - string, text color. Can be hex code for color or names
*   BorderColor (optional)- string, text outline color. Can be hex code for color or names:
*   BorderWidth (optional)- int, the width of the text outline in pixels
*   XExpr
*   YExpr
*   Start
*   End
*   Between
******************************************************************************************************* */

public sealed class DrawText {

    public string Text {get; set; }
    public string FontFile { get; set; } = "";
    public int FontSize { get; set; } = 32;
    public string FontColor { get; set; } = "white";
    public string? BorderColor { get; set; }
    public int? BorderWidth { get; set; }
    public string? XExpr { get; set; } = "0";
    public string? YExpr { get; set; } = "0";
    public double? Start { get; set; }
    public double? End { get; set; }
    public string? Between { get; set; }

    public int XCoord { get; set; } = 0;
    public int YCoord {get; set; } = 0;
    
    public DrawText(string text, string fontFile, int fontSize, string fontColor, string? xExp = "0", string? yExp = "0", string? borderColor = null, int? borderWidth = null, double? start = null, double? end = null)
    {
        Text = text;
        FontFile = fontFile;
        FontSize = fontSize;
        FontColor = fontColor;
        BorderColor = borderColor;
        BorderWidth = borderWidth;
        XExpr = xExp;
        YExpr = yExp;
        Start = start;
        End = end;
        if(Start != null && End != null && Start >= 0.0 && End > Start) {
            Between = $"between(t,{Start},{End})";
        }
    }

    public string GenerateDrawTextCmd()
    {
        var sb = new StringBuilder();

        sb.Append("drawtext=");
        sb.Append($"fontfile='{FontFile}':");
        sb.Append($"fontsize={FontSize}:");
        sb.Append($"fontcolor={FontColor}:");

        if (!string.IsNullOrEmpty(BorderColor) && BorderWidth.HasValue)
        {
            sb.Append($"bordercolor={BorderColor}:");
            sb.Append($"borderw={BorderWidth.Value}:");
        }

        sb.Append($"x={XExpr}:");
        sb.Append($"y={YExpr}:");
        sb.Append($"text='{Text}'");
        if (!string.IsNullOrEmpty(Between))
        {
            sb.Append($":enable='{Between})'");
        }

        return sb.ToString();
    }

    public void AddToList(
    Dictionary<string, List<DrawText>> dict,
    string position)
    {
        if (!dict.TryGetValue(position, out var list))
        {
            list = new List<DrawText>();
            dict[position] = list;
        }

        list.Add(this);
    }

}

