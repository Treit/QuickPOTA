using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace QuickPOTA;

internal static class Program
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level CLI handler prints message and exits.")]
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length > 0 && (args[0] == "-h" || args[0] == "--help" || args[0] == "/?"))
            {
                PrintHelp();
                return 0;
            }

            var existingFile = args.Length > 0 ? args[0] : null;
            if (existingFile is not null && !File.Exists(existingFile))
            {
                Console.Error.WriteLine($"File not found: {existingFile}");
                return 1;
            }

            var parks = new Parks();
            var loadTask = parks.LoadAsync();

            var session = existingFile is not null
                ? StartAppend(existingFile, parks, loadTask)
                : await StartNewAsync(parks, loadTask);

            RunLoop(session);
            session.Save();
            Console.WriteLine();
            Console.WriteLine($"Wrote {session.Qsos.Count} QSO(s) to {session.OutputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("QuickPOTA - quickly convert a paper POTA log to ADIF");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  quickpota                 Start a new activation and prompt for details.");
        Console.WriteLine("  quickpota <file.adi>      Append new QSOs to the given ADIF file.");
        Console.WriteLine();
        Console.WriteLine("In the QSO prompt:");
        Console.WriteLine("  [time] <call> [time] [sent/rcvd | rcvd] [time] [qth] [notes]   Log a contact.");
        Console.WriteLine("     e.g. 'NF7N 55N WA'          -> RST_SENT=599 RST_RCVD=559 STATE=WA");
        Console.WriteLine("     e.g. ':53 NF7N 55N WA'      -> TIME_ON uses last QSO hour, rolling over when needed");
        Console.WriteLine("     e.g. 'WM2V 55N/44N AZ'      -> RST_SENT=559 RST_RCVD=449 STATE=AZ");
        Console.WriteLine("  <freq> [mode]                Change frequency (KHz or MHz), optional mode");
        Console.WriteLine("  <mode>                       Change mode (CW SSB FT8 FM ...)");
        Console.WriteLine("  Q                            Quit and write the ADIF");
        Console.WriteLine();
        Console.WriteLine("RST cut numbers are translated (T=0 N=9 A=1 E=5 O=0 U=2 V=3 B=7 D=8).");
    }

    private static Session StartAppend(string path, Parks parks, Task loadTask)
    {
        var peek = Adif.PeekLast(path);
        if (peek.ParkRef is null || peek.MyCall is null || peek.FreqMhz is null || peek.Mode is null)
        {
            throw new InvalidOperationException("Existing ADIF does not contain enough context (park, callsign, freq, mode) to append.");
        }

        WaitForLoad(loadTask);
        var parkName = parks.Lookup(peek.ParkRef);

        Console.WriteLine($"Append mode: {path}");
        Console.WriteLine($"  Operator: {peek.MyCall}");
        Console.WriteLine($"  Park:     {peek.ParkRef}{(parkName is null ? "" : $" ({parkName})")}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Freq:     {peek.FreqMhz:0.000} MHz"));
        Console.WriteLine($"  Mode:     {peek.Mode}{(peek.Submode is null ? "" : $" ({peek.Submode})")}");
        Console.WriteLine();

        return new Session
        {
            OutputPath = path,
            Append = true,
            MyCall = peek.MyCall,
            ParkRef = peek.ParkRef,
            ParkName = parkName,
            CurrentFreqMhz = peek.FreqMhz.Value,
            CurrentMode = peek.Mode,
            CurrentSubmode = peek.Submode,
            AppendTimeMode = true,
            StartUtc = peek.LastTime ?? DateTime.UtcNow,
            EndUtc = DateTime.UtcNow,
        };
    }

    private static Task<Session> StartNewAsync(Parks parks, Task loadTask)
    {
        Console.WriteLine("QuickPOTA - new activation");
        Console.WriteLine();

        var myCall = PromptRequired("Your callsign", static s => IsValidCall(s) ? null : "Enter a valid callsign.").ToUpperInvariant();

        WaitForLoad(loadTask);
        if (parks.IsLoaded)
        {
            Console.WriteLine($"[Loaded {parks.Count} POTA park references from {parks.Source}]");
        }
        else
        {
            Console.WriteLine("[POTA park database not available; park names will be blank]");
        }

        string parkRef;
        string? parkName;
        while (true)
        {
            parkRef = PromptRequired("Park reference (e.g. US-3166)", null).ToUpperInvariant();
            parkName = parks.Lookup(parkRef);
            if (parkName is not null)
            {
                Console.WriteLine($"  -> {parkName}");
                break;
            }
            if (!parks.IsLoaded)
            {
                break;
            }
            Console.Write($"  '{parkRef}' not found in database. Use anyway? [y/N] ");
            var ans = Console.ReadLine();
            if (ans is not null && (ans.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) || ans.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
        }

        var date = PromptDate("Activation date (UTC, YYYY-MM-DD or today)");
        var startUtc = PromptTime("Start time (UTC, HHMM)", date);
        var endUtc = PromptTime("End time (UTC, HHMM)", date);
        if (endUtc < startUtc)
        {
            endUtc = endUtc.AddDays(1);
        }

        Console.WriteLine();
        Console.WriteLine("Tip: at the QSO prompt you can also enter a frequency (KHz or MHz)");
        Console.WriteLine("     to switch, e.g. '14030 CW' or '146.520 FM', or just a mode name.");
        Console.WriteLine();

        var freq = PromptFrequency("Starting frequency (KHz or MHz)");
        var (mode, submode) = PromptMode("Starting mode");

        return Task.FromResult(new Session
        {
            OutputPath = DefaultOutputPath(parkRef, date),
            Append = false,
            MyCall = myCall,
            ParkRef = parkRef,
            ParkName = parkName,
            CurrentFreqMhz = freq,
            CurrentMode = mode,
            CurrentSubmode = submode,
            StartUtc = startUtc,
            EndUtc = endUtc,
        });
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Background download failure is expected offline; embedded fallback covers it.")]
    private static void WaitForLoad(Task loadTask)
    {
        try
        {
            loadTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }
    }

    private static string DefaultOutputPath(string parkRef, DateTime date)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{parkRef}-{date:yyyyMMdd}.adi");
        return Path.Combine(Environment.CurrentDirectory, name);
    }

    private static void RunLoop(Session session)
    {
        Console.WriteLine("Ready. Type 'Q' <enter> to quit and write the ADIF.");
        Console.WriteLine();
        PrintStatus(session);

        while (true)
        {
            Console.Write($"[{session.Qsos.Count + 1}] > ");
            var line = Console.ReadLine();
            if (line is null)
            {
                break;
            }
            var input = line.Trim();
            if (input.Length == 0)
            {
                continue;
            }

            if (input.Equals("Q", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("QUIT", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("EXIT", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (input.Equals("?", StringComparison.Ordinal) || input.Equals("HELP", StringComparison.OrdinalIgnoreCase))
            {
                PrintStatus(session);
                Console.WriteLine("  Enter '[time] <call> [time] [sent/rcvd | rcvd] [time] [qth] [notes]' to log a QSO.");
                Console.WriteLine("  Enter a frequency (KHz or MHz) optionally followed by a mode.");
                Console.WriteLine("  Enter a mode name (CW SSB FT8 FM ...) to switch modes.");
                Console.WriteLine("  Enter 'Q' to quit.");
                continue;
            }

            var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (TryParseFrequency(tokens[0], out var newFreq))
            {
                session.CurrentFreqMhz = newFreq;
                if (tokens.Length > 1 && Modes.Known.Contains(tokens[1]))
                {
                    var (m, sm) = Modes.Normalize(tokens[1]);
                    session.CurrentMode = m;
                    session.CurrentSubmode = sm;
                }
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  -> QSY {session.CurrentFreqMhz:0.000} MHz {session.CurrentMode} ({Bands.FromMhz(session.CurrentFreqMhz) ?? "?"})"));
                continue;
            }

            if (tokens.Length == 1 && Modes.Known.Contains(tokens[0]))
            {
                var (m, sm) = Modes.Normalize(tokens[0]);
                session.CurrentMode = m;
                session.CurrentSubmode = sm;
                Console.WriteLine($"  -> Mode {session.CurrentMode}{(sm is null ? "" : $" ({sm})")}");
                continue;
            }

            var qso = BuildQso(tokens, session);
            if (qso is null)
            {
                Console.WriteLine("  ! Could not parse. Expected: [time] <call> [rst] [time] [qth] [notes...]");
                continue;
            }
            session.Qsos.Add(qso);
            session.NoteLoggedQso(qso);
            Console.WriteLine(LoggedMessage(qso));
        }
    }

    internal static string LoggedMessage(Qso qso)
    {
        var time = qso.HasExplicitTime ? qso.TimeUtc.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z " : "";
        return $"  logged {qso.Call} {time}{qso.RstSent}/{qso.RstRcvd}{(string.IsNullOrEmpty(qso.Qth) ? "" : " " + qso.Qth)}";
    }

    internal static Qso? BuildQso(string[] tokens, Session session)
    {
        var i = 0;
        DateTime? entryTime = null;

        if (TryParseQsoEntryTime(tokens[i], session, out var parsedTime))
        {
            entryTime = parsedTime;
            i++;
        }

        if (i >= tokens.Length)
        {
            return null;
        }

        var call = tokens[i].ToUpperInvariant();

        if (!IsValidCall(call))
        {
            return null;
        }

        i++;

        if (i < tokens.Length && entryTime is null && TryParseQsoEntryTime(tokens[i], session, out parsedTime))
        {
            entryTime = parsedTime;
            i++;
        }

        var rstSent = DefaultRstFor(session.CurrentMode);
        var rstRcvd = rstSent;
        string? qth = null;
        string? notes = null;

        if (i < tokens.Length && LooksLikeRstToken(tokens[i], session.CurrentMode))
        {
            var rstToken = tokens[i];
            var slash = rstToken.IndexOf('/');

            if (slash > 0 && slash < rstToken.Length - 1)
            {
                rstSent = NormalizeRst(rstToken[..slash], session.CurrentMode);
                rstRcvd = NormalizeRst(rstToken[(slash + 1)..], session.CurrentMode);
            }
            else
            {
                rstRcvd = NormalizeRst(rstToken, session.CurrentMode);
            }

            i++;
        }

        if (i < tokens.Length && entryTime is null && TryParseQsoEntryTime(tokens[i], session, out parsedTime))
        {
            entryTime = parsedTime;
            i++;
        }

        if (i < tokens.Length)
        {
            var qthToken = tokens[i];

            if (LooksLikeRstToken(qthToken, session.CurrentMode))
            {
                Console.WriteLine($"  warning: '{qthToken}' looks like an RST report; not writing it as QTH. Use '<sent>/<rcvd>' if you meant two reports.");
            }
            else
            {
                qth = qthToken.ToUpperInvariant();
            }

            i++;
        }

        if (i < tokens.Length)
        {
            var noteTokens = new List<string>();
            for (; i < tokens.Length; i++)
            {
                if (entryTime is null && TryParseQsoEntryTime(tokens[i], session, out parsedTime))
                {
                    entryTime = parsedTime;
                    continue;
                }

                noteTokens.Add(tokens[i]);
            }

            if (noteTokens.Count > 0)
            {
                notes = string.Join(' ', noteTokens);
            }
        }

        var band = Bands.FromMhz(session.CurrentFreqMhz) ?? "UNKNOWN";

        return new Qso
        {
            Call = call,
            RstSent = rstSent,
            RstRcvd = rstRcvd,
            Qth = qth,
            Notes = notes,
            TimeUtc = entryTime ?? session.DefaultQsoTimeUtc(),
            HasExplicitTime = entryTime is not null,
            FreqMhz = session.CurrentFreqMhz,
            Mode = session.CurrentMode,
            Submode = session.CurrentSubmode,
            Band = band,
        };
    }

    private static bool TryParseQsoEntryTime(string token, Session session, out DateTime timeUtc)
    {
        timeUtc = default;

        if (token.Length == 3 && token[0] == ':' && int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
        {
            if (minute is < 0 or >= 60)
            {
                return false;
            }

            var reference = session.LastExplicitQsoTimeUtc ?? session.StartUtc;
            timeUtc = new DateTime(reference.Year, reference.Month, reference.Day, reference.Hour, minute, 0, DateTimeKind.Utc);
            if (minute < reference.Minute)
            {
                timeUtc = timeUtc.AddHours(1);
            }
            return true;
        }

        var separator = token.IndexOf(':');
        if (separator <= 0 || separator != token.LastIndexOf(':') || separator == token.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(token.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var hour) ||
            !int.TryParse(token.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out minute) ||
            hour < 0 ||
            hour >= 24 ||
            minute < 0 ||
            minute >= 60)
        {
            return false;
        }

        var dateReference = session.LastExplicitQsoTimeUtc ?? session.StartUtc;
        timeUtc = new DateTime(dateReference.Year, dateReference.Month, dateReference.Day, hour, minute, 0, DateTimeKind.Utc);
        if (timeUtc < dateReference)
        {
            timeUtc = timeUtc.AddDays(1);
        }
        return true;
    }

    private static string DefaultRstFor(string mode) => mode switch
    {
        "CW" or "RTTY" or "PSK" or "PSK31" or "PSK63" => "599",
        "FT8" or "FT4" or "JT65" or "JT9" or "JS8" or "MFSK" or "Q65" => "-10",
        _ => "59",
    };

    private const string CutNumbers = "TOAUVEBDN";

    private static bool LooksLikeRstToken(string token, string mode)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var slash = token.IndexOf('/');

        if (slash > 0 && slash < token.Length - 1)
        {
            return LooksLikeSingleRst(token[..slash], mode)
                && LooksLikeSingleRst(token[(slash + 1)..], mode);
        }

        return LooksLikeSingleRst(token, mode);
    }

    private static bool LooksLikeSingleRst(string s, string mode)
    {
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        if (mode is "FT8" or "FT4" or "JT65" or "JT9" or "JS8" or "MFSK" or "Q65")
        {
            var start = s[0] is '-' or '+' ? 1 : 0;

            if (start == s.Length)
            {
                return false;
            }

            for (var k = start; k < s.Length; k++)
            {
                if (!char.IsDigit(s[k]))
                {
                    return false;
                }
            }

            return s.Length - start <= 3;
        }

        var cwLike = mode is "CW" or "RTTY" || mode.StartsWith("PSK", StringComparison.OrdinalIgnoreCase);
        var maxLen = cwLike ? 3 : 2;

        if (s.Length < 2 || s.Length > maxLen)
        {
            return false;
        }

        if (!char.IsDigit(s[0]))
        {
            return false;
        }

        for (var k = 1; k < s.Length; k++)
        {
            var c = char.ToUpperInvariant(s[k]);

            if (!char.IsDigit(c) && CutNumbers.IndexOf(c, StringComparison.Ordinal) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeRst(string raw, string mode)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return DefaultRstFor(mode);
        }

        if (mode is "FT8" or "FT4" or "JT65" or "JT9" or "JS8" or "MFSK" or "Q65")
        {
            return trimmed;
        }

        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            sb.Append(c switch
            {
                'T' or 't' or 'O' or 'o' => '0',
                'A' or 'a' => '1',
                'U' or 'u' => '2',
                'V' or 'v' => '3',
                'E' or 'e' => '5',
                'B' or 'b' => '7',
                'D' or 'd' => '8',
                'N' or 'n' => '9',
                _ => c,
            });
        }
        var result = sb.ToString();

        if (mode is "CW" or "RTTY" || mode.StartsWith("PSK", StringComparison.OrdinalIgnoreCase))
        {
            if (result.Length == 2 && result.All(char.IsDigit))
            {
                result += "9";
            }
        }
        else
        {
            if (result.Length == 1 && char.IsDigit(result[0]))
            {
                result = "5" + result;
            }
        }
        return result;
    }

    private static bool TryParseFrequency(string token, out double mhz)
    {
        mhz = 0;
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v <= 0)
        {
            return false;
        }

        var asMhz = v;
        var asKhz = v / 1000.0;
        var directValid = Bands.FromMhz(asMhz) is not null;
        var scaledValid = Bands.FromMhz(asKhz) is not null;

        if (directValid && scaledValid)
        {
            mhz = v >= 1000 ? asKhz : asMhz;
            return true;
        }
        if (directValid)
        {
            mhz = asMhz;
            return true;
        }
        if (scaledValid)
        {
            mhz = asKhz;
            return true;
        }
        return false;
    }

    private static bool IsValidCall(string s)
    {
        if (s.Length is < 3 or > 15)
        {
            return false;
        }
        var hasDigit = false;
        var hasLetter = false;
        foreach (var c in s)
        {
            if (char.IsDigit(c))
            {
                hasDigit = true;
            }
            else if (char.IsLetter(c))
            {
                hasLetter = true;
            }
            else if (c != '/')
            {
                return false;
            }
        }
        return hasDigit && hasLetter;
    }

    private static string PromptRequired(string label, Func<string, string?>? validator)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var line = Console.ReadLine();
            if (line is null)
            {
                throw new IOException("Input stream closed.");
            }
            var t = line.Trim();
            if (t.Length == 0)
            {
                Console.WriteLine("  ! Required.");
                continue;
            }
            if (validator is not null)
            {
                var err = validator(t);
                if (err is not null)
                {
                    Console.WriteLine($"  ! {err}");
                    continue;
                }
            }
            return t;
        }
    }

    private static DateTime PromptDate(string label)
    {
        string[] formats = ["yyyy-MM-dd", "yyyyMMdd", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy"];
        while (true)
        {
            Console.Write($"{label} [today]: ");
            var line = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line) || line.Equals("today", StringComparison.OrdinalIgnoreCase))
            {
                var t = DateTime.UtcNow;
                return new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
            }
            if (DateTime.TryParseExact(line, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
            }
            Console.WriteLine("  ! Use YYYY-MM-DD.");
        }
    }

    private static DateTime PromptTime(string label, DateTime date)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var line = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line))
            {
                Console.WriteLine("  ! Required.");
                continue;
            }
            var s = line.Replace(":", "", StringComparison.Ordinal);
            if (s.Length == 3)
            {
                s = "0" + s;
            }
            if (s.Length == 4 && s.All(char.IsDigit))
            {
                var h = int.Parse(s.AsSpan(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture);
                var m = int.Parse(s.AsSpan(2, 2), NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (h < 24 && m < 60)
                {
                    return new DateTime(date.Year, date.Month, date.Day, h, m, 0, DateTimeKind.Utc);
                }
            }
            if (s.Length == 6 && s.All(char.IsDigit))
            {
                var h = int.Parse(s.AsSpan(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture);
                var m = int.Parse(s.AsSpan(2, 2), NumberStyles.Integer, CultureInfo.InvariantCulture);
                var sec = int.Parse(s.AsSpan(4, 2), NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (h < 24 && m < 60 && sec < 60)
                {
                    return new DateTime(date.Year, date.Month, date.Day, h, m, sec, DateTimeKind.Utc);
                }
            }
            Console.WriteLine("  ! Use HHMM (UTC).");
        }
    }

    private static double PromptFrequency(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var line = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line))
            {
                Console.WriteLine("  ! Required.");
                continue;
            }
            if (TryParseFrequency(line, out var mhz))
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  -> {mhz:0.000} MHz ({Bands.FromMhz(mhz)})"));
                return mhz;
            }
            Console.WriteLine("  ! Not a valid amateur frequency.");
        }
    }

    private static (string Mode, string? Submode) PromptMode(string label)
    {
        while (true)
        {
            Console.Write($"{label} [CW]: ");
            var line = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line))
            {
                return ("CW", null);
            }
            if (Modes.Known.Contains(line))
            {
                return Modes.Normalize(line);
            }
            Console.WriteLine("  ! Unknown mode. Try CW, SSB, FT8, FM, etc.");
        }
    }

    private static void PrintStatus(Session s)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"--- {s.ParkRef}{(s.ParkName is null ? "" : $" ({s.ParkName})")} | Op {s.MyCall} | {s.CurrentFreqMhz:0.000} MHz {s.CurrentMode}{(s.CurrentSubmode is null ? "" : $" ({s.CurrentSubmode})")} ({Bands.FromMhz(s.CurrentFreqMhz) ?? "?"}) ---"));
    }
}

