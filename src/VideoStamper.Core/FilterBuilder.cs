using System;
using System.Globalization;
using System.Text;
using System.Collections.Generic;

namespace VideoStamper.Core;

public static class FilterBuilder
{
    public static string BuildFilterComplexForInput(
        InputSettings input,
        VideoMetadata meta
    )
    {
        var filters = new Dictionary<string, List<DrawText>>();

        if (input.Timestamp.Enabled)
        {
            var tsFilters = BuildTimestampFilter(input.Timestamp, meta);
            foreach (var (position, drawTexts) in tsFilters)
            {
                if(Globals.DEBUG > 2) {
                    Console.WriteLine($"{Globals.DEBUG_LEVEL}: Processing position: {position}");
                }

                foreach (var drawText in drawTexts)
                {
                    drawText.AddToList(filters, position); /*
                    string cmd = drawText.GenerateDrawTextCmd();
                    Console.WriteLine(cmd); */
                }
            }

        }

        // TODO: add  animations, etc.
        foreach (var sub in input.Subtitles ) {
            var subFilters = BuildSubtitleFilter(sub, meta);
            foreach (var (position, drawTexts) in subFilters)
            {
                if(Globals.DEBUG > 2) {
                    Console.WriteLine($"{Globals.DEBUG_LEVEL}: Processing position: {position}");
                }

                foreach (var drawText in drawTexts)
                {
                    drawText.AddToList(filters, position);
                }
            }
        }

        if(input.AutomaticallyFixOverlappingText) {
            FixOverlappingText(filters);
        }

        List<string> filterCmds = new List<string>();
        foreach(var (position, drawTexts) in filters) {
            if(Globals.DEBUG > 2) {
                    Console.WriteLine($"{Globals.DEBUG_LEVEL}: Processing position: {position}");
                }

                foreach (var drawText in drawTexts)
                {
                    string cmd = drawText.GenerateDrawTextCmd();
                    filterCmds.Add(cmd);
                }
        }
        return string.Join(",", filterCmds);
    }

