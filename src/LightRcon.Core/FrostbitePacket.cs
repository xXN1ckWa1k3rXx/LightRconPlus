using System.Buffers.Binary;
using System.Text;

namespace LightRcon.Core;

public sealed record FrostbitePacket(uint Sequence, bool OriginServer, bool IsResponse, string[] Words)
{
    public const int MaxSize = 16_384;
    public byte[] Encode()
    {
        var encoded = Words.Select(Encoding.UTF8.GetBytes).ToArray();
        if (Words.Any(x => x.Contains('\0'))) throw new ArgumentException("Protocol words cannot contain NUL.");
        var size = 12 + encoded.Sum(x => 5 + x.Length);
        if (size > MaxSize) throw new InvalidDataException("Frostbite packet exceeds 16384 bytes.");
        var bytes = new byte[size];
        var flags = (Sequence & 0x3fff_ffff) | (OriginServer ? 0x8000_0000u : 0) | (IsResponse ? 0x4000_0000u : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, flags);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), size);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), Words.Length);
        var offset = 12;
        foreach (var word in encoded)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), word.Length);
            word.CopyTo(bytes, offset + 4);
            offset += word.Length + 5;
        }
        return bytes;
    }
    public static async Task<FrostbitePacket> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[12];
        await stream.ReadExactlyAsync(header, token);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        var count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        if (size is < 12 or > MaxSize || count < 0 || count > (size - 12) / 5)
            throw new InvalidDataException("Malformed Frostbite packet header.");
        var body = new byte[size - 12];
        await stream.ReadExactlyAsync(body, token);
        var offset = 0;
        var words = new string[count];
        for (var i = 0; i < count; i++)
        {
            if (offset + 4 > body.Length) throw new InvalidDataException("Truncated word length.");
            var length = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(offset));
            if (length < 0 || length > body.Length - offset - 5 || body[offset + 4 + length] != 0)
                throw new InvalidDataException("Malformed Frostbite word.");
            words[i] = new UTF8Encoding(false, true).GetString(body, offset + 4, length);
            offset += length + 5;
        }
        if (offset != body.Length) throw new InvalidDataException("Unexpected packet trailing data.");
        return new(flags & 0x3fff_ffff, (flags & 0x8000_0000) != 0, (flags & 0x4000_0000) != 0, words);
    }
}
