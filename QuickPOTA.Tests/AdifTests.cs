namespace QuickPOTA.Tests;

public sealed class AdifTests
{
    [Fact]
    public void WriteUsesPotaRefFieldWithoutLegacySigFields()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".adi");

        try
        {
            Adif.Write(path, "AE7XI", "US-3166", [CreateQso()], append: false);
            var text = File.ReadAllText(path);

            Assert.Contains("<MY_POTA_REF:7>US-3166", text);
            Assert.DoesNotContain("MY_SIG", text);
            Assert.DoesNotContain("MY_SIG_INFO", text);
            Assert.DoesNotContain("SIG_INFO", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PeekLastReadsModernMyPotaRef()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".adi");

        try
        {
            File.WriteAllText(
                path,
                "QuickPOTA ADIF export\r\n<ADIF_VER:5>3.1.4 <EOH>\r\n<CALL:4>N7RV <FREQ:9>14.059000 <MODE:2>CW <MY_POTA_REF:7>US-3166 <STATION_CALLSIGN:5>AE7XI <QSO_DATE:8>20260719 <TIME_ON:6>221920 <EOR>\r\n");

            var peek = Adif.PeekLast(path);

            Assert.Equal("US-3166", peek.ParkRef);
            Assert.Equal("AE7XI", peek.MyCall);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PeekLastStillReadsLegacyMySigInfo()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".adi");

        try
        {
            File.WriteAllText(
                path,
                "QuickPOTA ADIF export\r\n<ADIF_VER:5>3.1.4 <EOH>\r\n<CALL:4>N7RV <FREQ:9>14.059000 <MODE:2>CW <MY_SIG:4>POTA <MY_SIG_INFO:7>US-3166 <STATION_CALLSIGN:5>AE7XI <QSO_DATE:8>20260719 <TIME_ON:6>221920 <EOR>\r\n");

            var peek = Adif.PeekLast(path);

            Assert.Equal("US-3166", peek.ParkRef);
            Assert.Equal("AE7XI", peek.MyCall);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Qso CreateQso() => new()
    {
        Call = "N7RV",
        RstSent = "599",
        RstRcvd = "559",
        Qth = "WA",
        TimeUtc = new DateTime(2026, 7, 19, 22, 19, 20, DateTimeKind.Utc),
        FreqMhz = 14.059,
        Mode = "CW",
        Band = "20m",
    };
}