    private static Dictionary<string, List<DrawText>> BuildTimestampFilter(
        TimestampSettings ts,
        VideoMetadata meta
    )
    {
        // 1. Decide the base Unix epoch for %{pts:gmtime:...}
        long unixEpoch = 0;
        bool hasTimestamp = false;

        // (a) Try metadata creation_time first, if allowed
        if (ts.UseMetadataCreationTime && !string.IsNullOrWhiteSpace(meta.CreationTimeRaw))
        {
            if (Globals.DEBUG > 0)
            {
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: Raw creation time: {meta.CreationTimeRaw}");
            }

            // Prefer a timezone-aware parse
            if (DateTimeOffset.TryParse(
                    meta.CreationTimeRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                    out var dto))
            {
                // dto represents an instant with an offset (e.g. 2025-11-01T11:01:08-05:00).
                //
                // drawtext/strftime interprets the epoch strictly as UTC when using gmtime.
                // To get the *wall-clock time* of the original timezone, we treat the local
                // wall time as if it were UTC by shifting the epoch by the offset:
                //
                //   epochLocalAsUtc = epochUtc + offsetSeconds
                //
                // So gmtime(epochLocalAsUtc) prints the local wall time.
                long offsetSeconds = (long)dto.Offset.TotalSeconds;
                unixEpoch = dto.ToUnixTimeSeconds() + offsetSeconds;
                hasTimestamp = true;

                if (Globals.DEBUG > 1)
                {
                    Console.WriteLine(
                        $"{Globals.DEBUG_LEVEL}: Parsed zoned timestamp = {dto.LocalDateTime} " +
                        $"(offset {dto.Offset}), unixEpoch (wall clock) = {unixEpoch}");
                }
            }
            // Fallback: no explicit offset in the string, treat it as local time
            else if (DateTime.TryParse(
                         meta.CreationTimeRaw,
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                         out var dtLocal))
            {
                var dtoLocal = new DateTimeOffset(dtLocal);
                long offsetSeconds = (long)dtoLocal.Offset.TotalSeconds;
                unixEpoch = dtoLocal.ToUnixTimeSeconds() + offsetSeconds;
                hasTimestamp = true;

                if (Globals.DEBUG > 1)
                {
                    Console.WriteLine(
                        $"{Globals.DEBUG_LEVEL}: Parsed local timestamp = {dtLocal} " +
                        $"(offset {dtoLocal.Offset}), unixEpoch (wall clock) = {unixEpoch}");
                }
            }
        }

        // (b) If that failed, use TimeOffset (seconds since Unix epoch)
        if (ts.TimeOffset.HasValue)
        {
            try
            {
                // TimeOffset is already "seconds from 1970-01-01T00:00:00Z"
                unixEpoch += ts.TimeOffset.Value;
                hasTimestamp = true;

                if (Globals.DEBUG > 1)
                {
                    Console.WriteLine(
                        $"{Globals.DEBUG_LEVEL}: Using TimeOffset = {ts.TimeOffset.Value}, unixEpoch = {unixEpoch}");
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                if (Globals.DEBUG > 0)
                {
                    Console.WriteLine(
                        $"{Globals.DEBUG_LEVEL}: Invalid TimeOffset {ts.TimeOffset.Value}, falling back to 0");
                }
            }
        }

        if (!hasTimestamp && Globals.DEBUG > 0)
        {
            Console.WriteLine(
                $"{Globals.DEBUG_LEVEL}: No usable timestamp found; unixEpoch will be 0 (NO_DATE).");
        }

        if (Globals.DEBUG > 1)
            Console.WriteLine($"{Globals.DEBUG_LEVEL}: Unix epoch = {unixEpoch}");

        string ffmpegFormat = ConvertTimeFormat(ts.Format);

        // 2. Support multi-line format strings
        ffmpegFormat = ffmpegFormat.Replace("'", "\\'")      // escape single quotes
                                   .Replace("\\", "\\\\\\\\") // escape backslashes
                                   .Replace(":", "\\\\\\:")   // escape colons for filter syntax
                                   .Replace(",", "\\\\,")     // escape commas
                                   .Replace("/", "\\\\/");    // escape forward slash

        string[] newlines = ffmpegFormat.Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries
        );

        var drawTexts = new Dictionary<string, List<DrawText>>();

        foreach (string format in newlines)
        {
            // Format string
            string text = "%{pts\\:gmtime\\:" + unixEpoch + "\\:" + format + "}";

            // 3. Position based on anchor
            var pos = ts.Position;
            var (xExpr, yExpr) = AnchorToExpressions(
                pos.Anchor,
                meta.Width,
                meta.Height,
                ts.Font.Size,
                pos.XOffset,
                pos.YOffset,
                pos.XPad,
                pos.YPad
            );

            var (xCoord, yCoord) = AnchorToEvaluated(
                pos.Anchor,
                meta.Width,
                ts.Font.Size * 0.5 * format.Length,
                meta.Height,
                ts.Font.Size,
                pos.XOffset,
                pos.YOffset,
                pos.XPad,
                pos.YPad
            );

            DrawText filter = new DrawText(
                text,
                ts.Font.FontFile,
                ts.Font.Size,
                ts.Font.Color,
                xExpr,
                yExpr
            );

            if (!string.IsNullOrEmpty(ts.Font.BorderColor) && ts.Font.BorderWidth.HasValue)
            {
                filter.BorderColor = ts.Font.BorderColor;
                filter.BorderWidth = ts.Font.BorderWidth;
            }

            filter.XCoord = (int)xCoord;
            filter.YCoord = (int)yCoord;
            filter.AddToList(drawTexts, pos.Anchor);
        }

        return drawTexts;
    }


    private static Dictionary<string, List<DrawText>> BuildSubtitleFilter(
        SubtitleSettings sub,
        VideoMetadata meta
    )
    {

        string text = sub.Text;

        // Support multi-line format strings
        text = text.Replace("'", "\\'")     // escape single quotes
                   .Replace("\\", "\\\\\\\\")                   // escape backslashes
                   .Replace(":", "\\\\\\:")                    // escape colons for filter syntax
                   .Replace(",", "\\\\,")                     //escape commas
                   .Replace("/","\\\\/");                     //escape forward slash
        
        string[] newlines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        
        var drawTexts = new Dictionary<string, List<DrawText>>();

        foreach(string newtext in newlines ) {

            // Position based on anchor
            var pos = sub.Position;
            var (xExpr, yExpr) = AnchorToExpressions(pos.Anchor, meta.Width, meta.Height, sub.Font.Size, pos.XOffset, pos.YOffset, pos.XPad, pos.YPad);
            var (xCoord, yCoord) = AnchorToEvaluated(pos.Anchor, meta.Width, sub.Font.Size * 0.5 * newtext.Length, meta.Height, sub.Font.Size, pos.XOffset, pos.YOffset, pos.XPad, pos.YPad);

            DrawText filter = new DrawText(newtext, sub.Font.FontFile, sub.Font.Size, sub.Font.Color, xExpr, yExpr);

            if (!string.IsNullOrEmpty(sub.Font.BorderColor) && sub.Font.BorderWidth.HasValue)
            {
                filter.BorderColor = sub.Font.BorderColor;
                filter.BorderWidth = sub.Font.BorderWidth;
            }

            filter.XCoord = (int)xCoord;
            filter.YCoord = (int)yCoord;
            filter.AddToList(drawTexts, pos.Anchor);
        }
        return drawTexts;
    }

