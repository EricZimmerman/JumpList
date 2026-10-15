using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace JumpList.Custom;

/// <summary>
///     A custom destinations jump list (*.customDestinations-ms).
/// </summary>
/// <remarks>
///     A 12 byte header (format version, number of categories, unknown) and then categories, each ending with the
///     footer 0xBABFFBAB. Each <see cref="Entry" /> is one category.
/// </remarks>
public class CustomDestination
{
    private const int FileHeaderSize = 12;

    private readonly byte[] footerBytes = {0xAB, 0xFB, 0xBF, 0xBA};

    /// <summary>
    ///     Parses a custom destinations jump list. A LNK that can't be parsed is reported in <see cref="ParseErrors" />.
    /// </summary>
    /// <exception cref="JumpListParseException">The data is empty, too small or has no footer</exception>
    public CustomDestination(byte[] rawBytes, string sourceFile, int codepage = 1252)
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

        if (rawBytes.Length <= 24)
        {
            throw new JumpListParseException("Empty custom destinations jump list", 0, "File");
        }

        var footerSig = BitConverter.ToInt32(footerBytes, 0);
        var fileSig = BitConverter.ToInt32(rawBytes, rawBytes.Length - 4);

        if (footerSig != fileSig)
        {
            throw new JumpListParseException("Invalid signature (footer missing)", rawBytes.Length - 4, "Footer");
        }

        Version = BitConverter.ToInt32(rawBytes, 0);
        NumberOfCategories = BitConverter.ToInt32(rawBytes, 4);

        Entries = new List<Entry>();

        var index = 0;

        //first, check for footer offsets. some files have more than one (one per category)

        var footerOffsets = new List<int>();

        while (index < rawBytes.Length)
        {
            var lo = ByteSearch(rawBytes, footerBytes, index);

            if (lo == -1)
            {
                break;
            }

            footerOffsets.Add(lo);

            index = lo + footerBytes.Length; //add length so we do not hit on it again
        }

        var chunkStart = 0;
        foreach (var footerOffset in footerOffsets)
        {
            var chunkSize = footerOffset - chunkStart + 4;
            var bytes = new byte[chunkSize];

            Buffer.BlockCopy(rawBytes, chunkStart, bytes, 0, bytes.Length);

            // The first chunk starts with the file header; later chunks start with their category
            var categoryOffset = chunkStart == 0 ? FileHeaderSize : 0;

            if (bytes.Length > 30)
            {
                var e = new Entry(bytes, chunkStart, codepage, categoryOffset);

                foreach (var parseError in e.ParseErrors)
                {
                    ParseErrors.Add($"Category at 0x{chunkStart:X}: {parseError}");
                }

                Entries.Add(e);
            }

            chunkStart += chunkSize;
        }
    }

    public string SourceFile { get; }

    public AppIdInfo AppId { get; }

    /// <summary>
    ///     Format version from the file header (2).
    /// </summary>
    public int Version { get; }

    /// <summary>
    ///     Number of categories from the file header.
    /// </summary>
    public int NumberOfCategories { get; }

    /// <summary>
    ///     One entry per category.
    /// </summary>
    public List<Entry> Entries { get; }

    /// <summary>
    ///     Problems that didn't stop parsing, such as a LNK that couldn't be parsed.
    /// </summary>
    public List<string> ParseErrors { get; } = new List<string>();

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Source: {SourceFile}");
        sb.AppendLine($"    AppId: {AppId}");
        sb.AppendLine($"    Total entries: {Entries.Count}");

        var entryNum = 0;
        foreach (var entry in Entries)
        {
            sb.AppendLine($"    Entry #: {entryNum}, Rank: {entry.Rank}");

            if (entry.Name.Length > 0)
            {
                sb.AppendLine($"   Name: {entry.Name}");
            }

            sb.AppendLine($"    Total lnk count: {entry.LnkFiles.Count}");

            foreach (var lnkFile in entry.LnkFiles)
            {
                sb.AppendLine($"     lnk header flags: {lnkFile.Header.DataFlags}");
                sb.AppendLine($"      Target created: {lnkFile.Header.TargetCreationDate}");
                sb.AppendLine($"      Target modified: {lnkFile.Header.TargetModificationDate}");
                sb.AppendLine($"      Target accessed: {lnkFile.Header.TargetLastAccessedDate}");
            }

            sb.AppendLine();
            entryNum += 1;
        }


        return sb.ToString();
    }


    public static int ByteSearch(byte[] searchIn, byte[] searchBytes, int start = 0)
    {
        var found = -1;
        var matched = false;
        //only look at this if we have a populated search array and search bytes with a sensible start
        if (searchIn.Length > 0 && searchBytes.Length > 0 && start <= searchIn.Length - searchBytes.Length &&
            searchIn.Length >= searchBytes.Length)
        {
            //iterate through the array to be searched
            for (var i = start; i <= searchIn.Length - searchBytes.Length; i++)
            {
                //if the start bytes match we will start comparing all other bytes
                if (searchIn[i] == searchBytes[0])
                {
                    if (searchIn.Length > 1)
                    {
                        //multiple bytes to be searched we have to compare byte by byte
                        matched = true;
                        for (var y = 1; y <= searchBytes.Length - 1; y++)
                        {
                            if (searchIn[i + y] != searchBytes[y])
                            {
                                matched = false;
                                break;
                            }
                        }

                        //everything matched up
                        if (matched)
                        {
                            found = i;
                            break;
                        }
                    }
                    else
                    {
                        //search byte is only one bit nothing else to do
                        found = i;
                        break; //stop the loop
                    }
                }
            }
        }

        return found;
    }
}
