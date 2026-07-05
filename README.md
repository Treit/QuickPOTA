# QuickPOTA

CLI tool for quickly generating ADIF files to upload to https://pota.app. Turn a paper POTA activation log into a valid ADIF in about the same time it takes to read the QSOs off the page.

## Highlights

- Interactive wizard that asks only for what it needs: your callsign, the park, the UTC date and start/end times, and the starting frequency and mode.
- A single QSO prompt that accepts callsigns, frequency changes, and mode changes on the fly.
- Automatic CW cut-number translation (`55N` becomes `559`, `TT9` becomes `009`, and so on).
- Frequency-to-band mapping for every HF, VHF, and UHF amateur band.
- QSO times are distributed evenly across the activation window, so you never have to log an exact clock time.
- Append mode: point it at an existing ADIF and just keep logging.
- Ships with an embedded copy of the POTA park directory, so it runs fully offline.
- AOT-compiled: one small native executable, no runtime install required.

## Install and build

```
dotnet build -c Release
```

For a self-contained native executable:

```
dotnet publish -c Release -r win-x64
dotnet publish -c Release -r linux-x64
```

On Windows, AOT publish requires the Visual Studio C++ build tools. If the linker step fails with `vswhere.exe is not recognized`, add the Visual Studio Installer directory to `PATH` for the current shell:

```powershell
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"
```

The published binary lives at `bin\Release\net10.0\<rid>\publish\quickpota.exe` (or `quickpota` on Linux).

## Usage

Start a new activation (interactive wizard):

```
quickpota
```

Append QSOs to an existing ADIF file (park, callsign, freq, and mode are read from the last record):

```
quickpota mylog.adi
```

Show help:

```
quickpota --help
```

## The QSO prompt

Once the wizard finishes, each line at the `[n] >` prompt is one of:

    <call> <rst> <qth> <notes>    Log a contact. Example: NF7N 55N WA nice sig
    <freq> [mode]                 Change frequency (KHz or MHz), optional mode
    <mode>                        Switch mode (CW SSB FT8 FM ...)
    Q                             Quit and write the ADIF
    ?                             Show status and help

Only the callsign is required on a QSO line. Missing fields fill in from mode-appropriate defaults (`599` for CW/RTTY, `59` for phone).

RST cut numbers are translated for CW-like modes: `T=0 O=0 A=1 U=2 V=3 E=5 B=7 D=8 N=9`.

Frequencies with a value of 1000 or more are interpreted as KHz, otherwise as MHz.

## Walkthrough: a new activation

```
> quickpota
QuickPOTA - new activation

Your callsign: k7abc
[Loaded 93029 POTA park references from cache]
Park reference (e.g. US-3166): US-3166
  -> Bridle Trails State Park
Activation date (UTC, YYYY-MM-DD or today) [today]:
Start time (UTC, HHMM): 1600
End time (UTC, HHMM): 1800

Tip: at the QSO prompt you can also enter a frequency (KHz or MHz)
     to switch, e.g. '14030 CW' or '146.520 FM', or just a mode name.

Starting frequency (KHz or MHz): 14030
  -> 14.030 MHz (20m)
Starting mode [CW]:
Ready. Type 'Q' <enter> to quit and write the ADIF.

--- US-3166 (Bridle Trails State Park) | Op K7ABC | 14.030 MHz CW (20m) ---
[1] > NF7N 55N WA
  logged NF7N 599/559 WA
[2] > K5XYZ 5NN TX great sig
  logged K5XYZ 599/599 TX
[3] > 146.520 FM
  -> QSY 146.520 MHz FM (2m)
[3] > W1AW 59 CT
  logged W1AW 59/59 CT
[4] > SSB
  -> Mode SSB
[4] > 7200
  -> QSY 7.200 MHz SSB (40m)
[4] > KD1AB 44 MA
  logged KD1AB 59/44 MA
[5] > Q

Wrote 4 QSO(s) to C:\path\to\US-3166-20260705.adi
```

The four QSOs get timestamps spread evenly between `1600Z` and `1800Z`, and each carries `MY_SIG=POTA` with `MY_SIG_INFO=US-3166` ready for upload.

## Walkthrough: appending to an existing log

```
> quickpota US-3166-20260705.adi
Append mode: US-3166-20260705.adi
  Operator: K7ABC
  Park:     US-3166 (Bridle Trails State Park)
  Freq:     7.200 MHz
  Mode:     SSB

Ready. Type 'Q' <enter> to quit and write the ADIF.

--- US-3166 (Bridle Trails State Park) | Op K7ABC | 7.200 MHz SSB (40m) ---
[1] > K9ZZ 59 IL
  logged K9ZZ 59/59 IL
[2] > AB1CDE 55 NH
  logged AB1CDE 59/55 NH
[3] > Q

Wrote 2 QSO(s) to US-3166-20260705.adi
```

In append mode the tool reuses the park, operator, freq, and mode from the last record in the file, and stamps each new QSO with the current UTC time.

## Cut-number examples

Given `CW` mode:

    NF7N 55N WA          -> RST_RCVD 559, STATE WA
    N0CALL TT9           -> RST_RCVD 009
    K5XYZ 5NN TX         -> RST_RCVD 599, STATE TX
    W1AW 4EE             -> RST_RCVD 455

Given `SSB` mode (cut numbers are still translated where they appear, but the default is `59`):

    W1AW 59 CT           -> RST_RCVD 59
    KD1AB 44 MA          -> RST_RCVD 44
    KA1B 5 OR            -> RST_RCVD 55 (single digit gets a leading 5)

## Output file

New activations write `<PARK>-<YYYYMMDD>.adi` in the current directory (for example, `US-3166-20260705.adi`). Append mode writes back to the file you passed on the command line.

Fields emitted per QSO include `CALL`, `QSO_DATE`, `TIME_ON`, `BAND`, `FREQ`, `MODE`, `RST_SENT`, `RST_RCVD`, `STATION_CALLSIGN`, `OPERATOR`, `MY_SIG=POTA`, `MY_SIG_INFO=<park>`, plus `STATE`, `QTH`, `COMMENT`, and `SIG_INFO=<park name>` when available.

## Park database

QuickPOTA ships with an embedded copy of the POTA park directory (`data/all_parks_ext.csv`) so it works fully offline. On startup it tries, in order:

1. A user-local cache under the local application data folder (refreshed once every 30 days).
2. A fresh download from `https://pota.app/all_parks_ext.csv` when the cache is stale or missing.
3. The embedded copy shipped in the executable.

The status line at startup indicates which source was used (`cache` or `embedded`). To refresh the committed copy, download the latest CSV into `data/all_parks_ext.csv` and rebuild.
