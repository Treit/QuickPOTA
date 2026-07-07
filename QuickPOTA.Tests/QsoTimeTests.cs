namespace QuickPOTA.Tests;

public sealed class QsoTimeTests
{
    [Fact]
    public void BuildQsoSupportsLeadingAndPostCallTimes()
    {
        var session = CreateSession();

        var leading = Program.BuildQso(Split("13:05 AE7XI 55N WA"), session);
        Assert.NotNull(leading);
        Assert.True(leading.HasExplicitTime);
        Assert.Equal(new DateTime(2026, 7, 5, 13, 5, 0, DateTimeKind.Utc), leading.TimeUtc);
        Assert.Equal("559", leading.RstRcvd);
        Assert.Equal("WA", leading.Qth);
        session.NoteLoggedQso(leading);

        var postCall = Program.BuildQso(Split("K7ABC 13:06 44N AZ"), session);
        Assert.NotNull(postCall);
        Assert.True(postCall.HasExplicitTime);
        Assert.Equal(new DateTime(2026, 7, 5, 13, 6, 0, DateTimeKind.Utc), postCall.TimeUtc);
        Assert.Equal("449", postCall.RstRcvd);
        Assert.Equal("AZ", postCall.Qth);
    }

    [Fact]
    public void BuildQsoSupportsPostRstTimeWithoutQth()
    {
        var session = CreateSession();

        var qso = Program.BuildQso(Split("EA1DD 539 :50"), session);

        Assert.NotNull(qso);
        Assert.True(qso.HasExplicitTime);
        Assert.Equal(new DateTime(2026, 7, 5, 13, 50, 0, DateTimeKind.Utc), qso.TimeUtc);
        Assert.Equal("539", qso.RstRcvd);
        Assert.Null(qso.Qth);
    }

    [Fact]
    public void BuildQsoElidesTrailingTimeFromNotes()
    {
        var session = CreateSession();

        var qso = Program.BuildQso(Split("EA1DD 539 DX :50"), session);

        Assert.NotNull(qso);
        Assert.True(qso.HasExplicitTime);
        Assert.Equal(new DateTime(2026, 7, 5, 13, 50, 0, DateTimeKind.Utc), qso.TimeUtc);
        Assert.Equal("DX", qso.Qth);
        Assert.Null(qso.Notes);
    }

    [Fact]
    public void BuildQsoElidesTimeFromNotesButKeepsOtherNotes()
    {
        var session = CreateSession();

        var qso = Program.BuildQso(Split("EA1DD 539 DX :50 loud flutter"), session);

        Assert.NotNull(qso);
        Assert.True(qso.HasExplicitTime);
        Assert.Equal(new DateTime(2026, 7, 5, 13, 50, 0, DateTimeKind.Utc), qso.TimeUtc);
        Assert.Equal("DX", qso.Qth);
        Assert.Equal("loud flutter", qso.Notes);
    }

    [Fact]
    public void LoggedMessageShowsExplicitTime()
    {
        var session = CreateSession();
        var qso = Program.BuildQso(Split("EA1DD 539 :50"), session);

        Assert.NotNull(qso);
        Assert.Equal("  logged EA1DD 13:50Z 599/539", Program.LoggedMessage(qso));
    }

    [Fact]
    public void BuildQsoPartialTimesUseLastExplicitHourAndRollOver()
    {
        var session = CreateSession();

        var first = Program.BuildQso(Split(":53 AE7XI 55N WA"), session);
        Assert.NotNull(first);
        Assert.Equal(new DateTime(2026, 7, 5, 13, 53, 0, DateTimeKind.Utc), first.TimeUtc);
        session.NoteLoggedQso(first);

        var second = Program.BuildQso(Split(":05 K7ABC 44N AZ"), session);
        Assert.NotNull(second);
        Assert.Equal(new DateTime(2026, 7, 5, 14, 5, 0, DateTimeKind.Utc), second.TimeUtc);
    }

    [Fact]
    public void ResolveTimesInterpolatesMissingTimesBetweenExplicitAnchors()
    {
        var session = CreateSession();
        AddQso(session, Program.BuildQso(Split("13:00 AE7XI"), session));
        AddQso(session, Program.BuildQso(Split("K7ABC"), session));
        AddQso(session, Program.BuildQso(Split("14:00 W1AW"), session));

        session.ResolveTimes();

        Assert.Equal(new DateTime(2026, 7, 5, 13, 0, 0, DateTimeKind.Utc), session.Qsos[0].TimeUtc);
        Assert.Equal(new DateTime(2026, 7, 5, 13, 30, 0, DateTimeKind.Utc), session.Qsos[1].TimeUtc);
        Assert.Equal(new DateTime(2026, 7, 5, 14, 0, 0, DateTimeKind.Utc), session.Qsos[2].TimeUtc);
    }

    [Fact]
    public void ResolveTimesInterpolatesAroundSingleExplicitAnchor()
    {
        var session = CreateSession();
        AddQso(session, Program.BuildQso(Split("AE7XI"), session));
        AddQso(session, Program.BuildQso(Split("14:00 K7ABC"), session));
        AddQso(session, Program.BuildQso(Split("W1AW"), session));

        session.ResolveTimes();

        Assert.Equal(new DateTime(2026, 7, 5, 13, 30, 0, DateTimeKind.Utc), session.Qsos[0].TimeUtc);
        Assert.Equal(new DateTime(2026, 7, 5, 14, 0, 0, DateTimeKind.Utc), session.Qsos[1].TimeUtc);
        Assert.Equal(new DateTime(2026, 7, 5, 14, 30, 0, DateTimeKind.Utc), session.Qsos[2].TimeUtc);
    }

    [Fact]
    public void ResolveTimesWithoutExplicitTimesUsesExistingSpreadBehavior()
    {
        var session = CreateSession();
        AddQso(session, Program.BuildQso(Split("AE7XI"), session));
        AddQso(session, Program.BuildQso(Split("K7ABC"), session));
        AddQso(session, Program.BuildQso(Split("W1AW"), session));

        session.ResolveTimes();

        Assert.Equal(new DateTime(2026, 7, 5, 13, 0, 0, DateTimeKind.Utc), session.Qsos[0].TimeUtc);
        Assert.Equal(new DateTime(2026, 7, 5, 14, 0, 0, DateTimeKind.Utc), session.Qsos[1].TimeUtc);
        Assert.Equal(new DateTime(2026, 7, 5, 15, 0, 0, DateTimeKind.Utc), session.Qsos[2].TimeUtc);
    }

    private static void AddQso(Session session, Qso? qso)
    {
        Assert.NotNull(qso);
        session.Qsos.Add(qso);
        session.NoteLoggedQso(qso);
    }

    private static string[] Split(string input) =>
        input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static Session CreateSession() => new()
    {
        OutputPath = "test.adi",
        Append = false,
        MyCall = "AE7XI",
        ParkRef = "US-1125",
        CurrentFreqMhz = 14.03,
        CurrentMode = "CW",
        StartUtc = new DateTime(2026, 7, 5, 13, 0, 0, DateTimeKind.Utc),
        EndUtc = new DateTime(2026, 7, 5, 15, 0, 0, DateTimeKind.Utc),
    };
}
