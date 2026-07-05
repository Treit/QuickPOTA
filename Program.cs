using System.Globalization;

namespace QuickPOTA;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            if (args.Length > 0 && (args[0] == "-h" || args[0] == "--help" || args[0] == "/?"))
            {
                PrintHelp();
                return 0;
            }

            string? existingFile = args.Length > 0 ? args[0] : null;
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
        Console.WriteLine("  <call> [sent/rcvd | rcvd] [qth] [notes]   Log a contact.");
        Console.WriteLine("     e.g. 'NF7N 55N WA'          -> RST_SENT=599 RST_RCVD=559 STATE=WA");
        Console.WriteLine("     e.g. 'WM2V 55N/44N AZ'      -> RST_SENT=559 RST_RCVD=449 STATE=AZ");
        Console.WriteLine("  <freq> [mode]                Change frequency (KHz or MHz), optional mode");
        Console.WriteLine("  <mode>                       Change mode (CW SSB FT8 FM ...)");
        Console.WriteLine("  Q                            Quit and write the ADIF");
        Console.WriteLine();
        Console.WriteLine("RST cut numbers are translated (T=0 N=9 A=1 E=5 O=0 U=2 V=3 B=7 D=8).");
    }

    private static Session StartAppend(string path, Parks parks, Task loadTask)
    {
        var (_, freq, mode, parkRef, myCall, lastTime) = Adif.PeekLast(path);
        if (parkRef is null || myCall is null || freq is null || mode is null)
        {
            throw new InvalidOperationException("Existing ADIF does not contain enough context (park, callsign, freq, mode) to append.");
        }

        try { loadTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
        var parkName = parks.Lookup(parkRef);

        Console.WriteLine($"Append mode: {path}");
        Console.WriteLine($"  Operator: {myCall}");
        Console.WriteLine($"  Park:     {parkRef}{(parkName is null ? "" : $" ({parkName})")}");
        Console.WriteLine($"  Freq:     {freq:0.000} MHz");
        Console.WriteLine($"  Mode:     {mode}");
        Console.WriteLine();

        return new Session
        {
            OutputPath = path,
            Append = true,
            MyCall = myCall,
            ParkRef = parkRef,
            ParkName = parkName,
            CurrentFreqMhz = freq.Value,
            CurrentMode = mode,
            AppendTimeMode = true,
            StartUtc = lastTime ?? DateTime.UtcNow,
            EndUtc = DateTime.UtcNow,
        };
    }

    private static async Task<Session> StartNewAsync(Parks parks, Task loadTask)
    {
        Console.WriteLine("QuickPOTA - new activation");
        Console.WriteLine();

        var myCall = PromptRequired("Your callsign", s => IsValidCall(s) ? null : "Enter a valid callsign.").ToUpperInvariant();

        try { loadTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
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
        var mode = PromptMode("Starting mode");

        await Task.CompletedTask;

        return new Session
        {
            OutputPath = DefaultOutputPath(parkRef, date),
            Append = false,
            MyCall = myCall,
            ParkRef = parkRef,
            ParkName = parkName,
            CurrentFreqMhz = freq,
            CurrentMode = mode,
            StartUtc = startUtc,
            EndUtc = endUtc,
        };
    }

    private static string DefaultOutputPath(string parkRef, DateTime date)
    {
        var name = $"{parkRef}-{date:yyyyMMdd}.adi";
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
            if (line is null) break;
            var input = line.Trim();
            if (input.Length == 0) continue;

            if (input.Equals("Q", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("QUIT", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("EXIT", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (input.Equals("?", StringComparison.Ordinal) || input.Equals("HELP", StringComparison.OrdinalIgnoreCase))
            {
                PrintStatus(session);
                Console.WriteLine("  Enter '<call> [sent/rcvd | rcvd] [qth] [notes]' to log a QSO.");
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
                    session.CurrentMode = Modes.Normalize(tokens[1]);
                }
                Console.WriteLine($"  -> QSY {session.CurrentFreqMhz:0.000} MHz {session.CurrentMode} ({Bands.FromMhz(session.CurrentFreqMhz) ?? "?"})");
                continue;
            }

            if (tokens.Length == 1 && Modes.Known.Contains(tokens[0]))
            {
                session.CurrentMode = Modes.Normalize(tokens[0]);
                Console.WriteLine($"  -> Mode {session.CurrentMode}");
                continue;
            }

            var qso = BuildQso(tokens, session);
            if (qso is null)
            {
                Console.WriteLine("  ! Could not parse. Expected: <call> [rst] [qth] [notes...]");
                continue;
            }
            session.Qsos.Add(qso);
            Console.WriteLine($"  logged {qso.Call} {qso.RstSent}/{qso.RstRcvd}{(string.IsNullOrEmpty(qso.Qth) ? "" : " " + qso.Qth)}");
        }
    }

    private static Qso? BuildQso(string[] tokens, Session session)
    {
        var call = tokens[0].ToUpperInvariant();
        if (!IsValidCall(call)) return null;

        string rstSent = DefaultRstFor(session.CurrentMode);
        string rstRcvd = rstSent;
        string? qth = null;
        string? notes = null;

        if (tokens.Length >= 2)
        {
            var rstToken = tokens[1];
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
        }
        if (tokens.Length >= 3)
        {
            qth = tokens[2].ToUpperInvariant();
        }
        if (tokens.Length >= 4)
        {
            notes = string.Join(' ', tokens[3..]);
        }

        var band = Bands.FromMhz(session.CurrentFreqMhz) ?? "UNKNOWN";

        return new Qso
        {
            Call = call,
            RstSent = rstSent,
            RstRcvd = rstRcvd,
            Qth = qth,
            Notes = notes,
            TimeUtc = session.AppendTimeMode ? DateTime.UtcNow : session.StartUtc,
            FreqMhz = session.CurrentFreqMhz,
            Mode = session.CurrentMode,
            Band = band,
        };
    }

    private static string DefaultRstFor(string mode) => mode switch
    {
        "CW" or "RTTY" or "PSK" or "PSK31" or "PSK63" => "599",
        "FT8" or "FT4" or "JT65" or "JT9" or "JS8" or "MFSK" or "Q65" => "-10",
        _ => "59",
    };

    private static string NormalizeRst(string raw, string mode)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return DefaultRstFor(mode);

        if (mode is "FT8" or "FT4" or "JT65" or "JT9" or "JS8" or "MFSK" or "Q65")
        {
            return trimmed;
        }

        var sb = new System.Text.StringBuilder(trimmed.Length);
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

        if (mode == "CW" || mode == "RTTY" || mode.StartsWith("PSK", StringComparison.OrdinalIgnoreCase))
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
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }
        if (v <= 0) return false;
        mhz = v >= 1000 ? v / 1000.0 : v;
        return Bands.FromMhz(mhz) is not null;
    }

    private static bool IsValidCall(string s)
    {
        if (s.Length < 3 || s.Length > 15) return false;
        var hasDigit = false;
        var hasLetter = false;
        foreach (var c in s)
        {
            if (char.IsDigit(c)) hasDigit = true;
            else if (char.IsLetter(c)) hasLetter = true;
            else if (c != '/') return false;
        }
        return hasDigit && hasLetter;
    }

    private static string PromptRequired(string label, Func<string, string?>? validator)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var line = Console.ReadLine();
            if (line is null) throw new IOException("Input stream closed.");
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
        while (true)
        {
            Console.Write($"{label} [today]: ");
            var line = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line) || line.Equals("today", StringComparison.OrdinalIgnoreCase))
            {
                var t = DateTime.UtcNow;
                return new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
            }
            string[] formats = ["yyyy-MM-dd", "yyyyMMdd", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy"];
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
            var s = line.Replace(":", "");
            if (s.Length == 3) s = "0" + s;
            if (s.Length == 4 && s.All(char.IsDigit))
            {
                var h = int.Parse(s[..2], CultureInfo.InvariantCulture);
                var m = int.Parse(s[2..], CultureInfo.InvariantCulture);
                if (h < 24 && m < 60)
                {
                    return new DateTime(date.Year, date.Month, date.Day, h, m, 0, DateTimeKind.Utc);
                }
            }
            if (s.Length == 6 && s.All(char.IsDigit))
            {
                var h = int.Parse(s[..2], CultureInfo.InvariantCulture);
                var m = int.Parse(s.Substring(2, 2), CultureInfo.InvariantCulture);
                var sec = int.Parse(s[4..], CultureInfo.InvariantCulture);
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
                Console.WriteLine($"  -> {mhz:0.000} MHz ({Bands.FromMhz(mhz)})");
                return mhz;
            }
            Console.WriteLine("  ! Not a valid amateur frequency.");
        }
    }

    private static string PromptMode(string label)
    {
        while (true)
        {
            Console.Write($"{label} [CW]: ");
            var line = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line)) return "CW";
            if (Modes.Known.Contains(line))
            {
                return Modes.Normalize(line);
            }
            Console.WriteLine("  ! Unknown mode. Try CW, SSB, FT8, FM, etc.");
        }
    }

    private static void PrintStatus(Session s)
    {
        Console.WriteLine($"--- {s.ParkRef}{(s.ParkName is null ? "" : $" ({s.ParkName})")} | Op {s.MyCall} | {s.CurrentFreqMhz:0.000} MHz {s.CurrentMode} ({Bands.FromMhz(s.CurrentFreqMhz) ?? "?"}) ---");
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
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public bool AppendTimeMode { get; init; }
    public List<Qso> Qsos { get; } = new();

    public void Save()
    {
        if (!AppendTimeMode)
        {
            DistributeTimes();
        }
        Adif.Write(OutputPath, MyCall, ParkRef, ParkName, Qsos, Append);
    }

    private void DistributeTimes()
    {
        if (Qsos.Count == 0) return;
        if (Qsos.Count == 1)
        {
            Qsos[0].TimeUtc = StartUtc;
            return;
        }
        var span = (EndUtc - StartUtc).TotalSeconds;
        if (span <= 0)
        {
            foreach (var q in Qsos) q.TimeUtc = StartUtc;
            return;
        }
        var step = span / (Qsos.Count - 1);
        for (var i = 0; i < Qsos.Count; i++)
        {
            Qsos[i].TimeUtc = StartUtc.AddSeconds(step * i);
        }
    }
}
