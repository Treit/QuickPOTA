namespace QuickPOTA;

internal static class Bands
{
    private static readonly (double Low, double High, string Name)[] Ranges =
    [
        (0.1357, 0.1378, "2200m"),
        (0.472, 0.479, "630m"),
        (1.8, 2.0, "160m"),
        (3.5, 4.0, "80m"),
        (5.06, 5.45, "60m"),
        (7.0, 7.3, "40m"),
        (10.1, 10.15, "30m"),
        (14.0, 14.35, "20m"),
        (18.068, 18.168, "17m"),
        (21.0, 21.45, "15m"),
        (24.89, 24.99, "12m"),
        (28.0, 29.7, "10m"),
        (50.0, 54.0, "6m"),
        (144.0, 148.0, "2m"),
        (222.0, 225.0, "1.25m"),
        (420.0, 450.0, "70cm"),
        (902.0, 928.0, "33cm"),
        (1240.0, 1300.0, "23cm"),
        (2300.0, 2450.0, "13cm"),
    ];

    public static string? FromMhz(double mhz)
    {
        foreach (var r in Ranges)
        {
            if (mhz >= r.Low && mhz <= r.High)
            {
                return r.Name;
            }
        }
        return null;
    }
}
