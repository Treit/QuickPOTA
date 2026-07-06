namespace QuickPOTA;

internal sealed class Qso
{
    public required string Call { get; init; }
    public string RstSent { get; init; } = "599";
    public string RstRcvd { get; init; } = "599";
    public string? Qth { get; init; }
    public string? Notes { get; init; }
    public required DateTime TimeUtc { get; set; }
    public required double FreqMhz { get; init; }
    public required string Mode { get; init; }
    public string? Submode { get; init; }
    public required string Band { get; init; }
}
