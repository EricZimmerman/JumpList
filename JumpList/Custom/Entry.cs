using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lnk;

namespace JumpList.Custom;

/// <summary>
///     One category of a custom destinations jump list and the LNK files in it.
/// </summary>
/// <remarks>
///     A category starts with its type: 0 (custom: name length, UTF-16 name, number of entries), 1 (known: category
///     id, 1 = frequent, 2 = recent) or 2 (tasks: number of entries). Each entry is a 16 byte class id followed by the
///     shell object, which for LNK files is the LNK data.
/// </remarks>
public class Entry
{
    private const int ClassIdSize = 16;

    private readonly byte[] footerBytes = {0xAB, 0xFB, 0xBF, 0xBA};

    // LNK header size (0x4C) and LNK class id, which is also the class id before each shell object
    private readonly byte[] lnkHeaderBytes =
    {
        0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
    };

    private readonly Dictionary<string, byte[]> lnkBytes;

    /// <summary>
    ///     Parses the first chunk of a file, where the category follows the 12 byte file header.
    /// </summary>
    public Entry(byte[] rawBytes, int entryOffset, int codepage = 1252) : this(rawBytes, entryOffset, codepage, 12)
    {
    }

    /// <param name="rawBytes">The chunk, up to and including its footer</param>
    /// <param name="entryOffset">Offset of the chunk in the file, used in LNK names</param>
    /// <param name="codepage">Codepage for LNK strings</param>
    /// <param name="categoryOffset">Where the category starts: 12 in the first chunk (after the file header), else 0</param>
    public Entry(byte[] rawBytes, int entryOffset, int codepage, int categoryOffset)
    {
        if (rawBytes == null)
        {
            throw new ArgumentNullException(nameof(rawBytes));
        }

        LnkFiles = new List<LnkFile>();
        lnkBytes = new Dictionary<string, byte[]>();
        Name = string.Empty;

        if (rawBytes.Length < Math.Max(16, categoryOffset + 4))
        {
            throw new JumpListParseException(
                $"Category of {rawBytes.Length} bytes is too short", entryOffset, "Category");
        }

        // Kept for compatibility: in the first chunk these are the file header (version, number of categories,
        // unknown) and the category type
        Unknown0 = BitConverter.ToInt32(rawBytes, 0);
        Rank = BitConverter.ToSingle(rawBytes, 4);
        Unknown2 = BitConverter.ToInt32(rawBytes, 8);

        CategoryType = BitConverter.ToInt32(rawBytes, categoryOffset);
        HeaderType = CategoryType;

        var fieldOffset = categoryOffset + 4;

        if (CategoryType == 0 && rawBytes.Length >= fieldOffset + 2)
        {
            var nameLen = BitConverter.ToInt16(rawBytes, fieldOffset);
            var available = (rawBytes.Length - fieldOffset - 2) / 2;

            if (nameLen < 0 || nameLen > available)
            {
                ParseErrors.Add($"Category name length is {nameLen} characters, but only {available} are available");
                nameLen = (short)Math.Max(0, Math.Min((int)nameLen, available));
            }

            Name = Encoding.Unicode.GetString(rawBytes, fieldOffset + 2, nameLen * 2).Split('\0').First();
            fieldOffset += 2 + nameLen * 2;

            if (rawBytes.Length >= fieldOffset + 4)
            {
                NumberOfEntries = BitConverter.ToInt32(rawBytes, fieldOffset);
            }
        }
        else if (CategoryType == 1 && rawBytes.Length >= fieldOffset + 4)
        {
            KnownCategoryId = BitConverter.ToInt32(rawBytes, fieldOffset);
        }
        else if (CategoryType == 2 && rawBytes.Length >= fieldOffset + 4)
        {
            NumberOfEntries = BitConverter.ToInt32(rawBytes, fieldOffset);
        }

        var lnkOffsets = new List<int>();
        var index = 0;

        var footerPos = CustomDestination.ByteSearch(rawBytes, footerBytes, index);
        if (footerPos == -1)
        {
            footerPos = rawBytes.Length;
        }

        while (index < rawBytes.Length)
        {
            var lo = CustomDestination.ByteSearch(rawBytes, lnkHeaderBytes, index);

            if (lo == -1 || lo >= footerPos)
            {
                break;
            }

            lnkOffsets.Add(lo);

            index = lo + 1; //add length so we do not hit on it again
        }

        for (var counter = 0; counter < lnkOffsets.Count; counter++)
        {
            var start = lnkOffsets[counter];
            int end;

            if (counter == lnkOffsets.Count - 1)
            {
                //last one, so we need to use footerpos
                end = footerPos;
            }
            else
            {
                end = lnkOffsets[counter + 1];

                // The next shell object starts with its 16 byte class id; it isn't part of this LNK
                if (end - ClassIdSize > start && IsLnkClassId(rawBytes, end - ClassIdSize))
                {
                    end -= ClassIdSize;
                }
            }

            var bytes = new byte[end - start];

            Buffer.BlockCopy(rawBytes, start, bytes, 0, bytes.Length);

            var name = $"Offset_0x{entryOffset + start:X}.lnk";

            lnkBytes.Add(name, bytes);

            try
            {
                LnkFiles.Add(new LnkFile(bytes, name, codepage));
            }
            catch (Exception ex)
            {
                ParseErrors.Add($"{name}: unable to parse LNK: {ex.Message}");
            }
        }
    }

    public string Name { get; }
    public int Unknown0 { get; }
    public float Rank { get; }
    public int Unknown2 { get; }

    /// <summary>
    ///     Same as <see cref="CategoryType" />.
    /// </summary>
    public int HeaderType { get; }

    /// <summary>
    ///     0 = custom (has a <see cref="Name" />), 1 = known (see <see cref="KnownCategoryId" />), 2 = tasks.
    /// </summary>
    public int CategoryType { get; }

    /// <summary>
    ///     For known categories: 1 = frequent, 2 = recent. Otherwise 0.
    /// </summary>
    public int KnownCategoryId { get; }

    /// <summary>
    ///     Number of entries the category says it has (custom and task categories).
    /// </summary>
    public int NumberOfEntries { get; }

    public List<LnkFile> LnkFiles { get; }

    /// <summary>
    ///     Problems with this category, such as a LNK that couldn't be parsed.
    /// </summary>
    public List<string> ParseErrors { get; } = new List<string>();

    private bool IsLnkClassId(byte[] rawBytes, int offset)
    {
        for (var i = 0; i < ClassIdSize; i++)
        {
            if (rawBytes[offset + i] != lnkHeaderBytes[4 + i])
            {
                return false;
            }
        }

        return true;
    }

    public void DumpAllLnkFiles(string outDir, string appId)
    {
        foreach (var entry in lnkBytes)
        {
            if (entry.Value.Length == 0 || entry.Value[0] != 0x4c)
            {
                //this isn't a lnk file since it doesn't start with 0x4c, so continue
                continue;
            }

            var fName = $"AppId_{appId}_{entry.Key}";
            var outPath = Path.Combine(outDir, fName);

            File.WriteAllBytes(outPath, entry.Value);
        }
    }


    public override string ToString()
    {
        var sb = new StringBuilder();


        sb.AppendLine($"unknown0: {Unknown0}");
        sb.AppendLine($"Rank: {Rank:F4}");
        sb.AppendLine($"unknown2: {Unknown2}");
        sb.AppendLine($"HeaderType: {HeaderType}");
        if (Name.Length > 0)
        {
            sb.AppendLine($"Name: {Name}");
        }

        return sb.ToString();
    }
}
