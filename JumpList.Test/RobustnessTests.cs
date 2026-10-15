using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using JumpList.Automatic;
using JumpList.Custom;
using NUnit.Framework;
using OleCf;

namespace JumpList.Test;

/// <summary>
///     Malformed DestLists and custom destinations, built in memory or from damaged samples
/// </summary>
[TestFixture]
public class RobustnessTests
{
    private static readonly Guid TimeBasedDroid = Guid.Parse("a21b9d4a-92da-11e5-88e1-00155d016d02");
    private static readonly long Modified = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc).ToFileTimeUtc();

    private static readonly byte[] LnkClassId =
        { 0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 };

    private static byte[] Header(int version, int entries, int pinned = 0)
    {
        var header = new byte[32];
        BitConverter.GetBytes(version).CopyTo(header, 0);
        BitConverter.GetBytes(entries).CopyTo(header, 4);
        BitConverter.GetBytes(pinned).CopyTo(header, 8);
        return header;
    }

    private static byte[] Entry(int version, int number, string path, Guid? fileDroid = null, long? modified = null,
        byte[] sps = null)
    {
        var pathOffset = version == 1 ? 112 : 128;
        var entry = new byte[pathOffset + 2 + path.Length * 2];
        (fileDroid ?? TimeBasedDroid).ToByteArray().CopyTo(entry, 24);
        Encoding.ASCII.GetBytes("host").CopyTo(entry, 72);
        BitConverter.GetBytes(number).CopyTo(entry, 88);
        BitConverter.GetBytes(modified ?? Modified).CopyTo(entry, 100);
        BitConverter.GetBytes(-1).CopyTo(entry, 108);
        BitConverter.GetBytes((short)path.Length).CopyTo(entry, pathOffset);
        Encoding.Unicode.GetBytes(path).CopyTo(entry, pathOffset + 2);

        if (version == 1)
        {
            return entry;
        }

        sps ??= new byte[0];
        return entry.Concat(BitConverter.GetBytes(sps.Length)).Concat(sps).ToArray();
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(t => t).ToArray();

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    public void BuiltDestListParses(int version)
    {
        var dl = new DestList(Concat(Header(version, 2, 1), Entry(version, 1, @"C:\a.txt"),
            Entry(version, 2, @"C:\b.txt")));

        dl.ParseErrors.Should().BeEmpty();
        dl.Entries.Select(t => t.Path).Should().Equal(@"C:\a.txt", @"C:\b.txt");
        dl.Entries[0].Hostname.Should().Be("host");
        dl.Entries[0].MacAddress.Should().Be("00:15:5d:01:6d:02");
        dl.Entries[0].CreationTime.Should().Be(new DateTimeOffset(2015, 11, 24, 18, 39, 2, TimeSpan.Zero).AddTicks(4912714));
        dl.Entries[0].LastModified.Should().Be(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
    }

    [TestCase(0)]
    [TestCase(31)]
    public void DestListSmallerThanItsHeaderThrows(int size)
    {
        Action act = () => new DestList(new byte[size]);

        act.Should().Throw<JumpListParseException>();
    }

    [TestCase(1)]
    [TestCase(3)]
    public void TruncatedEntryKeepsTheEntriesBeforeIt(int version)
    {
        var data = Concat(Header(version, 2), Entry(version, 1, @"C:\a.txt"), Entry(version, 2, @"C:\b.txt"));

        var dl = new DestList(data.Take(data.Length - 6).ToArray());

        dl.Entries.Select(t => t.Path).Should().Equal(@"C:\a.txt");
        dl.ParseErrors.Should().ContainSingle();
    }

    [TestCase((short)-1)]
    [TestCase(short.MaxValue)]
    public void BadPathLengthStopsWithoutThrowing(short length)
    {
        var bad = Entry(3, 2, @"C:\b.txt");
        BitConverter.GetBytes(length).CopyTo(bad, 128);

        var dl = new DestList(Concat(Header(3, 2), Entry(3, 1, @"C:\a.txt"), bad));

        dl.Entries.Should().ContainSingle();
        dl.ParseErrors.Should().ContainSingle().Which.Should().Contain($"path of {length} characters");
    }

    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void BadPropertyStoreSizeStopsWithoutThrowing(int size)
    {
        var bad = Entry(3, 2, @"C:\b.txt");
        BitConverter.GetBytes(size).CopyTo(bad, bad.Length - 4);

        var dl = new DestList(Concat(Header(3, 2), Entry(3, 1, @"C:\a.txt"), bad));

        dl.Entries.Should().ContainSingle();
        dl.ParseErrors.Should().ContainSingle().Which.Should().Contain($"property store of {size} bytes");
    }

    [Test]
    public void OutOfRangeTimeIsReported()
    {
        var dl = new DestList(Concat(Header(3, 1), Entry(3, 1, @"C:\a.txt", modified: -5)));

        dl.Entries.Single().LastModified.Should().Be(DateTimeOffset.MinValue);
        dl.ParseErrors.Should().ContainSingle().Which.Should().Contain("Last modified time is out of range");
    }

    [Test]
    public void NonTimeBasedDroidUsesTheMarkers()
    {
        // A version 4 (random) GUID has no time or MAC address in it
        var dl = new DestList(Concat(Header(3, 1),
            Entry(3, 1, @"C:\a.txt", Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301"))));

        dl.Entries.Single().CreationTime.Should().Be(DestListEntry.NoCreationTime);
        dl.Entries.Single().MacAddress.Should().Be(DestListEntry.NoMacAddress);
    }

    [Test]
    public void ShellPathGuidsAreResolved()
    {
        // {20D04FE0-...} is My Computer; a bare GUID used to lose its first and last characters before the lookup
        var dl = new DestList(Concat(Header(3, 2), Entry(3, 1, @"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
            Entry(3, 2, @"::20D04FE0-3AEA-1069-A2D8-08002B30309D")));

        dl.Entries[0].Path.Should().EndWith("==> " + ExtensionBlocks.Utils.GetFolderNameFromGuid("20D04FE0-3AEA-1069-A2D8-08002B30309D"));
        dl.Entries[1].Path.Should().Be(dl.Entries[0].Path.Replace("::{20D04FE0-3AEA-1069-A2D8-08002B30309D} ", "::20D04FE0-3AEA-1069-A2D8-08002B30309D "));
    }

    [Test]
    public void RandomCorruptionOfARealDestListOnlyThrowsParseExceptions()
    {
        var ole = OleCf.OleCf.LoadFile(TestMain.Sample("Win10", "f01b4d95cf55d32a.automaticDestinations-ms"));
        var good = ole.GetPayloadForDirectory(ole.Directory.Single(t => t.DirectoryName == "DestList"));
        var random = new Random(3);

        for (var i = 0; i < 3000; i++)
        {
            var data = good.Take(random.Next(0, good.Length + 1)).ToArray();
            for (var flips = random.Next(0, 8); flips > 0 && data.Length > 0; flips--)
            {
                data[random.Next(data.Length)] = (byte)random.Next(256);
            }

            try
            {
                _ = new DestList(data);
            }
            catch (JumpListParseException)
            {
                // Expected when the header is cut off
            }
        }
    }

    [TestCase("Win10", "f01b4d95cf55d32a.automaticDestinations-ms")]
    [TestCase("dtformats", "1b4dd67f29cb1962.automaticDestinations-ms")]
    [TestCase("dtformats", "5afe4de1b92fc382.customDestinations-ms")]
    public void RandomCorruptionOfAWholeFileOnlyThrowsParseExceptions(string folder, string file)
    {
        var good = File.ReadAllBytes(TestMain.Sample(folder, file));
        var custom = file.EndsWith("customDestinations-ms");
        var random = new Random(5);

        for (var i = 0; i < 1500; i++)
        {
            var data = (byte[])good.Clone();
            for (var flips = random.Next(1, 12); flips > 0; flips--)
            {
                data[random.Next(data.Length)] = (byte)random.Next(256);
            }

            try
            {
                if (custom)
                {
                    _ = new CustomDestination(data, file);
                }
                else
                {
                    _ = new AutomaticDestination(data, file);
                }
            }
            catch (Exception ex) when (ex is JumpListParseException || ex is OleCfParseException)
            {
                // Expected when the container or footer is damaged
            }
        }
    }

    [Test]
    public void DamagedLnkStreamIsReportedAndTheOtherEntriesKept()
    {
        var path = TestMain.Sample("Win10", "f01b4d95cf55d32a.automaticDestinations-ms");
        var data = File.ReadAllBytes(path);

        // Break the class id of the first LNK in the file
        var lnkStart = IndexOf(data, new byte[] { 0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00 });
        lnkStart.Should().BePositive();
        data[lnkStart + 4] = 0xFF;

        var a = new AutomaticDestination(data, path);

        a.DestListEntries.Should().HaveCount(9);
        a.DestListEntries.Count(t => t.Lnk == null).Should().Be(1);
        a.ParseErrors.Should().ContainSingle().Which.Should().Contain("unable to parse LNK");
    }

    private static List<byte[]> SampleLnks()
    {
        var c = new CustomDestination(
            File.ReadAllBytes(TestMain.Sample("dtformats", "5afe4de1b92fc382.customDestinations-ms")), "x");
        var dir = Path.Combine(Path.GetTempPath(), "jl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            c.Entries[0].DumpAllLnkFiles(dir, "x");
            return Directory.GetFiles(dir).OrderBy(t => t).Select(File.ReadAllBytes).ToList();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    ///     A custom destinations file with a custom category, a known category and a tasks category
    /// </summary>
    private static byte[] MultiCategoryFile(List<byte[]> lnks)
    {
        byte[] Entries(IEnumerable<byte[]> items) => items.SelectMany(t => LnkClassId.Concat(t)).ToArray();
        var footer = new byte[] { 0xAB, 0xFB, 0xBF, 0xBA };

        return Concat(
            BitConverter.GetBytes(2), BitConverter.GetBytes(3), BitConverter.GetBytes(0),
            // Custom category with a name and two LNKs
            BitConverter.GetBytes(0), BitConverter.GetBytes((short)7), Encoding.Unicode.GetBytes("Pinned!"),
            BitConverter.GetBytes(2), Entries(lnks.Take(2)), footer,
            // Known category: recent
            BitConverter.GetBytes(1), BitConverter.GetBytes(2), footer,
            // A second custom category, which used to lose its name
            BitConverter.GetBytes(0), BitConverter.GetBytes((short)5), Encoding.Unicode.GetBytes("Other"),
            BitConverter.GetBytes(1), Entries(lnks.Skip(2).Take(1)), footer,
            // Tasks
            BitConverter.GetBytes(2), BitConverter.GetBytes(3), Entries(lnks.Skip(3).Take(3)), footer);
    }

    [Test]
    public void EveryCategoryOfACustomDestinationIsParsed()
    {
        var lnks = SampleLnks();
        lnks.Should().HaveCount(9);

        var c = new CustomDestination(MultiCategoryFile(lnks), "x.customDestinations-ms");

        c.ParseErrors.Should().BeEmpty();
        c.NumberOfCategories.Should().Be(3);
        c.Entries.Select(t => t.CategoryType).Should().Equal(0, 0, 2);
        c.Entries.Select(t => t.Name).Should().Equal("Pinned!", "Other", "");
        c.Entries.Select(t => t.LnkFiles.Count).Should().Equal(2, 1, 3);
        c.Entries.Select(t => t.NumberOfEntries).Should().Equal(2, 1, 3);
        c.Entries.SelectMany(t => t.LnkFiles).SelectMany(t => t.ParseErrors).Should().BeEmpty();
    }

    [Test]
    public void DamagedLnkInACustomDestinationIsReported()
    {
        var lnks = SampleLnks();
        lnks[1] = lnks[1].Take(30).ToArray(); // a LNK header cut short

        var c = new CustomDestination(MultiCategoryFile(lnks), "x.customDestinations-ms");

        c.Entries[0].LnkFiles.Should().ContainSingle();
        c.ParseErrors.Should().ContainSingle().Which.Should().Contain("unable to parse LNK");
        c.Entries.SelectMany(t => t.LnkFiles).Should().HaveCount(5);
    }

    private static int IndexOf(byte[] data, byte[] pattern) => CustomDestination.ByteSearch(data, pattern);
}
