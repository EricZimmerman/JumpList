# Automatic and Custom Destination Jump List parser

Parses Windows jump lists: automatic destinations (`*.automaticDestinations-ms`, OLE compound files with a DestList and one LNK stream per entry) and custom destinations (`*.customDestinations-ms`, categories of LNK files). Used by [JLECmd](https://github.com/EricZimmerman/JLECmd).

```csharp
var auto = JumpList.JumpList.LoadAutoJumplist(@"C:\Temp\f01b4d95cf55d32a.automaticDestinations-ms");
foreach (var entry in auto.DestListEntries)
{
    Console.WriteLine($"{entry.EntryNumber} {entry.Path} {entry.LastModified} pinned: {entry.Pinned} {entry.Lnk?.LocalPath}");
}

var custom = JumpList.JumpList.LoadCustomJumplist(@"C:\Temp\5afe4de1b92fc382.customDestinations-ms");
```

- AppIds are looked up case-insensitively (`JumpList.AppIdList`, about 730 built in). Add your own with `JumpList.AppIdList.LoadAppListFromFile` (`"APPID"|"Description"` per line); they are kept for every jump list loaded afterwards.
- Unusable files throw `JumpListParseException` or `OleCfParseException`. A damaged DestList entry, property store or LNK is listed in `ParseErrors` and the rest of the jump list is still parsed (an entry whose LNK can't be read has `Lnk == null`).
- When a DestList entry's file droid isn't a time-based GUID (for example all zeros), `CreatedOn` is 1582-10-15 and `MacAddress` is `00:00:00:00:00:00`.

Open Source Development funding and support provided by the following contributors: [SANS Institute](http://sans.org/) and [SANS DFIR](http://dfir.sans.org/).
