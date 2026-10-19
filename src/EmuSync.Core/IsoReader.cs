using System.Buffers.Binary;
using System.Text;

namespace EmuSync.Core;

public static class IsoReader
{
    // shortcut: uncompressed ISO only, add a block decoder when CSO/CHD support is requested.
    const int Sector = 2048;
    record Entry(uint Sector, uint Size, bool Directory);

    public static async Task<Game> Read(Stream stream, string filename)
    {
        if (!stream.CanSeek) throw new IOException("The selected storage cannot seek inside ISO files. Copy the ISO to local storage first.");
        var reader = new Reader(stream);
        Entry? root = null;
        for (var sector = 16; sector < 48; sector++)
        {
            var block = await reader.Bytes((long)sector * Sector, Sector);
            if (Encoding.ASCII.GetString(block, 1, 5) != "CD001" || block[6] != 1)
                throw new InvalidDataException("Not a supported PSP ISO. Select an uncompressed .iso file.");
            if (block[0] == 255) break;
            if (block[0] != 1) continue;
            if (BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(128)) != Sector)
                throw new InvalidDataException("Unsupported ISO sector size.");
            root = ParseEntry(block.AsSpan(156));
            break;
        }
        if (root is null) throw new InvalidDataException("The ISO has no readable directory.");
        var gameDirectory = await reader.Find(root, "PSP_GAME");
        var sfo = await reader.File(gameDirectory, "PARAM.SFO", 1024 * 1024);
        var fields = ReadSfo(sfo ?? throw new InvalidDataException("This ISO has no PSP game information."));
        var id = SafePath.GameId(fields.GetValueOrDefault("DISC_ID", ""));
        var title = fields.GetValueOrDefault("TITLE", Path.GetFileNameWithoutExtension(filename));
        if (string.IsNullOrWhiteSpace(title)) title = id;
        var icon = await reader.File(gameDirectory, "ICON0.PNG", 2 * 1024 * 1024);
        var background = await reader.File(gameDirectory, "PIC1.PNG", 4 * 1024 * 1024);
        return new Game(id, title, filename, stream.Length, Image(icon), Image(background));
    }

    static string? Image(byte[]? bytes)
    {
        if (bytes is null) return null;
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return null;
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20));
        if (width is 0 or > 4096 || height is 0 or > 4096) return null;
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }

    public static Dictionary<string,string> ReadSfo(byte[] data)
    {
        if (data.Length < 20 || !data.AsSpan(0,4).SequenceEqual(new byte[] {0,80,83,70}))
            throw new InvalidDataException("Invalid PSP game information.");
        uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset,4));
        var keys = U32(8); var values = U32(12); var count = U32(16);
        if (count > 4096 || 20L + count * 16L > keys || keys > values || values > data.Length)
            throw new InvalidDataException("Invalid PSP metadata offsets.");
        var result = new Dictionary<string,string>();
        for (var i = 0; i < count; i++)
        {
            var pos = 20 + i * 16;
            var keyStart = keys + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos,2));
            var format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos+2,2));
            var length = U32(pos+4); var valueStart = (long)values + U32(pos+12);
            if (keyStart >= values || length > 65536 || valueStart + length > data.Length)
                throw new InvalidDataException("Truncated PSP game information.");
            var keyEnd = Array.IndexOf(data, (byte)0, (int)keyStart, (int)(values-keyStart));
            if (keyEnd < 0) throw new InvalidDataException("Invalid PSP metadata key.");
            if (format != 0x0204) continue;
            var key = Encoding.ASCII.GetString(data, (int)keyStart, keyEnd-(int)keyStart);
            var value = Encoding.UTF8.GetString(data, (int)valueStart, (int)length).TrimEnd('\0');
            result[key] = value;
        }
        return result;
    }

    static Entry ParseEntry(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 34 || bytes[0] < 34 || bytes[0] > bytes.Length)
            throw new InvalidDataException("Invalid ISO directory record.");
        var sector = BinaryPrimitives.ReadUInt32LittleEndian(bytes[2..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[10..]);
        if (sector != BinaryPrimitives.ReadUInt32BigEndian(bytes[6..]) || size != BinaryPrimitives.ReadUInt32BigEndian(bytes[14..]))
            throw new InvalidDataException("Damaged ISO directory record.");
        return new Entry(sector,size,(bytes[25] & 2) != 0);
    }

    sealed class Reader(Stream stream)
    {
        public async Task<byte[]> Bytes(long start,int size)
        {
            if (start < 0 || size < 0 || start > stream.Length-size) throw new InvalidDataException("The ISO is incomplete.");
            stream.Position = start;
            var data = new byte[size];
            await stream.ReadExactlyAsync(data);
            return data;
        }
        public async Task<Entry> Find(Entry directory,string name)
        {
            if (!directory.Directory || directory.Size > 8 * 1024 * 1024) throw new InvalidDataException("Invalid ISO directory.");
            var bytes = await Bytes((long)directory.Sector*Sector,(int)directory.Size);
            for (var pos=0; pos<bytes.Length;)
            {
                var length = bytes[pos];
                if (length == 0) { pos = (pos/Sector+1)*Sector; continue; }
                if (length < 34 || pos+length > bytes.Length || 33+bytes[pos+32] > length)
                    throw new InvalidDataException("Invalid ISO filename record.");
                var itemName = Encoding.ASCII.GetString(bytes,pos+33,bytes[pos+32]).Split(';')[0];
                if (itemName.Equals(name,StringComparison.OrdinalIgnoreCase)) return ParseEntry(bytes.AsSpan(pos,length));
                pos += length;
            }
            throw new FileNotFoundException($"{name} is missing from the ISO.");
        }
        public async Task<byte[]?> File(Entry directory,string name,int limit)
        {
            Entry file;
            try { file = await Find(directory,name); } catch (FileNotFoundException) { return null; }
            if (file.Directory || file.Size > limit) return null;
            return await Bytes((long)file.Sector*Sector,(int)file.Size);
        }
    }
}
