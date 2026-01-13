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
    public double? Dur {get; set; }
    public string? Between { get; set; }
    public string? InAnim { get; set; } = null;
    public string? OutAnim { get; set; } = null;
    public double? InAnimStart { get; set; } = 0;
    public double? OutAnimStart { get; set; } = 0;
    public double? InAnimEnd { get; set; } = 0;
    public double? OutAnimEnd { get; set; } = 0;
    public double? InAnimDur { get; set; }
    public double? OutAnimDur { get; set; }

    public int XCoord { get; set; } = 0;
    public int YCoord {get; set; } = 0;
    
    public DrawText(string text, string fontFile, int fontSize, string fontColor, string? xExp = "0", string? yExp = "0", string? borderColor = null, int? borderWidth = null, double? start = null, double? end = null, string? inAnimation = null, double? inAnimationDur = null, string? outAnimation = null, double? outAnimationDur = null)
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
        Dur = End - Start;

        InAnim = inAnimation;
        InAnimDur = inAnimationDur;
        OutAnim = outAnimation;
        OutAnimDur = outAnimationDur;

        if(InAnim != null && InAnimDur > 0.0) {
            if(InAnimDur > Dur) {
                if(OutAnimDur > 0.0 && OutAnimDur < Dur) {
                    InAnimDur = Dur - OutAnimDur;
                }
                else
                {
                    InAnimDur = Dur / 2;
                }
            }
            InAnimStart = Start != null ? Start : 0.0;
            InAnimEnd = InAnimStart + InAnimDur;
            Start = InAnimEnd;
        } else {
            InAnim = null;
        }

        if(OutAnim != null && OutAnimDur > 0.0 && End != null && End > Start) {
            if(OutAnimDur > Dur - InAnimDur) {
                OutAnimDur = Dur - InAnimDur;
            }
            OutAnimStart = End - OutAnimDur;
            OutAnimEnd = End;
            End = OutAnimStart;
        } else {
            OutAnim = null;
        }

        if(Start != null && End != null && Start >= 0.0 && End > Start) {
            Between = $"between(t,{Start},{End})";
        }

    }

    public string GenerateDrawTextCmd()
    {

        var sb = new StringBuilder();

        if(InAnim != null) {
            sb.Append(GenerateInAnimDrawText());
            sb.Append(",");
        }

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
            sb.Append($":enable='{Between}'");
        } else {
            if(Start != null && End != null && Start >= 0.0 && End > Start) {
                Between = $"between(t,{Start},{End})";
                sb.Append($":enable='{Between}'");
            }
        }

        if(OutAnim != null) {
            sb.Append(",");
            sb.Append(GenerateOutAnimDrawText());
        }

        return sb.ToString();
    }

    public string GenerateInAnimDrawText()
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

        switch(InAnim?.ToLower()) {
            case "fade in":
                sb.Append($"alpha=if(lt(t\\,{InAnimStart})\\,0\\,if(lt(t\\,{InAnimEnd})\\,(t-{InAnimStart})/{InAnimDur}\\,1)):");
                sb.Append($"x={XExpr}:");
                sb.Append($"y={YExpr}:");
                break;
            case "fly in from top left":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,(0 - text_w) + ({XExpr} - (0 - text_w)) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,(0 - text_h) + ({YExpr} - (0 - text_h)) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            case "fly in from top":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,(w-(w/2)-(text_w/2)) + ({XExpr} - (w-(w/2)-(text_w/2))) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,(0 - text_h) + ({YExpr} - (0 - text_h)) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            case "fly in from top right":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,w + ({XExpr} - w) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,(0 - text_h) + ({YExpr} - (0 - text_h)) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            case "fly in from left":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,(0 - text_w) + ({XExpr} - (0 - text_w)) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,(h-(h/2)-(text_h/2)) + ({YExpr} - (h-(h/2)-(text_h/2))) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            case "fly in from right":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,w + ({XExpr} - w) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,(h-(h/2)-(text_h/2)) + ({YExpr} - (h-(h/2)-(text_h/2))) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            case "fly in from bottom left":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,(0 - text_w) + ({XExpr} - (0 - text_w)) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,h + ({YExpr} - h) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            case "fly in from bottom":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,(w-(w/2)-(text_w/2)) + ({XExpr} - (w-(w/2)-(text_w/2))) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,h + ({YExpr} - h) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            case "fly in from bottom right":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,w + ({XExpr} - w) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,h + ({YExpr} - h) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            case "slide in from left":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,(0 - text_w) + ({XExpr} - (0 - text_w)) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y={YExpr}:");
                break;
            case "slide in from right":
                sb.Append($"x=if(lt(t\\,{InAnimEnd})\\,w + ({XExpr} - w) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{XExpr}):");
                sb.Append($"y={YExpr}:");
                break;
            case "slide in from top":
                sb.Append($"x={XExpr}:");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,(0 - text_h) + ({YExpr} - (0 - text_h)) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            case "slide in from bottom":
                sb.Append($"x={XExpr}:");
                sb.Append($"y=if(lt(t\\,{InAnimEnd})\\,h + ({YExpr} - h) * (t-{InAnimStart})/({InAnimEnd} - {InAnimStart})\\,{YExpr}):");
                break;
            default:
                sb.Append($"x={XExpr}:");
                sb.Append($"y={YExpr}:");
                break;
        }

        sb.Append($"text='{Text}'");
        sb.Append($":enable='between(t,{InAnimStart},{InAnimEnd})'");
        return sb.ToString();
    }

    public string GenerateOutAnimDrawText()
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

        switch(OutAnim?.ToLower()) {
            case "fade out":
                sb.Append($"alpha=if(lt(t\\,{OutAnimStart})\\,1\\,if(lt(t\\,{OutAnimEnd})\\,({OutAnimEnd}-t)/{OutAnimDur}\\,0)):");
                sb.Append($"x={XExpr}:");
                sb.Append($"y={YExpr}:");
                break;
            case "fly out to top left":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + ( (0 - text_w) - {XExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + ((0 - text_h) - {YExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                break;
            case "fly out to top":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + ((w-(w/2)-(text_w/2)) - {XExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + ((0 - text_h) - {YExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                break;
            case "fly out to top right":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + ( w - {XExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + ((0 - text_h) - {YExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                break;
            case "fly out to left":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + ((0 - text_w) - {XExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + ((h-(h/2)-(text_h/2)) - {YExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                break;
            case "fly out to right":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + (w - {XExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + ( (h-(h/2)-(text_h/2)) - {YExpr} ) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                break;
            case "fly out to bottom left":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + ((0 - text_w) - {XExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + ( h - {YExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                break;
            case "fly out to bottom":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + ( (w-(w/2)-(text_w/2)) - {XExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + (h - {YExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                break;
            case "fly out to bottom right":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + (w - {XExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + ( h - {YExpr} ) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                break;
            case "slide out to left":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + ( (0 - text_w) - {XExpr} ) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y={YExpr}:");
                break;
            case "slide out to right":
                sb.Append($"x=if(lt(t\\,{OutAnimStart})\\,{XExpr}\\,{XExpr} + (w - {XExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                sb.Append($"y={YExpr}:");
                break;
            case "slide out to top":
                sb.Append($"x={XExpr}:");
                sb.Append($"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + (0 - text_h) - {YExpr}) * (t-{OutAnimStart})/({OutAnimEnd} - {OutAnimStart})):");
                break;
            case "slide out to bottom":
                sb.Append($"x={XExpr}:");
                sb.Append(
                    $"y=if(lt(t\\,{OutAnimStart})\\,{YExpr}\\,{YExpr} + ((h+text_h) - {YExpr}) * (t-{OutAnimStart})/({OutAnimEnd}-{OutAnimStart})):");
                break;

            default:
                sb.Append($"x={XExpr}:");
                sb.Append($"y={YExpr}:");
                break;
        }

        sb.Append($"text='{Text}'");
        sb.Append($":enable='between(t,{OutAnimStart},{OutAnimEnd})'");
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

