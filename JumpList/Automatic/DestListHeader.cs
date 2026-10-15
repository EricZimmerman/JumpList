using System;
using System.Text;

namespace JumpList.Automatic;

public class DestListHeader
{
    public const int Size = 32;

    /// <exception cref="JumpListParseException">rawBytes is shorter than 32 bytes</exception>
    public DestListHeader(byte[] rawBytes)
    {
        if (rawBytes == null)
        {
            throw new ArgumentNullException(nameof(rawBytes));
        }

        if (rawBytes.Length < Size)
        {
            throw new JumpListParseException(
                $"The DestList header is {Size} bytes, but only {rawBytes.Length} are available", 0, "DestList header");
        }

        Version = BitConverter.ToInt32(rawBytes, 0);
        NumberOfEntries = BitConverter.ToInt32(rawBytes, 4);
        NumberOfPinnedEntries = BitConverter.ToInt32(rawBytes, 8);
        UnknownCounter = BitConverter.ToSingle(rawBytes, 12);
        LastEntryNumber = BitConverter.ToInt32(rawBytes, 16);
        Unknown1 = BitConverter.ToInt32(rawBytes, 20);
        LastRevisionNumber = BitConverter.ToInt32(rawBytes, 24);
        Unknown2 = BitConverter.ToInt32(rawBytes, 28);
    }

    public int Version { get; }
    public int NumberOfEntries { get; }
    public int NumberOfPinnedEntries { get; }
    public float UnknownCounter { get; }
    public int LastEntryNumber { get; }
    public int Unknown1 { get; }
    public int LastRevisionNumber { get; }
    public int Unknown2 { get; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Version: {Version}");
        sb.AppendLine($"NumberOfEntries: {NumberOfEntries}");
        sb.AppendLine($"NumberOfPinnedEntries: {NumberOfPinnedEntries}");
        sb.AppendLine($"LastEntryNumber: {LastEntryNumber}");
        sb.AppendLine($"LastRevisionNumber: {LastRevisionNumber}");
        sb.AppendLine($"Unknown0: {UnknownCounter}");
        sb.AppendLine($"AccessCount: {Unknown1}");
        sb.AppendLine($"Unknown2: {Unknown2}");

        return sb.ToString();
    }
}
