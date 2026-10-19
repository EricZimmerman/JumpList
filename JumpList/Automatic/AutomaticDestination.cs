using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using ExtensionBlocks;
using Lnk;
using OleCf;

namespace JumpList.Automatic;

public class AutomaticDestination
{
    private readonly int _codepage;

    // Each directory entry's stream, read once while parsing; the OLE container is not kept, since it holds the whole
    // file and a copy of its mini stream (135 MB for a 106 MB set of jump lists). An LNK stream's array is the one its
    // LnkFile.RawBytes holds, so this costs only the DestList and DestListPropertyStore. A stream that could not be
    // read keeps its exception, rethrown by GetLnkFromDirectoryName as before
    private readonly Dictionary<DirectoryEntry, byte[]> _streams = new Dictionary<DirectoryEntry, byte[]>();
    private readonly Dictionary<DirectoryEntry, ExceptionDispatchInfo> _streamErrors = new Dictionary<DirectoryEntry, ExceptionDispatchInfo>();

    /// <summary>
    ///     Parses an automatic destinations jump list. A damaged DestList, property store or LNK stream is reported in
    ///     <see cref="ParseErrors" /> and the rest is still parsed.
    /// </summary>
    /// <exception cref="JumpListParseException">The data is empty</exception>
    /// <exception cref="OleCfParseException">The data is not a usable OLE compound file</exception>
    public AutomaticDestination(byte[] rawBytes, string sourceFile, int codepage = 1252)
    {
        if (rawBytes == null)
        {
            throw new ArgumentNullException(nameof(rawBytes));
        }

        if (rawBytes.Length == 0)
        {
            throw new JumpListParseException("Empty file", 0, "File");
        }

        SourceFile = sourceFile;
        _codepage = codepage;

        var appid = Path.GetFileName(sourceFile ?? string.Empty).Split('.').FirstOrDefault();
        if (appid != null)
        {
            var aid = new AppIdInfo(appid);
            AppId = aid;
        }
        else
        {
            AppId = new AppIdInfo("Unable to determine AppId");
        }

        var oleContainer = new OleCfFile(rawBytes, sourceFile);

        Directory = oleContainer.Directory;
        ParseErrors.AddRange(oleContainer.ParseErrors.Select(t => $"OLE container: {t}"));

        // (the root entry's payload is the mini stream itself: not a stream of the jump list)
        foreach (var dirItem in Directory.Where(t => t.DirectoryType != DirectoryEntry.DirectoryEntryTypes.RootStorage))
        {
            try
            {
                _streams[dirItem] = oleContainer.GetPayloadForDirectory(dirItem);
            }
            catch (Exception ex)
            {
                _streamErrors[dirItem] = ExceptionDispatchInfo.Capture(ex);
            }
        }

        var destList =
            Directory.FirstOrDefault(t => t.DirectoryName.ToLowerInvariant() == "destlist");
        if (destList != null && destList.DirectorySize > 0)
        {
            try
            {
                var destBytes = GetStream(destList);

                if (destBytes != null)
                {
                    DestList = new DestList(destBytes);
                    ParseErrors.AddRange(DestList.ParseErrors.Select(t => $"DestList: {t}"));
                }
            }
            catch (Exception ex) when (ex is OleCfParseException || ex is JumpListParseException)
            {
                ParseErrors.Add($"DestList: {ex.Message}");
            }
        }

        var destListPropertyStore =
            Directory.FirstOrDefault(t => t.DirectoryName == "DestListPropertyStore");
        if (destListPropertyStore != null && destListPropertyStore.DirectorySize > 0)
        {
            try
            {
                var destListPropertyStoreBytes = GetStream(destListPropertyStore);

                EmptyDestListPropertyStore = true;

                if (destListPropertyStoreBytes != null && destListPropertyStoreBytes.Length >= 4 &&
                    BitConverter.ToInt32(destListPropertyStoreBytes, 0) > 0)
                {
                    EmptyDestListPropertyStore = false;
                    DestListPropertyStore = new PropertySheet(destListPropertyStoreBytes.Skip(4).ToArray());
                }
            }
            catch (Exception ex)
            {
                ParseErrors.Add($"DestListPropertyStore: {ex.Message}");
            }
        }

        DestListEntries = new List<AutoDestList>();

        if (DestList != null)
        {
            DestListCount = DestList.Header.NumberOfEntries;
            PinnedDestListCount = DestList.Header.NumberOfPinnedEntries;
            DestListVersion = DestList.Header.Version;
            LastUsedEntryNumber = DestList.Header.LastEntryNumber;

            foreach (var entry in DestList.Entries)
            {
                // The stream for an entry is named with its number in hex
                var dirItem =
                    Directory.FirstOrDefault(
                        t =>
                            string.Equals(t.DirectoryName, entry.EntryNumber.ToString("X"),
                                StringComparison.InvariantCultureIgnoreCase));

                LnkFile dlnk = null;

                if (dirItem != null)
                {
                    dlnk = ReadLnk(dirItem);
                }

                if (entry.Sps != null)
                {
                    HasSps = true;
                }

                DestListEntries.Add(new AutoDestList(entry, dlnk, entry.InteractionCount));
            }
        }
    }