    private static (string x, string y) AnchorToExpressions(string anchor, int width, int height, int fontsize, int xOffset = 0, int yOffset = 0, double xPad = 5.0, double yPad = 5.0)
    {
        anchor = anchor.ToLowerInvariant().Replace(" ", "");

        return anchor switch
        {
            "topleft" =>
                ($"(w-((w/100)*(100-{xPad})))+{xOffset}",
                 $"({height}-(({height}/100)*(100-{yPad})))+{yOffset}"),

            "topmiddle" =>
                ($"(w-(w/2)-(text_w/2))+{xOffset}",
                 $"({height}-(({height}/100)*(100-{yPad})))+{yOffset}"),

            "topright" =>
                ($"(w-(w*{xPad}/100)-text_w)+{xOffset}",
                 $"({height}-(({height}/100)*(100-{yPad})))+{yOffset}"),

            "middleleft" =>
                ($"(w-((w/100)*(100-{xPad})))+{xOffset}",
                 $"({height}-({height}/2)-({fontsize}/2))+{yOffset}"),

            "middle" =>
                ($"(w-(w/2)-(text_w/2))+{xOffset}",
                 $"({height}-({height}/2)-({fontsize}/2))+{yOffset}"),

            "middleright" =>
                ($"(w-(w*{xPad}/100)-text_w)+{xOffset}",
                 $"({height}-({height}/2)-({fontsize}/2))+{yOffset}"),

            "bottomleft" =>
                ($"(w-((w/100)*(100-{xPad})))+{xOffset}",
                 $"({height}-({height}*({yPad}/100))-{fontsize})+{yOffset}"),

            "bottommiddle" =>
                ($"(w-(w/2)-(text_w/2))+{xOffset}",
                 $"({height}-({height}*({yPad}/100))-{fontsize})+{yOffset}"),

            "bottomright" =>
                ($"(w-(w*{xPad}/100)-text_w)+{xOffset}",
                 $"({height}-({height}*({yPad}/100))-{fontsize})+{yOffset}"),

            _ =>
                ($"{xOffset}", $"{yOffset}") // default origin
        };
    }

    private static (double x, double y) AnchorToEvaluated(string anchor, int width, double text_width, int height, int fontsize, int xOffset = 0, int yOffset = 0, double xPad = 5.0, double yPad = 5.0)
    {
        anchor = anchor.ToLowerInvariant().Replace(" ", "");

        return anchor switch
        {
            "topleft" =>
                ( (width-((width/100)*(100-xPad)))+xOffset,
                 (height-((height/100)*(100-yPad)))+yOffset ),

            "topmiddle" =>
                ( (width-(width/2)-(text_width/2))+xOffset,
                 (height-((height/100)*(100-yPad)))+yOffset ),

            "topright" =>
                ( (width-(width*xPad/100)-text_width)+xOffset,
                 (height-((height/100)*(100-yPad)))+yOffset ),

            "middleleft" =>
                ( (width-((width/100)*(100-xPad)))+xOffset,
                 (height-(height/2)-(fontsize/2))+yOffset ),

            "middle" =>
                ( (width-(width/2)-(text_width/2))+xOffset,
                 (height-(height/2)-(fontsize/2))+yOffset ),

            "middleright" =>
                ( (width-(width*xPad/100)-text_width)+xOffset,
                 (height-(height/2)-(fontsize/2))+yOffset ),

            "bottomleft" =>
                ( (width-((width/100)*(100-xPad)))+xOffset,
                 (height-(height*(yPad/100))-fontsize)+yOffset ),

            "bottommiddle" =>
                ( (width-(width/2)-(text_width/2))+xOffset,
                 (height-(height*(yPad/100))-fontsize)+yOffset ),

            "bottomright" =>
                ( (width-(width*xPad/100)-text_width)+xOffset,
                 (height-(height*(yPad/100))-fontsize)+yOffset ),

            _ =>
                (xOffset, yOffset) // default origin
        };
    }

