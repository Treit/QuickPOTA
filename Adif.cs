using System.Globalization;
using System.Text;

namespace QuickPOTA;

internal static class Adif
{
    private const string ProgramId = "QuickPOTA";
    private const string AdifVer = "3.1.4";

    public static void Write(string path, string myCall, string parkRef, string? parkName, IEnumerable<Qso> qsos, bool append)
    {
        var sb = new StringBuilder();
        var writeHeader = !append || !File.Exists(path);

        if (writeHeader)
        {
            sb.Append(Header());
        }

        foreach (var q in qsos)
        {
            sb.Append(Record(q, myCall, parkRef, parkName));
        }

        if (append && File.Exists(path))
        {
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
        }
        else
        {
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }

    private static string Header()
    {
        var now = DateTime.UtcNow;
        var sb = new StringBuilder();
        sb.AppendLine("QuickPOTA ADIF export");
        sb.Append(Field("ADIF_VER", AdifVer));
        sb.Append(Field("PROGRAMID", ProgramId));
        sb.Append(Field("CREATED_TIMESTAMP", now.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture)));
        sb.AppendLine("<EOH>");
        return sb.ToString();
    }

    private static string Record(Qso q, string myCall, string parkRef, string? parkName)
    {
        var sb = new StringBuilder();
        sb.Append(Field("CALL", q.Call));
        sb.Append(Field("QSO_DATE", q.TimeUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
        sb.Append(Field("TIME_ON", q.TimeUtc.ToString("HHmmss", CultureInfo.InvariantCulture)));
        sb.Append(Field("BAND", q.Band));
        sb.Append(Field("FREQ", q.FreqMhz.ToString("0.000000", CultureInfo.InvariantCulture)));
        sb.Append(Field("MODE", q.Mode));
        if (!string.IsNullOrEmpty(q.Submode))
        {
            sb.Append(Field("SUBMODE", q.Submode));
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
        return string.Create(CultureInfo.InvariantCulture, $"<{name}:{bytes}>{value} ");
    }

    public static (string? LastCall, double? FreqMhz, string? Mode, string? Submode, string? ParkRef, string? MyCall, DateTime? LastTime) PeekLast(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var eohEnd = FindEohEnd(bytes);
        var bodyStart = eohEnd >= 0 ? eohEnd : 0;

        var recordRanges = SplitByEor(bytes, bodyStart);
        for (var i = recordRanges.Count - 1; i >= 0; i--)
        {
            var (offset, length) = recordRanges[i];
            if (length == 0)
            {
                continue;
            }
            var fields = ParseFields(bytes, offset, length);
            if (fields.Count == 0)
            {
                continue;
            }

            fields.TryGetValue("CALL", out var call);
            fields.TryGetValue("FREQ", out var freq);
            fields.TryGetValue("MODE", out var mode);
            fields.TryGetValue("SUBMODE", out var submode);
            fields.TryGetValue("MY_SIG_INFO", out var park);
            fields.TryGetValue("STATION_CALLSIGN", out var mycall);
            fields.TryGetValue("QSO_DATE", out var date);
            fields.TryGetValue("TIME_ON", out var time);

            DateTime? dt = null;
            if (date is not null && time is not null &&
                DateTime.TryParseExact(date + time.PadRight(6, '0')[..6], "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                dt = parsed;
            }
            double? f = null;
            if (freq is not null && double.TryParse(freq, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
            {
                f = fv;
            }
            return (call, f, mode, submode, park, mycall, dt);
        }
        return (null, null, null, null, null, null, null);
    }

    private static int FindEohEnd(byte[] bytes)
    {
        ReadOnlySpan<byte> marker = "<EOH>"u8;
        for (var i = bytes.Length - marker.Length; i >= 0; i--)
        {
            var slice = bytes.AsSpan(i, marker.Length);
            if (EqualsIgnoreCaseAscii(slice, marker))
            {
                return i + marker.Length;
            }
        }
        return -1;
    }

    private static List<(int Offset, int Length)> SplitByEor(byte[] bytes, int start)
    {
        var ranges = new List<(int, int)>();
        ReadOnlySpan<byte> marker = "<EOR>"u8;
        var cursor = start;
        var i = start;
        while (i <= bytes.Length - marker.Length)
        {
            if (bytes[i] == (byte)'<' && EqualsIgnoreCaseAscii(bytes.AsSpan(i, marker.Length), marker))
            {
                ranges.Add((cursor, i - cursor));
                i += marker.Length;
                cursor = i;
            }
            else
            {
                i++;
            }
        }
        if (cursor < bytes.Length)
        {
            ranges.Add((cursor, bytes.Length - cursor));
        }
        return ranges;
    }

    private static bool EqualsIgnoreCaseAscii(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            var ca = a[i];
            var cb = b[i];
            if (ca >= (byte)'A' && ca <= (byte)'Z')
            {
                ca = (byte)(ca + 32);
            }
            if (cb >= (byte)'A' && cb <= (byte)'Z')
            {
                cb = (byte)(cb + 32);
            }
            if (ca != cb)
            {
                return false;
            }
        }
        return true;
    }

    private static Dictionary<string, string> ParseFields(byte[] bytes, int offset, int length)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var end = offset + length;
        var i = offset;
        while (i < end)
        {
            if (bytes[i] != (byte)'<')
            {
                i++;
                continue;
            }
            var gt = IndexOf(bytes, (byte)'>', i + 1, end);
            if (gt < 0)
            {
                break;
            }
            var tagLen = gt - (i + 1);
            var tagText = Encoding.ASCII.GetString(bytes, i + 1, tagLen);
            var parts = tagText.Split(':');
            if (parts.Length < 2 ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var valLen))
            {
                i = gt + 1;
                continue;
            }
            var valStart = gt + 1;
            if (valStart + valLen > end)
            {
                break;
            }
            var value = Encoding.UTF8.GetString(bytes, valStart, valLen);
            dict[parts[0].ToUpperInvariant()] = value;
            i = valStart + valLen;
        }
        return dict;
    }

    private static int IndexOf(byte[] bytes, byte target, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (bytes[i] == target)
            {
                return i;
            }
        }
        return -1;
    }
}
