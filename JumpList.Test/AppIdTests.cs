using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace JumpList.Test;

[TestFixture]
[NonParallelizable] // JumpList.AppIdList is static
public class AppIdTests
{
    [Test]
    public void BuiltInListLoadsEveryAppId()
    {
        // Count the distinct ids in the resource file itself, whatever its line endings
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JumpList.slnx")))
        {
            dir = dir.Parent;
        }

        var lines = File.ReadAllLines(Path.Combine(dir!.FullName, "JumpList", "Resources", "AppIDs.txt"));
        var ids = lines.Select(t => t.Split('|')).Where(t => t.Length == 2)
            .Select(t => t[0].Trim().Trim('"').ToLowerInvariant()).Distinct().Count();

        ids.Should().BeGreaterThan(700);
        new AppIdList().Count.Should().Be(ids);
    }

    [TestCase("f01b4d95cf55d32a")]
    [TestCase("F01B4D95CF55D32A")]
    [TestCase("F01b4d95CF55d32a")]
    public void LookupIsCaseInsensitive(string id)
    {
        // Mixed or upper case used to throw KeyNotFoundException
        new AppIdList().GetDescriptionFromId(id).Should().Be("Windows Explorer Windows 8.1");
    }

    [TestCase("0000000000000000")]
    [TestCase("")]
    [TestCase(null)]
    public void UnknownIdIsUnknownAppId(string id)
    {
        // Used to return null
        new AppIdList().GetDescriptionFromId(id).Should().Be("Unknown AppId");
    }

    [Test]
    public void AppIdsLoadedFromAFileSurviveLoadingJumpLists()
    {
        var file = Path.GetTempFileName();
        try
        {
            // LF line endings and an id that overrides a built-in one
            File.WriteAllText(file, "\"ABCDEF0123456789\"|\"My test app\"\n\"F01B4D95CF55D32A\"|\"Overridden\"\n");

            var original = JumpList.AppIdList;
            try
            {
                JumpList.AppIdList = new AppIdList();
                JumpList.AppIdList.LoadAppListFromFile(file).Should().Be(1);

                // Loading jump lists used to replace AppIdList, discarding these
                var a = JumpList.LoadAutoJumplist(TestMain.Sample("Win10", "f01b4d95cf55d32a.automaticDestinations-ms"));
                JumpList.LoadCustomJumplist(TestMain.Sample("dtformats", "5afe4de1b92fc382.customDestinations-ms"));

                a.AppId.Description.Should().Be("Overridden");
                JumpList.AppIdList.GetDescriptionFromId("abcdef0123456789").Should().Be("My test app");
            }
            finally
            {
                JumpList.AppIdList = original;
            }
        }
        finally
        {
            File.Delete(file);
        }
    }
}
