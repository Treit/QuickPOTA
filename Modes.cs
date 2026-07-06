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

    public static (string Mode, string? Submode) Normalize(string input)
    {
        var up = input.ToUpperInvariant();
        return up switch
        {
            "USB" => ("SSB", "USB"),
            "LSB" => ("SSB", "LSB"),
            "FUSION" => ("C4FM", null),
            _ => (up, null),
        };
    }
}
