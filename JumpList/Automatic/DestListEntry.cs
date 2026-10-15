using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ExtensionBlocks;
using Lnk.ExtraData;

namespace JumpList.Automatic;

public class DestListEntry
{
    /// <summary>
    ///     <see cref="CreationTime" /> when <see cref="FileDroid" /> is not a time-based (version 1) GUID, such as an
    ///     all-zero droid. Callers such as JLECmd treat the year 1582 as "no time".
    /// </summary>
    public static readonly DateTimeOffset NoCreationTime = new DateTimeOffset(1582, 10, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     <see cref="MacAddress" /> when <see cref="FileDroid" /> is not a time-based (version 1) GUID.
    /// </summary>
    public const string NoMacAddress = "00:00:00:00:00:00";

    private static readonly Regex GuidRegex =
        new Regex(
            @"\b[A-F0-9]{8}(?:-[A-F0-9]{4}){3}-[A-F0-9]{12}\b|\(\b[A-F0-9]{8}(?:-[A-F0-9]{4}){3}-[A-F0-9]{12}\b\)|\{\b[A-F0-9]{8}(?:-[A-F0-9]{4}){3}-[A-F0-9]{12}\b\}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DestListEntry(byte[] rawBytes, int version, int mruPosition, int spsSize = 0)
    {
        if (rawBytes == null)
        {
            throw new ArgumentNullException(nameof(rawBytes));
        }

        var pathSizeOffset = version > 1 ? 128 : 112;

        if (rawBytes.Length < pathSizeOffset + 2)
        {
            throw new JumpListParseException(
                $"A version {version} DestList entry is at least {pathSizeOffset + 2} bytes, not {rawBytes.Length}", 0,
                "DestList entry");
        }

        MRUPosition = mruPosition;

        Checksum = BitConverter.ToInt64(rawBytes, 0);

        VolumeDroid = ReadGuid(rawBytes, 8);
        FileDroid = ReadGuid(rawBytes, 24);
        VolumeBirthDroid = ReadGuid(rawBytes, 40);
        FileBirthDroid = ReadGuid(rawBytes, 56);

        if (rawBytes[73] == 0)
        {
            Hostname = Encoding.Unicode.GetString(rawBytes, 72, 16).Split('\0').First();
        }
        else
        {
            Hostname = Encoding.ASCII.GetString(rawBytes, 72, 16).Split('\0').First();
        }

        EntryNumber = BitConverter.ToInt32(rawBytes, 88);
        Unknown0 = BitConverter.ToInt32(rawBytes, 92);
        AccessCount = BitConverter.ToSingle(rawBytes, 96);

        var lastModified = BitConverter.ToInt64(rawBytes, 100);
        try
        {
            LastModified = new DateTimeOffset(DateTime.FromFileTimeUtc(lastModified));
        }
        catch (ArgumentOutOfRangeException)
        {
            ParseErrors.Add($"Last modified time is out of range (0x{lastModified:X16})");
            LastModified = DateTimeOffset.MinValue;
        }

        PinStatus = BitConverter.ToInt32(rawBytes, 108);

        if (version > 1)
        {
            InteractionCount = BitConverter.ToInt32(rawBytes, 116);
            Unknown3 = BitConverter.ToInt32(rawBytes, 120);
            Unknown4 = BitConverter.ToInt32(rawBytes, 124);
        }

        var pathChars = BitConverter.ToInt16(rawBytes, pathSizeOffset);
        var available = (rawBytes.Length - pathSizeOffset - 2) / 2;
        if (pathChars < 0 || pathChars > available)
        {
            ParseErrors.Add($"Path length is {pathChars} characters, but only {available} are available");
            pathChars = (short)Math.Max(0, Math.Min((int)pathChars, available));
        }

        Path = Encoding.Unicode.GetString(rawBytes, pathSizeOffset + 2, pathChars * 2);

        if (Path.StartsWith("knownfolder"))
        {
            var kfId = Path.Split('{').Last().TrimEnd('}');

            var fName = Utils.GetFolderNameFromGuid(kfId);

            Path = $"{Path} ==> {fName}";
        }

        if (Path.StartsWith("::"))
        {
            var newPathSegs = new List<string>();

            foreach (var pathSeg in Path.Split('\\'))
            {
                var matchResults = GuidRegex.Match(pathSeg);

                if (matchResults.Success)
                {
                    // Strip only the braces or parentheses, if any
                    var pguid = matchResults.Value.Trim('{', '}', '(', ')');
                    newPathSegs.Add(Utils.GetFolderNameFromGuid(pguid));
                }
                else
                {
                    newPathSegs.Add(pathSeg);
                }
            }

            Path = $"{Path} ==> {string.Join("\\", newPathSegs)}";
        }

        // The MAC address and creation time are only in time-based (version 1) GUIDs
        if (IsTimeBased(FileDroid))
        {
            var tempMac = FileDroid.ToString().Split('-').Last();
            MacAddress = Regex.Replace(tempMac, ".{2}", "$0:").TrimEnd(':');
            CreationTime = GetDateTimeOffsetFromGuid(FileDroid);
        }
        else
        {
            MacAddress = NoMacAddress;
            CreationTime = NoCreationTime;
        }

        if (spsSize > 0)
        {
            // A damaged property store shouldn't lose the rest of the entry
            try
            {
                Sps = new PropertySheet(rawBytes.Skip(rawBytes.Length - spsSize).ToArray());
            }
            catch (Exception ex)
            {
                ParseErrors.Add($"Unable to parse property store ({spsSize} bytes): {ex.Message}");
            }
        }
    }

    public long Checksum { get; }
    public int EntryNumber { get; }
    public int MRUPosition { get; }
    public Guid FileBirthDroid { get; }
    public Guid FileDroid { get; }
    public string Hostname { get; }

    /// <summary>
    ///     UTC. DateTimeOffset.MinValue when the stored value is out of range.
    /// </summary>
    public DateTimeOffset LastModified { get; }

    public string Path { get; }
    public int PinStatus { get; }
    public int Unknown0 { get; }
    public float AccessCount { get; }
    public int InteractionCount { get; }
    public int Unknown3 { get; }
    public int Unknown4 { get; }
    public Guid VolumeBirthDroid { get; }
    public Guid VolumeDroid { get; }

    public PropertySheet Sps { get; }

    /// <summary>
    ///     Time from <see cref="FileDroid" />, or <see cref="NoCreationTime" /> when it is not time-based.
    /// </summary>
    public DateTimeOffset CreationTime { get; }

    /// <summary>
    ///     MAC address from <see cref="FileDroid" />, or <see cref="NoMacAddress" /> when it is not time-based.
    /// </summary>
    public string MacAddress { get; }

    /// <summary>
    ///     Problems with this entry, such as an out-of-range time or a path longer than the entry.
    /// </summary>
    public List<string> ParseErrors { get; } = new List<string>();

    private static Guid ReadGuid(byte[] rawBytes, int offset)
    {
        var bytes = new byte[16];
        Buffer.BlockCopy(rawBytes, offset, bytes, 0, 16);
        return new Guid(bytes);
    }

    private static bool IsTimeBased(Guid guid)
    {
        return guid.ToByteArray()[7] >> 4 == 1;
    }

    private static DateTimeOffset GetDateTimeOffsetFromGuid(Guid guid)
    {
        // UUID version 1 timestamps count 100ns intervals from the start of the Gregorian calendar
        var bytes = guid.ToByteArray();

        // clear the version
        bytes[7] &= 0x0f;

        var timestamp = BitConverter.ToInt64(bytes, 0);

        // 60 bits from 1582 always fits in a DateTimeOffset
        return new DateTimeOffset(NoCreationTime.Ticks + timestamp, TimeSpan.Zero);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Checksum: {Checksum}");
        sb.AppendLine($"VolumeDroid: {VolumeDroid}");
        sb.AppendLine($"VolumeBirthDroid: {VolumeBirthDroid}");
        sb.AppendLine($"FileDroid: {FileDroid}");
        sb.AppendLine($"FileBirthDroid: {FileBirthDroid}");
        sb.AppendLine($"Hostname: {Hostname}");
        sb.AppendLine($"EntryNumber: {EntryNumber}");
        sb.AppendLine($"MRUPosition: {MRUPosition}");
        sb.AppendLine($"LastMod: {LastModified}");
        sb.AppendLine($"PinStatus: {PinStatus}");
        sb.AppendLine($"Path: {Path}");
        sb.AppendLine($"MacAddress: {MacAddress}");
        sb.AppendLine($"CreationTime: {CreationTime}");
        sb.AppendLine($"Unknown0: {Unknown0}");
        sb.AppendLine($"AccessCount: {AccessCount}");
        sb.AppendLine($"InteractionCount: {InteractionCount}");
        sb.AppendLine($"Unknown3: {Unknown3}");
        sb.AppendLine($"Unknown4: {Unknown4}");

        return sb.ToString();
    }
}
