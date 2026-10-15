using System;

namespace JumpList;

/// <summary>
///     Thrown when data is not a usable jump list (empty, not a compound file, missing footer). Problems with single
///     entries, such as a damaged LNK or DestList entry, are reported in ParseErrors instead.
/// </summary>
public class JumpListParseException : Exception
{
    public JumpListParseException(string message, int offset = -1, string field = null, Exception inner = null)
        : base(message, inner)
    {
        Offset = offset;
        Field = field;
    }

    /// <summary>
    ///     Byte offset of the failing read, or -1 when unknown.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    ///     Name of the field or structure being read when parsing failed, if known.
    /// </summary>
    public string Field { get; }
}