    /// <summary>
    ///     Problems that didn't stop parsing: a damaged DestList, property store or LNK stream, or problems in the OLE
    ///     container. An entry whose LNK couldn't be read has a null Lnk.
    /// </summary>
    public List<string> ParseErrors { get; } = new List<string>();

    private LnkFile ReadLnk(DirectoryEntry dirItem)
    {
        var sfn = $"{SourceFile}_Directory name_{dirItem.DirectoryName:X}";

        try
        {
            var p = GetStream(dirItem);

            if (p == null || p.Length == 0)
            {
                ParseErrors.Add($"Stream {dirItem.DirectoryName} is empty");
                return null;
            }

            return new LnkFile(p, sfn, _codepage);
        }
        catch (Exception ex)
        {
            ParseErrors.Add($"Stream {dirItem.DirectoryName}: unable to parse LNK: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     A stream's bytes as the OLE container gave them while parsing (the same array each time), or its exception
    /// </summary>
    private byte[] GetStream(DirectoryEntry dirItem)
    {
        if (_streamErrors.TryGetValue(dirItem, out var error))
        {
            error.Throw();
        }

        return _streams.TryGetValue(dirItem, out var bytes) ? bytes : null;
    }

    public List<DirectoryEntry> Directory { get; }

    public AppIdInfo AppId { get; }

    public int DestListCount { get; }
    public int PinnedDestListCount { get; }
    public int LastUsedEntryNumber { get; }

    public int DestListVersion { get; }

    public string SourceFile { get; }

    private DestList DestList { get; }
    
    public bool HasSps { get; }
    
    public PropertySheet DestListPropertyStore { get; }
    public bool EmptyDestListPropertyStore { get; }

    public List<AutoDestList> DestListEntries { get; }

    /// <summary>
    ///     Parses the LNK in a stream, using the codepage the jump list was loaded with. Null when there is no such
    ///     stream, and for the root entry (its payload is the mini stream, which held every small stream; parsed as an
    ///     LNK, that gave the first one with junk after it)
    /// </summary>
    public LnkFile GetLnkFromDirectoryName(string dirName)
    {
        var dirItem =
            Directory.FirstOrDefault(
                t =>
                    string.Equals(t.DirectoryName, dirName,
                        StringComparison.InvariantCultureIgnoreCase));

        if (dirItem != null && dirItem.DirectoryType != DirectoryEntry.DirectoryEntryTypes.RootStorage)
        {
            var sfn = $"{SourceFile}_Directory name_{dirItem.DirectoryName:X}";

            var p = GetStream(dirItem);

            var dlnk = new LnkFile(p, sfn, _codepage);

            return dlnk;
        }

        return null;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($">>Source: {SourceFile}");
        sb.AppendLine($"    AppId: {AppId}");
        sb.AppendLine($"    Directory contains {Directory.Count:N0} items (including Root storage and DestList). Has Sps: {HasSps}");

        if (DestList != null)
        {
            sb.AppendLine(
                $"    DestList (v{DestList?.Header.Version}) entries Expected: {DestListCount}, Actual: {DestListEntries.Count}");
        }
        else
        {
            sb.AppendLine("    Jump list contains no DestList entries");
        }

        sb.AppendLine();

        if (DestListPropertyStore != null)
        {
            sb.AppendLine("    Jump list DestList Property Store");
            
            foreach (var propertyName in DestListPropertyStore.PropertyNames)
            {
                sb.AppendLine($"         Property: {propertyName.Key} --> {propertyName.Value}");
            }
        }

        foreach (var entry in DestListEntries)
        {
            sb.AppendLine($"    Entry #: {entry.EntryNumber}, Path: {entry.Path}");
            sb.AppendLine($"    Created: {entry.CreatedOn}, Modified: {entry.LastModified}");
            sb.AppendLine($"    Hostname: {entry.Hostname}, MAC Address: {entry.MacAddress}");

            if (entry.Lnk != null)
            {
                sb.AppendLine($"    lnk file flags: {entry.Lnk.Header.DataFlags}");
                sb.AppendLine($"    Target created: {entry.Lnk.Header.TargetCreationDate}");
                sb.AppendLine($"    Target modified: {entry.Lnk.Header.TargetModificationDate}");
                sb.AppendLine($"    Target accessed: {entry.Lnk.Header.TargetLastAccessedDate}");
            }

            if (entry.Sps != null)
            {
                sb.AppendLine("Property store info");
                sb.AppendLine(entry.Sps.ToString());
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public void DumpAllLnkFiles(string outDir)
    {
        if (System.IO.Directory.Exists(outDir) == false)
        {
            System.IO.Directory.CreateDirectory(outDir);
        }

        foreach (var directoryItem in Directory)
        {
            if (directoryItem.DirectoryName.ToLowerInvariant() == "root entry" ||
                directoryItem.DirectoryName.ToLowerInvariant() == "destlist")
            {
                continue;
            }

            if (directoryItem.DirectoryType != DirectoryEntry.DirectoryEntryTypes.Stream)
            {
                continue;
            }

            byte[] lnkBytes;
            try
            {
                lnkBytes = GetStream(directoryItem);
            }
            catch (OleCfParseException)
            {
                continue;
            }

            if (lnkBytes == null || lnkBytes.Length == 0 || lnkBytes[0] != 0x4c)
            {
                //this isn't a lnk file since it doesn't start with 0x4c, so continue
                continue;
            }
            var fName = $"AppId_{AppId.AppId}_DirName_{directoryItem.DirectoryName}.lnk";
            var outPath = Path.Combine(outDir, fName);


            File.WriteAllBytes(outPath, lnkBytes);
        }
    }
}


public class AutoDestList
{
    public AutoDestList(DestListEntry destEntry, LnkFile lnk, int interactionCount)
    {
        Hostname = destEntry.Hostname;
        VolumeDroid = destEntry.VolumeDroid;
        VolumeBirthDroid = destEntry.VolumeBirthDroid;
        FileDroid = destEntry.FileDroid;
        FileBirthDroid = destEntry.FileBirthDroid;
        EntryNumber = destEntry.EntryNumber;
        MRUPosition = destEntry.MRUPosition;
        CreatedOn = destEntry.CreationTime;
        LastModified = destEntry.LastModified;
        Pinned = destEntry.PinStatus != -1;
        Path = destEntry.Path;
        MacAddress = destEntry.MacAddress;
        Sps = destEntry.Sps;

        Lnk = lnk;
        InteractionCount = interactionCount;
    }

    public string Hostname { get; }
    public Guid VolumeDroid { get; }
    public Guid VolumeBirthDroid { get; }
    public Guid FileDroid { get; }
    public Guid FileBirthDroid { get; }
    public int EntryNumber { get; }
    public int MRUPosition { get; }
    public int InteractionCount { get; }
    public DateTimeOffset CreatedOn { get; }
    public DateTimeOffset LastModified { get; }
    public bool Pinned { get; }
    
    public PropertySheet Sps { get;}
    
    public string Path { get; }
    public string MacAddress { get; }

    public LnkFile Lnk { get; }
}