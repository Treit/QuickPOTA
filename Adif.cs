using System.Text;

namespace QuickPOTA;

internal static class Adif
{
    private const string ProgramId = "QuickPOTA";
    private const string AdifVer = "3.1.4";

    public static void Write(string path, string myCall, string parkRef, string? parkName, IEnumerable<Qso> qsos, bool append)
    {
        var sb = new StringBuilder();

        if (!append || !File.Exists(path))
        {
            sb.Append(Header());
        }

        foreach (var q in qsos)
        {
            sb.Append(Record(q, myCall, parkRef, parkName));
        }

        if (append && File.Exists(path))
        {
            File.AppendAllText(path, sb.ToString(), Encoding.ASCII);
        }
        else
        {
            File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
        }
    }

    private static string Header()
    {
        var now = DateTime.UtcNow;
        var sb = new StringBuilder();
        sb.AppendLine($"QuickPOTA ADIF export");
        sb.Append(Field("ADIF_VER", AdifVer));
        sb.Append(Field("PROGRAMID", ProgramId));
        sb.Append(Field("CREATED_TIMESTAMP", now.ToString("yyyyMMdd HHmmss")));
        sb.AppendLine("<EOH>");
        return sb.ToString();
    }

    private static string Record(Qso q, string myCall, string parkRef, string? parkName)
    {
        var sb = new StringBuilder();
        sb.Append(Field("CALL", q.Call));
        sb.Append(Field("QSO_DATE", q.TimeUtc.ToString("yyyyMMdd")));
        sb.Append(Field("TIME_ON", q.TimeUtc.ToString("HHmmss")));
        sb.Append(Field("BAND", q.Band));
        sb.Append(Field("FREQ", q.FreqMhz.ToString("0.000000")));
        sb.Append(Field("MODE", q.Mode));
        var sub = Modes.SubmodeFor(q.Mode);
        if (sub is not null)
        {
            sb.Append(Field("SUBMODE", sub));
        }
        sb.Append(Field("RST_SENT", q.RstSent));
        sb.Append(Field("RST_RCVD", q.RstRcvd));
        sb.Append(Field("STATION_CALLSIGN", myCall));
        sb.Append(Field("OPERATOR", myCall));
        sb.Append(Field("MY_SIG", "POTA"));
        sb.Append(Field("MY_SIG_INFO", parkRef));
        if (!string.IsNullOrWhiteSpace(q.Qth))
        {
            sb.Append(Field("STATE", q.Qth));
            sb.Append(Field("QTH", q.Qth));
        }
        if (!string.IsNullOrWhiteSpace(q.Notes))
        {
            sb.Append(Field("COMMENT", q.Notes));
            sb.Append(Field("NOTES", q.Notes));
        }
        if (!string.IsNullOrWhiteSpace(parkName))
        {
            sb.Append(Field("SIG_INFO", parkName));
        }
        sb.AppendLine("<EOR>");
        return sb.ToString();
    }

    private static string Field(string name, string value)
    {
        var bytes = Encoding.UTF8.GetByteCount(value);
        return $"<{name}:{bytes}>{value} ";
    }

    public static (string? LastCall, double? FreqMhz, string? Mode, string? ParkRef, string? MyCall, DateTime? LastTime) PeekLast(string path)
    {
        var text = File.ReadAllText(path);
        var idx = text.LastIndexOf("<EOH>", StringComparison.OrdinalIgnoreCase);
        var body = idx >= 0 ? text[(idx + 5)..] : text;
        var records = body.Split(new[] { "<EOR>", "<eor>", "<Eor>" }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = records.Length - 1; i >= 0; i--)
        {
            var rec = records[i];
            if (string.IsNullOrWhiteSpace(rec)) continue;
            var fields = ParseFields(rec);
            fields.TryGetValue("CALL", out var call);
            fields.TryGetValue("FREQ", out var freq);
            fields.TryGetValue("MODE", out var mode);
            fields.TryGetValue("MY_SIG_INFO", out var park);
            fields.TryGetValue("STATION_CALLSIGN", out var mycall);
            fields.TryGetValue("QSO_DATE", out var date);
            fields.TryGetValue("TIME_ON", out var time);
            DateTime? dt = null;
            if (date is not null && time is not null &&
                DateTime.TryParseExact(date + time.PadRight(6, '0')[..6], "yyyyMMddHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                dt = parsed;
            }
            double? f = null;
            if (freq is not null && double.TryParse(freq, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fv))
            {
                f = fv;
            }
            return (call, f, mode, park, mycall, dt);
        }
        return (null, null, null, null, null, null);
    }

    private static Dictionary<string, string> ParseFields(string rec)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < rec.Length)
        {
            var lt = rec.IndexOf('<', i);
            if (lt < 0) break;
            var gt = rec.IndexOf('>', lt);
            if (gt < 0) break;
            var tag = rec[(lt + 1)..gt];
            var parts = tag.Split(':');
            if (parts.Length < 2)
            {
                i = gt + 1;
                continue;
            }
            if (!int.TryParse(parts[1], out var len))
            {
                i = gt + 1;
                continue;
            }
            var start = gt + 1;
            if (start + len > rec.Length) break;
            var val = rec.Substring(start, len);
            dict[parts[0].ToUpperInvariant()] = val;
            i = start + len;
        }
        return dict;
    }
}