internal sealed class Session
{
    public required string OutputPath { get; set; }
    public required bool Append { get; init; }
    public required string MyCall { get; init; }
    public required string ParkRef { get; init; }
    public string? ParkName { get; init; }
    public required double CurrentFreqMhz { get; set; }
    public required string CurrentMode { get; set; }
    public string? CurrentSubmode { get; set; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public bool AppendTimeMode { get; init; }
    public DateTime? LastExplicitQsoTimeUtc { get; set; }
    public List<Qso> Qsos { get; } = [];

    public void Save()
    {
        ResolveTimes();
        Adif.Write(OutputPath, MyCall, ParkRef, Qsos, Append);
    }

    internal DateTime DefaultQsoTimeUtc() => AppendTimeMode ? DateTime.UtcNow : StartUtc;

    internal void NoteLoggedQso(Qso qso)
    {
        if (qso.HasExplicitTime)
        {
            LastExplicitQsoTimeUtc = qso.TimeUtc;
        }
    }

    internal void ResolveTimes(DateTime? endUtcOverride = null)
    {
        if (Qsos.Count == 0)
        {
            return;
        }

        if (!Qsos.Any(static q => q.HasExplicitTime))
        {
            if (AppendTimeMode)
            {
                return;
            }

            DistributeTimes(StartUtc, EndUtc);
            return;
        }

        var endUtc = endUtcOverride ?? (AppendTimeMode ? DateTime.UtcNow : EndUtc);
        var previousExplicit = -1;
        for (var i = 0; i < Qsos.Count; i++)
        {
            if (!Qsos[i].HasExplicitTime)
            {
                continue;
            }

            if (previousExplicit < 0)
            {
                ResolveLeadingTimes(i);
            }
            else
            {
                ResolveBetweenExplicitTimes(previousExplicit, i);
            }

            previousExplicit = i;
        }

        if (previousExplicit >= 0)
        {
            ResolveTrailingTimes(previousExplicit, endUtc);
        }
    }

    private void DistributeTimes(DateTime startUtc, DateTime endUtc)
    {
        if (Qsos.Count == 1)
        {
            Qsos[0].TimeUtc = startUtc;
            return;
        }
        var span = (endUtc - startUtc).TotalSeconds;
        if (span <= 0)
        {
            foreach (var q in Qsos)
            {
                q.TimeUtc = startUtc;
            }
            return;
        }
        var step = span / (Qsos.Count - 1);
        for (var i = 0; i < Qsos.Count; i++)
        {
            Qsos[i].TimeUtc = startUtc.AddSeconds(step * i);
        }
    }

    private void ResolveLeadingTimes(int explicitIndex)
    {
        if (explicitIndex == 0)
        {
            return;
        }

        var explicitTime = Qsos[explicitIndex].TimeUtc;
        if (explicitTime > StartUtc)
        {
            InterpolateTimes(0, explicitIndex - 1, StartUtc, explicitTime);
            return;
        }

        for (var i = explicitIndex - 1; i >= 0; i--)
        {
            Qsos[i].TimeUtc = explicitTime.AddMinutes(i - explicitIndex);
        }
    }

    private void ResolveBetweenExplicitTimes(int previousExplicit, int nextExplicit)
    {
        if (nextExplicit == previousExplicit + 1)
        {
            return;
        }

        var previousTime = Qsos[previousExplicit].TimeUtc;
        var nextTime = Qsos[nextExplicit].TimeUtc;
        if (nextTime > previousTime)
        {
            InterpolateTimes(previousExplicit + 1, nextExplicit - 1, previousTime, nextTime);
            return;
        }

        ExtrapolateForward(previousExplicit + 1, nextExplicit - 1, previousTime);
    }

    private void ResolveTrailingTimes(int explicitIndex, DateTime endUtc)
    {
        if (explicitIndex == Qsos.Count - 1)
        {
            return;
        }

        var explicitTime = Qsos[explicitIndex].TimeUtc;
        if (endUtc > explicitTime)
        {
            InterpolateTimes(explicitIndex + 1, Qsos.Count - 1, explicitTime, endUtc);
            return;
        }

        ExtrapolateForward(explicitIndex + 1, Qsos.Count - 1, explicitTime);
    }

    private void InterpolateTimes(int firstIndex, int lastIndex, DateTime startUtc, DateTime endUtc)
    {
        var count = lastIndex - firstIndex + 1;
        var stepTicks = (endUtc - startUtc).Ticks / (count + 1);
        for (var i = 0; i < count; i++)
        {
            Qsos[firstIndex + i].TimeUtc = startUtc.AddTicks(stepTicks * (i + 1));
        }
    }

    private void ExtrapolateForward(int firstIndex, int lastIndex, DateTime startUtc)
    {
        for (var i = firstIndex; i <= lastIndex; i++)
        {
            Qsos[i].TimeUtc = startUtc.AddMinutes(i - firstIndex + 1);
        }
    }
}
