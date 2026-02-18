namespace VFDProxy.Models;

public enum LineRouting
{
    ForwardToGrbl,
    InterceptSpindle,
    InterceptToolChange,
    InterceptPause,
    InterceptCoolant,
    PassThrough        // edge-case: blank / comment-only lines
}

public sealed record ParsedGCodeLine(
    string      Raw,
    LineRouting Routing,
    bool        HasM3,
    bool        HasM4,
    bool        HasM5,
    bool        HasM6,
    bool        HasM0,
    bool        HasM1,
    bool        HasToolChange,
    bool        HasSWord,
    double      SpindleRpm,
    bool        HasCoolant,
    string      Normalized   // upper-case, comment-stripped, trimmed
);