    private static string ConvertTimeFormat (string formatter)
    {

        if(Globals.DEBUG > 1)
        {
            Console.WriteLine($"Input format is: {formatter}");
        }
        //Mapping of ISO 8601 components to strftime format codes
        Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HH"] = "%H",   // Hour (24-hour, leading zero)
                ["H"]  = "%#H",  // Hour (24-hour, no leading zero)
                ["hh"] = "%I",   // Hour (12-hour, leading zero)
                ["h"]  = "%#I",  // Hour (12-hour, no leading zero)

                ["mm"] = "%M",   // Minute (leading zero)
                ["m"]  = "%#M",  // Minute (no leading zero)

                ["ss"] = "%S",   // Second (leading zero)
                ["s"]  = "%#S",  // Second (no leading zero)

                ["tt"] = "%p",   // AM/PM

                ["yyyy"] = "%Y",  //Year (4 digits)
                ["yy"] = "%y",    //Year (2 digits)
                ["MMMM"] = "%B",  //Full month name
                ["MMM"] = "%b",   //Abbreviated month name
                ["MM"] = "%m",    //Month (2 digits)
                ["M"] = "%#m",    //Month (no leading 0)
                ["dddd"] = "%A",  //Full weekday name
                ["ddd"] = "%a",   //Abbreviated weekday name
                ["dd"] = "%d",    //Day of the month (2 digits)
                ["d"] = "%#d"    //Day of the month (no leading 0)
            };
        
        //Replace components with their strftime equivalents
        string strftimeFormat = "";
        int len = formatter.Length;
        string newstring = "";
        char currChar,lastChar = '\0';

        for (var i = 0; i < len; i++) {
            currChar = formatter[i];
            
            //first char
            if (!string.IsNullOrEmpty(newstring) && currChar == lastChar) {
                newstring += currChar;
            } else {
                if (!string.IsNullOrEmpty(newstring) && Map.ContainsKey(newstring)) {
                    if(Globals.DEBUG > 1)
                    {
                        Console.WriteLine($"{Globals.DEBUG_LEVEL}: Match found for '{newstring}' -> '({Map[newstring]})'");
                    }
                    strftimeFormat += Map[newstring];
                } else if (!string.IsNullOrEmpty(newstring)) {
                    if(Globals.DEBUG > 1)
                    {
                        Console.WriteLine($"No match for '{newstring}'. Keeping as is.");
                    }
                    strftimeFormat += newstring;
                }
                newstring = currChar.ToString();
            }
            lastChar = currChar;
        }

        //Final replacement for the last collected string
        if (Map.ContainsKey(newstring)) {
            if(Globals.DEBUG > 1)
            {
                 Console.WriteLine($"Final match for '{newstring}' -> '{Map[newstring]})'");
            }
            strftimeFormat += Map[newstring];
        } else {
            if(Globals.DEBUG > 1)
            {
                 Console.WriteLine($"No match for final '{newstring}'. Keeping as is.");
            }
            strftimeFormat += newstring;
        }

