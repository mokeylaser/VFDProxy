using System.Text;
using System.Text.RegularExpressions;
using VFDProxy.Models;

namespace VFDProxy.Parsing;

/// <summary>
/// Fast, single-pass G-code line parser.
/// Not a full G-code interpreter — tuned for the command patterns Candle actually sends.
/// </summary>
public static class GCodeParser
{
    // Matches (parenthesis comments) to strip them
    private static readonly Regex ParenComment = new(@"\([^)]*\)", RegexOptions.Compiled);
    // Matches ;semicolon comments to end of line
    private static readonly Regex SemiComment  = new(@";.*$",       RegexOptions.Compiled);
    // Matches S followed by a decimal number
    private static readonly Regex SWordPattern = new(@"\bS([0-9]+(?:\.[0-9]*)?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Matches T followed by digits
    private static readonly Regex TWordPattern = new(@"\bT[0-9]+\b",                RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ParsedGCodeLine Parse(string rawLine, AppConfig config)
    {
        var normalized = Normalize(rawLine);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ParsedGCodeLine(
                Raw: rawLine, Routing: LineRouting.PassThrough,
                HasM3: false, HasM4: false, HasM5: false, HasM6: false,
                HasM0: false, HasM1: false, HasToolChange: false,
                HasSWord: false, SpindleRpm: 0, HasCoolant: false,
                Normalized: normalized);
        }

        // Tokenize on whitespace for M/S word detection
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        bool hasM3 = false, hasM4 = false, hasM5 = false, hasM6 = false;
        bool hasM0 = false, hasM1 = false, hasCoolant = false;
        bool hasTool = TWordPattern.IsMatch(normalized);
        bool hasSWord = false;
        double spindleRpm = 0;

        foreach (var tok in tokens)
        {
            if (tok == "M3" || tok == "M03") hasM3 = true;
            else if (tok == "M4" || tok == "M04") hasM4 = true;
            else if (tok == "M5" || tok == "M05") hasM5 = true;
            else if (tok == "M6" || tok == "M06") hasM6 = true;
            else if (tok == "M0" || tok == "M00") hasM0 = true;
            else if (tok == "M1" || tok == "M01") hasM1 = true;
            else if (tok == "M7" || tok == "M07" ||
                     tok == "M8" || tok == "M08" ||
                     tok == "M9" || tok == "M09") hasCoolant = true;
        }

        // S word can be standalone or attached (e.g. "S12000" or "M3 S12000")
        var sMatch = SWordPattern.Match(normalized);
        if (sMatch.Success && double.TryParse(sMatch.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double rpm))
        {
            hasSWord  = true;
            spindleRpm = rpm;
        }

        // Determine routing
        LineRouting routing;

        bool isSpindleCommand = hasM3 || hasM4 || hasM5 || hasSWord;
        bool isToolChange     = hasTool || hasM6;
        bool isPause          = (hasM0 || hasM1) && config.TreatM0M1AsPause;
        bool isCoolant        = hasCoolant && config.StripCoolantCommands;

        if (config.StripSpindleCommands && isSpindleCommand)
        {
            // A line might be ONLY spindle (e.g. "M3 S12000") — intercept entirely.
            // A line might mix motion + spindle (e.g. "G1 X10 F500 M3 S12000") —
            // we still forward it but strip the M3/S portions.
            bool hasMoveWords = HasMotionWords(tokens);
            routing = hasMoveWords ? LineRouting.ForwardToGrbl : LineRouting.InterceptSpindle;
        }
        else if (config.StripToolChanges && isToolChange)
        {
            routing = LineRouting.InterceptToolChange;
        }
        else if (isPause)
        {
            routing = LineRouting.InterceptPause;
        }
        else if (isCoolant)
        {
            routing = LineRouting.InterceptCoolant;
        }
        else
        {
            routing = LineRouting.ForwardToGrbl;
        }

        return new ParsedGCodeLine(
            Raw: rawLine,
            Routing: routing,
            HasM3: hasM3,
            HasM4: hasM4,
            HasM5: hasM5,
            HasM6: hasM6,
            HasM0: hasM0,
            HasM1: hasM1,
            HasToolChange: hasTool,
            HasSWord: hasSWord,
            SpindleRpm: spindleRpm,
            HasCoolant: hasCoolant,
            Normalized: normalized);
    }

    /// <summary>
    /// Strips the spindle-related tokens from a line that also has motion words,
    /// so we can forward the motion part cleanly to GRBL.
    /// </summary>
    public static string StripSpindleTokens(string normalized)
    {
        var sb = new StringBuilder();
        foreach (var tok in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (tok is "M3" or "M03" or "M4" or "M04" or "M5" or "M05") continue;
            if (SWordPattern.IsMatch(tok)) continue;
            sb.Append(tok);
            sb.Append(' ');
        }
        return sb.ToString().TrimEnd();
    }

    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);

    private static string Normalize(string raw)
    {
        var s = ParenComment.Replace(raw, " ");
        s = SemiComment.Replace(s, string.Empty);
        s = s.ToUpperInvariant().Trim();
        // Collapse multiple spaces (single-pass)
        s = MultiSpace.Replace(s, " ");
        return s;
    }

    private static readonly HashSet<string> MotionPrefixes =
        new(StringComparer.OrdinalIgnoreCase) { "G", "X", "Y", "Z", "A", "B", "F", "I", "J", "K", "R" };

    private static bool HasMotionWords(string[] tokens)
    {
        foreach (var tok in tokens)
        {
            if (tok.Length < 1) continue;
            var prefix = tok[..1];
            if (MotionPrefixes.Contains(prefix)) return true;
        }
        return false;
    }
}
