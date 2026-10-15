using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using JumpList.Automatic;
using JumpList.Custom;
using NUnit.Framework;
using OleCf;

namespace JumpList.Test;

/// <summary>
///     Assertions on the real jump lists in TestFiles/
/// </summary>
[TestFixture]
public class TestMain
{
    internal static string Sample(params string[] parts) =>
        Path.Combine(new[] { TestContext.CurrentContext.TestDirectory, "TestFiles" }.Concat(parts).ToArray());

    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso);

    [TestCase("Win10", "5f7b5f1e01b83767", 2, 0, 0, 0)]
    [TestCase("Win10", "7e4dca80246863e3", 3, 1, 0, 3)]
    [TestCase("Win10", "f01b4d95cf55d32a", 11, 9, 4, 3)]
    [TestCase("Win7", "1b4dd67f29cb1962", 6, 4, 0, 1)]
    [TestCase("Win80", "f01b4d95cf55d32a", 6, 4, 0, 1)]
    [TestCase("Win81", "f01b4d95cf55d32a", 6, 4, 0, 1)]
    [TestCase("dtformats", "1b4dd67f29cb1962", 9, 7, 0, 1)]
    [TestCase("dtformats", "9d1f905ce5044aee", 4, 2, 0, 3)]
    public void AutomaticDestinationSample(string folder, string appId, int directory, int entries, int pinned,
        int version)
    {
        var a = JumpList.LoadAutoJumplist(Sample(folder, appId + ".automaticDestinations-ms"));

        a.AppId.AppId.Should().Be(appId);
        a.Directory.Should().HaveCount(directory);
        a.DestListCount.Should().Be(entries);
        a.DestListEntries.Should().HaveCount(entries);
        a.PinnedDestListCount.Should().Be(pinned);
        a.DestListEntries.Count(t => t.Pinned).Should().Be(pinned);
        a.DestListVersion.Should().Be(version);
        a.ParseErrors.Should().BeEmpty();

        // Every entry with a stream has its LNK, and every LNK parsed cleanly
        a.DestListEntries.Where(t => t.Lnk != null).Should().HaveCount(a.Directory.Count - 2);
        a.DestListEntries.Where(t => t.Lnk != null).SelectMany(t => t.Lnk.ParseErrors).Should().BeEmpty();
    }

    [Test]
    public void Win10EntryValues()
    {
        var a = JumpList.LoadAutoJumplist(Sample("Win10", "f01b4d95cf55d32a.automaticDestinations-ms"));

        a.AppId.Description.Should().Be("Windows Explorer Windows 8.1");
        a.DestListEntries.Select(t => t.EntryNumber).Should().Equal(7, 9, 8, 1, 2, 6, 5, 4, 3);
        a.DestListEntries.Select(t => t.MRUPosition).Should().Equal(Enumerable.Range(0, 9));

        var first = a.DestListEntries[0];
        first.Path.Should().Be(@"C:\Temp");
        first.Hostname.Should().Be("desktop-annfp9d");
        first.MacAddress.Should().Be("00:15:5d:01:6d:02");
        first.InteractionCount.Should().Be(2);
        first.Pinned.Should().BeFalse();
        first.CreatedOn.Should().Be(Utc("2015-11-24T18:39:02.4912714+00:00"));
        first.LastModified.Should().Be(Utc("2015-11-24T20:31:34.4475253+00:00"));
        first.Lnk.LocalPath.Should().Be(@"C:\Temp");

        var pinned = a.DestListEntries.Single(t => t.EntryNumber == 1);
        pinned.Pinned.Should().BeTrue();
        pinned.Path.Should().Be(@"C:\Users\e\Desktop");
    }

    [Test]
    public void EntriesWithoutTimeBasedDroidUseTheMarkersJlecmdBlanks()
    {
        // Edge entries have all-zero droids; JLECmd blanks the 1582 time and all-zero MAC address
        var a = JumpList.LoadAutoJumplist(Sample("dtformats", "9d1f905ce5044aee.automaticDestinations-ms"));

        a.AppId.Description.Should().Be("Edge Browser");
        foreach (var entry in a.DestListEntries)
        {
            entry.CreatedOn.Should().Be(DestListEntry.NoCreationTime);
            entry.CreatedOn.Year.Should().Be(1582);
            entry.MacAddress.Should().Be("00:00:00:00:00:00");
            entry.Hostname.Should().BeEmpty();
        }

        a.DestListEntries[0].Path.Should().Be("http://support.microsoft.com/kb/3124263");
        a.DestListEntries[0].LastModified.Should().Be(Utc("2016-01-17T13:08:08.2475045+00:00"));
    }

    [Test]
    public void EmptyDestListHasNoEntries()
    {
        var a = JumpList.LoadAutoJumplist(Sample("Win10", "5f7b5f1e01b83767.automaticDestinations-ms"));

        a.DestListEntries.Should().BeEmpty();
        a.ToString().Should().Contain("Jump list contains no DestList entries");
    }

    [Test]
    public void CustomDestinationSample()
    {
        var c = JumpList.LoadCustomJumplist(Sample("dtformats", "5afe4de1b92fc382.customDestinations-ms"));

        c.Version.Should().Be(2);
        c.NumberOfCategories.Should().Be(1);
        c.ParseErrors.Should().BeEmpty();

        var category = c.Entries.Should().ContainSingle().Subject;
        category.CategoryType.Should().Be(2);
        category.HeaderType.Should().Be(2);
        category.NumberOfEntries.Should().Be(9);
        category.LnkFiles.Should().HaveCount(9);
        category.LnkFiles.Should().OnlyContain(t => t.LocalPath == @"C:\Windows\System32\GettingStarted.exe");
        category.LnkFiles[0].Arguments.Should()
            .Be("{A97AF2A4-8EF1-49C1-8B4C-68B8AE925549} http://go.microsoft.com/fwlink/?LinkID=139460");
        category.LnkFiles[0].SourceFile.Should().EndWith("Offset_0x24.lnk");

        // The next entry's class id is no longer left on the end of each LNK
        category.LnkFiles.SelectMany(t => t.ParseErrors).Should().BeEmpty();
    }

    [Test]
    public void InvalidFileShouldThrowException()
    {
        Action action = () => JumpList.LoadAutoJumplist(Sample("Bad", "CALC.EXE-3FBEF7FD.pf"));

        // Still an Exception with the same message for existing callers
        action.Should().Throw<OleCfParseException>().WithMessage("Invalid signature!");
    }

    [Test]
    public void EmptyFilesThrow()
    {
        ((Action)(() => new AutomaticDestination(new byte[0], "x.automaticDestinations-ms"))).Should()
            .Throw<JumpListParseException>().WithMessage("Empty file");
        ((Action)(() => new CustomDestination(new byte[0], "x.customDestinations-ms"))).Should()
            .Throw<JumpListParseException>().WithMessage("Empty file");
        ((Action)(() => new CustomDestination(new byte[20], "x.customDestinations-ms"))).Should()
            .Throw<JumpListParseException>().WithMessage("Empty custom destinations jump list");
        ((Action)(() => new CustomDestination(new byte[100], "x.customDestinations-ms"))).Should()
            .Throw<JumpListParseException>().WithMessage("Invalid signature (footer missing)");
    }
}