        if(Globals.DEBUG > 1)
        {
            Console.WriteLine($"New datetime format string is: {strftimeFormat}");
        }
        return strftimeFormat;
    }

    private static void ApplyVerticalOffset(DrawText dt, int delta)
    {
        if (Math.Abs(delta) < 1) return;

        dt.YCoord += delta;  // keep numeric coord in sync

        var sign = delta >= 0 ? "+" : "-";
        var magnitude = Math.Abs(delta).ToString(
            CultureInfo.InvariantCulture);

        dt.YExpr = $"({dt.YExpr}){sign}{magnitude}";
    }

    private static double EstimateTextWidth(DrawText dt)
    {
        if (string.IsNullOrEmpty(dt.Text))
            return 0;

        // Same heuristic you’re using elsewhere: width ≈ fontSize * 0.5 * characters
        return dt.FontSize * 0.5 * dt.Text.Length;
    }

    private static int Factorial(int n)
    {
        int result = 1;
        for (int i = 2; i <= n; i++)
        {
            // Guard very loosely against overflow – with your typical
            // small c (<= 5), this will never hit.
            if (result > int.MaxValue / i)
                return int.MaxValue;

            result *= i;
        }
        return result;
    }

    private static void FixOverlappingText(Dictionary<string, List<DrawText>> filters)
    {
        foreach (var kvp in filters)
        {
            string pos = kvp.Key;
            var list = kvp.Value;
            int c = list.Count;

            if (c <= 1)
                continue;

            if (Globals.DEBUG > 1)
            {
                Console.WriteLine($"{Globals.DEBUG_LEVEL}: {c} number of texts at position {pos}");
            }

            int maxIterations = Factorial(c);
            int iteration = 0;
            bool changed;

            do
            {
                changed = false;

                if (Globals.DEBUG > 2)
                {
                    Console.WriteLine(
                        $"{Globals.DEBUG_LEVEL}: Overlap pass {iteration + 1} (max {maxIterations}) for position {pos}");
                }

                // Check every pair (i, j) with j < i
                for (int i = 0; i < c; i++)
                {
                    var currText = list[i];

                    for (int j = 0; j < i; j++)
                    {
                        var prevText = list[j];

                        // --- vertical overlap ---
                        int yDiff = Math.Abs(currText.YCoord - prevText.YCoord);
                        bool verticalOverlap = yDiff < (currText.FontSize + 10);

                        // --- time overlap (same as before) ---
                        double currStart = currText.Start ?? -1;
                        double prevStart = prevText.Start ?? -1;
                        double prevEnd   = prevText.End   ?? double.MaxValue;

                        bool timeOverlap =
                            currStart < 0 ||
                            prevStart < 0 ||
                            (currStart >= prevStart && currStart <= prevEnd);

                        // --- horizontal overlap ---
                        double currWidth = EstimateTextWidth(currText);
                        double prevWidth = EstimateTextWidth(prevText);

                        double currLeft  = currText.XCoord;
                        double currRight = currLeft + currWidth;

                        double prevLeft  = prevText.XCoord;
                        double prevRight = prevLeft + prevWidth;

                        bool horizontalOverlap =
                            currLeft < prevRight &&
                            prevLeft < currRight;

                        if (Globals.DEBUG > 2)
                        {
                            Console.WriteLine(
                                $"{Globals.DEBUG_LEVEL}: Checking overlap. pos={pos}, " +
                                $"i={i} (x=[{currLeft},{currRight}], y={currText.YCoord}, start={currStart}), " +
                                $"j={j} (x=[{prevLeft},{prevRight}], y={prevText.YCoord}, start={prevStart}, end={prevEnd}), " +
                                $"vert={verticalOverlap}, horiz={horizontalOverlap}, time={timeOverlap}");
                        }

                        if (verticalOverlap && horizontalOverlap && timeOverlap)
                        {
                            int delta = (currText.FontSize + 10) - yDiff;

                            if (pos.Contains("bottom", StringComparison.OrdinalIgnoreCase))
                            {
                                // Bottom anchors: move the previous text *up*
                                ApplyVerticalOffset(prevText, -delta);

                                if (Globals.DEBUG > 2)
                                {
                                    Console.WriteLine(
                                        $"{Globals.DEBUG_LEVEL}: Adjusted previous text #{j} at {pos} upward by {delta}. New yCoord: {prevText.YCoord}");
                                }
                            }
                            else
                            {
                                // Other anchors: move the current text *down*
                                ApplyVerticalOffset(currText, delta);

                                if (Globals.DEBUG > 2)
                                {
                                    Console.WriteLine(
                                        $"{Globals.DEBUG_LEVEL}: Adjusted current text #{i} at {pos} downward by {delta}. New yCoord: {currText.YCoord}");
                                }
                            }

                            changed = true;
                        }
                    }
                }

                iteration++;
            }
            while (changed && iteration < maxIterations);

            if (Globals.DEBUG > 1)
            {
                Console.WriteLine(
                    $"{Globals.DEBUG_LEVEL}: Finished overlap adjustment for position {pos} after {iteration} passes (changed={changed}).");
            }
        }
    }

}

