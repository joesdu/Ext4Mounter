namespace Ext4Mounter;

/// <summary>
/// 包装物理磁盘的 FileStream，提供 Length/Position 支持，
/// 并处理 Windows 物理磁盘的扇区对齐要求（所有读取必须 512 字节对齐）
/// </summary>
public sealed class PhysicalDiskStream(string diskPath, long diskSize) : Stream
{
    private const int SectorSize = 512;

    private readonly FileStream _inner = new(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, FileOptions.RandomAccess | FileOptions.SequentialScan);

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; } = diskSize;

    public override long Position { get; set; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0)
        {
            return 0;
        }
        if (Position >= Length)
        {
            return 0;
        }

        // 限制不超过磁盘末尾
        if (Position + count > Length)
        {
            count = (int)(Length - Position);
        }

        // 计算对齐的起始扇区和读取范围
        var alignedStart = (Position / SectorSize) * SectorSize;
        var preSkip = (int)(Position - alignedStart);
        var alignedEnd = (((Position + count + SectorSize) - 1) / SectorSize) * SectorSize;
        var alignedCount = (int)(alignedEnd - alignedStart);

        // 如果已经对齐，直接读
        if (preSkip == 0 && count % SectorSize == 0)
        {
            _inner.Seek(alignedStart, SeekOrigin.Begin);
            var totalRead = 0;
            while (totalRead < count)
            {
                var bytesRead = _inner.Read(buffer, offset + totalRead, count - totalRead);
                if (bytesRead == 0)
                {
                    break;
                }
                totalRead += bytesRead;
            }
            Position += totalRead;
            return totalRead;
        }

        // 非对齐：读到临时缓冲区，再拷贝需要的部分
        var alignedBuffer = new byte[alignedCount];
        _inner.Seek(alignedStart, SeekOrigin.Begin);
        var totalAlignedRead = 0;
        while (totalAlignedRead < alignedCount)
        {
            var bytesRead = _inner.Read(alignedBuffer, totalAlignedRead, alignedCount - totalAlignedRead);
            if (bytesRead == 0)
            {
                break;
            }
            totalAlignedRead += bytesRead;
        }

        // 从对齐缓冲区中拷贝实际需要的数据
        var available = totalAlignedRead - preSkip;
        if (available <= 0)
        {
            return 0;
        }
        var toCopy = Math.Min(count, available);
        Array.Copy(alignedBuffer, preSkip, buffer, offset, toCopy);
        Position += toCopy;
        return toCopy;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin   => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End     => Length + offset,
            _                  => throw new ArgumentException("Invalid SeekOrigin", nameof(origin))
        };
        return Position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}