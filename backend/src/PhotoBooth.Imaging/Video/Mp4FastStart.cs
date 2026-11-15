using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace PhotoBooth.Imaging.Video;

/// <summary>
/// Chuyển hộp chỉ mục <c>moov</c> lên trước dữ liệu <c>mdat</c> (như <c>qt-faststart</c> / <c>-movflags +faststart</c>)
/// để điện thoại phát video ngay khi đang tải. Media Foundation luôn ghi moov ở cuối file.
/// Khi dời moov lên trước, mọi offset trong stco/co64 phải cộng thêm kích thước moov.
/// </summary>
public static class Mp4FastStart
{
    private static readonly HashSet<string> Containers = new() { "moov", "trak", "mdia", "minf", "stbl", "edts", "dinf", "udta" };

    private readonly record struct Box(string Type, long Offset, long Size, int HeaderSize);

    /// <summary>Sắp lại file tại chỗ. Trả về true nếu đã sắp lại, false nếu moov đã ở đầu hoặc file không hợp lệ.</summary>
    public static bool Process(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var top = ReadBoxes(data, 0, data.Length);
        if (top == null) return false;

        int moovIdx = top.FindIndex(b => b.Type == "moov");
        int mdatIdx = top.FindIndex(b => b.Type == "mdat");
        if (moovIdx < 0 || mdatIdx < 0 || moovIdx < mdatIdx) return false;

        var moovBox = top[moovIdx];
        if (moovBox.Size > int.MaxValue) return false;
        byte[] moov = data.AsSpan((int)moovBox.Offset, (int)moovBox.Size).ToArray();
        if (!ShiftChunkOffsets(moov, 0, moov.Length, moovBox.Size)) return false;

        string tmp = path + ".faststart.tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        {
            // Giữ các hộp đứng trước mdat (ftyp, uuid...), chèn moov, rồi tới phần còn lại trừ moov cũ
            for (int i = 0; i < top.Count; i++)
            {
                if (i == mdatIdx) fs.Write(moov);
                if (i == moovIdx) continue;
                fs.Write(data, (int)top[i].Offset, (int)top[i].Size);
            }
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
        return true;
    }

    /// <summary>Đọc danh sách hộp liền nhau trong [start, end). Trả null nếu cấu trúc hỏng.</summary>
    private static List<Box>? ReadBoxes(ReadOnlySpan<byte> data, long start, long end)
    {
        var list = new List<Box>();
        long pos = start;
        while (pos + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.Slice((int)pos, 4));
            string type = System.Text.Encoding.ASCII.GetString(data.Slice((int)pos + 4, 4));
            int header = 8;
            if (size == 1)
            {
                if (pos + 16 > end) return null;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.Slice((int)pos + 8, 8));
                header = 16;
            }
            else if (size == 0)
            {
                size = end - pos;
            }
            if (size < header || pos + size > end) return null;
            list.Add(new Box(type, pos, size, header));
            pos += size;
        }
        return pos == end ? list : null;
    }

    /// <summary>Cộng <paramref name="delta"/> vào mọi offset trong stco / co64 nằm trong vùng moov.</summary>
    private static bool ShiftChunkOffsets(byte[] moov, long start, long end, long delta)
    {
        var boxes = ReadBoxes(moov, start, end);
        if (boxes == null) return false;
        foreach (var box in boxes)
        {
            long body = box.Offset + box.HeaderSize;
            if (box.Type == "moov" || Containers.Contains(box.Type))
            {
                if (box.Type == "udta") continue;
                if (!ShiftChunkOffsets(moov, body, box.Offset + box.Size, delta)) return false;
            }
            else if (box.Type == "stco")
            {
                // version/flags (4) + số lượng (4) + các offset 32 bit
                uint count = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan((int)body + 4, 4));
                for (long i = 0; i < count; i++)
                {
                    var span = moov.AsSpan((int)(body + 8 + i * 4), 4);
                    ulong value = BinaryPrimitives.ReadUInt32BigEndian(span) + (ulong)delta;
                    if (value > uint.MaxValue) return false; // cần co64; video phiên chụp không lớn tới mức này
                    BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value);
                }
            }
            else if (box.Type == "co64")
            {
                uint count = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan((int)body + 4, 4));
                for (long i = 0; i < count; i++)
                {
                    var span = moov.AsSpan((int)(body + 8 + i * 8), 8);
                    BinaryPrimitives.WriteUInt64BigEndian(span, BinaryPrimitives.ReadUInt64BigEndian(span) + (ulong)delta);
                }
            }
        }
        return true;
    }
}
