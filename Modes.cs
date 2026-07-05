namespace QuickPOTA;

internal static class Modes
{
    public static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "CW", "SSB", "USB", "LSB", "AM", "FM", "DIGITAL", "DIGITALVOICE",
        "FT8", "FT4", "PSK", "PSK31", "PSK63", "RTTY", "JT65", "JT9",
        "JS8", "MFSK", "OLIVIA", "CONTESTI", "FSK441", "MSK144", "Q65",
        "PACKET", "DMR", "DSTAR", "C4FM", "FUSION", "SSTV", "HELL",
        "ATV", "FAX", "ROS", "THROB",
    };

    public static string Normalize(string mode)
    {
        var up = mode.ToUpperInvariant();
        return up switch
        {
            "USB" or "LSB" => "SSB",
            "FUSION" => "C4FM",
            _ => up,
        };
    }

    public static string? SubmodeFor(string modeInput)
    {
        var up = modeInput.ToUpperInvariant();
        return up switch
        {
            "USB" => "USB",
            "LSB" => "LSB",
            _ => null,
        };
    }
}
