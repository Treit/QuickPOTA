namespace QuickPOTA;

internal enum ActivationKind
{
    Pota,
    Event,
}

internal sealed record ActivationContext(
    string OperatorCall,
    string StationCall,
    string Sig,
    string SigInfo,
    string? SigInfoDisplay,
    string? MyGridSquare)
{
    public bool IsPota => string.Equals(Sig, "POTA", StringComparison.OrdinalIgnoreCase);
}
