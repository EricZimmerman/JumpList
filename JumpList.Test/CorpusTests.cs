using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using JumpList.Automatic;
using JumpList.Custom;
using NUnit.Framework;

namespace JumpList.Test;

/// <summary>
///     Runs over a local collection of real jump lists that is not in the repository: the "Jump lists and lnks"
///     folder next to the repository, or the folder in the JUMPLIST_CORPUS environment variable. Skipped when missing.
/// </summary>
[TestFixture]
public class CorpusTests
{
    // Known oddities in the corpus
    private const string TruncatedFile = "9b9cdc69c1c24e2b.automaticDestinations-ms"; // in Tom/; the file is cut short

    private static string CorpusPath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("JUMPLIST_CORPUS");
        if (!string.IsNullOrEmpty(fromEnvironment) && Directory.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Jump lists and lnks");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        Assert.Ignore("Jump list corpus not found");
        return null;
    }

    private static bool IsTruncatedSample(string file) =>
        file.Replace('\\', '/').EndsWith("/Tom/AutomaticDestinations/" + TruncatedFile);

    [Test]
    public void EveryAutomaticDestinationParses()
    {
        var files = Directory.GetFiles(CorpusPath(), "*.automaticDestinations-ms", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();

        var entries = 0;
        foreach (var file in files)
        {
            var a = new AutomaticDestination(File.ReadAllBytes(file), file);

            if (IsTruncatedSample(file))
            {
                continue;
            }

            a.ParseErrors.Should().BeEmpty(file);
            a.DestListEntries.Should().HaveCount(a.DestListCount, file);
            // PinnedDestListCount is the header's count. In one edited sample the header says 2 but one entry is pinned
            if (!file.Contains("(ExtraDirectories)"))
            {
                a.DestListEntries.Count(t => t.Pinned).Should().Be(a.PinnedDestListCount, file);
            }

            a.DestListEntries.Where(t => t.Lnk == null).Should().BeEmpty(file);
            entries += a.DestListEntries.Count;
        }

        entries.Should().BeGreaterThan(20000);
    }

    [Test]
    public void TruncatedAutomaticDestinationKeepsWhatIsThere()
    {
        var file = Path.Combine(CorpusPath(), "Tom", "AutomaticDestinations", TruncatedFile);
        if (!File.Exists(file))
        {
            Assert.Ignore("Sample not in the corpus");
        }

        // The old code failed the whole file
        var a = new AutomaticDestination(File.ReadAllBytes(file), file);

        a.DestListCount.Should().Be(2);
        var entry = a.DestListEntries.Should().ContainSingle().Subject;
        entry.EntryNumber.Should().Be(2);
        entry.Lnk.Should().BeNull();
        a.ParseErrors.Should().Contain(t => t.Contains("The mini stream is truncated"));
        a.ParseErrors.Should().Contain(t => t.StartsWith("Stream 2: unable to parse LNK"));
    }

    [Test]
    public void EveryCustomDestinationParses()
    {
        var files = Directory.GetFiles(CorpusPath(), "*.customDestinations-ms", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();

        var lnks = 0;
        var named = 0;
        foreach (var file in files)
        {
            var data = File.ReadAllBytes(file);

            if (data.Length <= 24)
            {
                // Empty jump lists, which Windows writes as a bare header
                ((Action)(() => new CustomDestination(data, file))).Should().Throw<JumpListParseException>();
                continue;
            }

            var c = new CustomDestination(data, file);

            c.ParseErrors.Should().BeEmpty(file);
            c.Entries.Should().OnlyContain(t => t.CategoryType >= 0 && t.CategoryType <= 2, file);

            // LNKs no longer carry the next entry's class id, which Lnk reported as trailing data
            c.Entries.SelectMany(t => t.LnkFiles).SelectMany(t => t.ParseErrors).Should().BeEmpty(file);

            lnks += c.Entries.Sum(t => t.LnkFiles.Count);
            named += c.Entries.Count(t => t.Name.Length > 0);
        }

        lnks.Should().BeGreaterThan(100);
        named.Should().BeGreaterThan(0, "custom categories after the first have names");
    }
}
