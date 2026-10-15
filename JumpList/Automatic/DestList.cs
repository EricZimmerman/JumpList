using System;
using System.Collections.Generic;
using System.Text;

namespace JumpList.Automatic;

/// <summary>
///     The DestList stream of an automatic destinations jump list.
/// </summary>
/// <remarks>
///     Version 1 (Windows 7/8) entries are 114 bytes plus the path. Version 2 and later (Windows 10/11) entries are
///     130 bytes plus the path, followed by a 32-bit size and that many bytes of property store (0 when there is none).
/// </remarks>
public class DestList
{
    private const int V1PathSizeOffset = 112;
    private const int V2PathSizeOffset = 128;

    /// <exception cref="JumpListParseException">rawBytes is shorter than the 32 byte header</exception>
    public DestList(byte[] rawBytes)
    {
        if (rawBytes == null)
        {
            throw new ArgumentNullException(nameof(rawBytes));
        }

        if (rawBytes.Length < DestListHeader.Size)
        {
            throw new JumpListParseException(
                $"The DestList is {rawBytes.Length} bytes, smaller than its {DestListHeader.Size} byte header", 0,
                "DestList header");
        }

        Entries = new List<DestListEntry>();

        var headerBytes = new byte[DestListHeader.Size];
        Buffer.BlockCopy(rawBytes, 0, headerBytes, 0, DestListHeader.Size);

        Header = new DestListHeader(headerBytes);

        var index = DestListHeader.Size;
        var mruPos = 0;

        var pathSizeOffset = Header.Version == 1 ? V1PathSizeOffset : V2PathSizeOffset;

        // Version 1 stops at the header's entry count; later versions read to the end of the stream
        while (index < rawBytes.Length && (Header.Version != 1 || Entries.Count < Header.NumberOfEntries))
        {
            var remaining = rawBytes.Length - index;

            if (remaining < pathSizeOffset + 2)
            {
                ParseErrors.Add($"{remaining} trailing byte(s) at 0x{index:X} are too short for a DestList entry");
                break;
            }

            var pathSize = BitConverter.ToInt16(rawBytes, index + pathSizeOffset);
            //now that we know pathSize we can determine how big each record is
            var entrySize = pathSizeOffset + 2 + pathSize * 2;

            if (pathSize < 0 || entrySize > remaining)
            {
                ParseErrors.Add(
                    $"DestList entry at 0x{index:X} has a path of {pathSize} characters, but only {remaining} bytes remain. Stopping");
                break;
            }

            var spsSize = 0;
            var stride = entrySize;

            if (Header.Version != 1)
            {
                //after the path, if the 4 bytes are 0, theres no extra data, but if not, its an SPS
                if (remaining < entrySize + 4)
                {
                    ParseErrors.Add($"DestList entry at 0x{index:X} is missing its property store size. Stopping");
                    break;
                }

                spsSize = BitConverter.ToInt32(rawBytes, index + entrySize);

                if (spsSize < 0 || spsSize > remaining - entrySize - 4)
                {
                    ParseErrors.Add(
                        $"DestList entry at 0x{index:X} has a property store of {spsSize} bytes, but only {remaining - entrySize - 4} remain. Stopping");
                    break;
                }

                stride = entrySize + 4 + spsSize;
            }

            var entryBytes = new byte[stride];
            Buffer.BlockCopy(rawBytes, index, entryBytes, 0, stride);

            var entry = new DestListEntry(entryBytes, Header.Version, mruPos, spsSize);

            foreach (var parseError in entry.ParseErrors)
            {
                ParseErrors.Add($"DestList entry {entry.EntryNumber} at 0x{index:X}: {parseError}");
            }

            Entries.Add(entry);

            index += stride;
            mruPos += 1;
        }
    }

    public DestListHeader Header { get; }

    public List<DestListEntry> Entries { get; }

    /// <summary>
    ///     Problems found while parsing, such as an entry that runs past the end of the stream.
    /// </summary>
    public List<string> ParseErrors { get; } = new List<string>();

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Header: {Header}");

        sb.AppendLine("");
        sb.AppendLine($"DestList Entries Expected: {Header.NumberOfEntries:N0}, Found: {Entries.Count:N0}");
        foreach (var destListEntry in Entries)
        {
            sb.AppendLine(destListEntry.ToString());
        }

        return sb.ToString();
    }
}
