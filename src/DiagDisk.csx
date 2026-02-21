// 临时诊断脚本：读取磁盘 2 的 MBR 和 superblock
var diskPath = @"\\.\PhysicalDrive2";
using var fs = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

// 读 MBR (前 512 字节)
var mbr = new byte[512];
fs.Read(mbr, 0, 512);

Console.WriteLine("=== MBR ===");
Console.WriteLine($"Magic: 0x{mbr[510]:X2}{mbr[511]:X2}");

// 4 个分区表项，每个 16 字节，从偏移 446 开始
for (int i = 0; i < 4; i++)
{
    int offset = 446 + i * 16;
    byte status = mbr[offset];
    byte type = mbr[offset + 4];
    uint startLBA = BitConverter.ToUInt32(mbr, offset + 8);
    uint sizeSectors = BitConverter.ToUInt32(mbr, offset + 12);
    Console.WriteLine($"Partition {i + 1}: status=0x{status:X2}, type=0x{type:X2}, startLBA={startLBA}, sectors={sizeSectors}, size={sizeSectors * 512L / 1024 / 1024} MB");
}

// 读 ext4 superblock (偏移 1024 字节处)
Console.WriteLine("\n=== EXT4 Superblock (offset 1024) ===");
fs.Seek(1024, SeekOrigin.Begin);
var sb = new byte[256];
fs.Read(sb, 0, 256);
uint magic = BitConverter.ToUInt16(sb, 0x38);
Console.WriteLine($"Magic: 0x{magic:X4} (should be 0xEF53 for ext4)");
uint blockCount = BitConverter.ToUInt32(sb, 4);
uint blockSize = (uint)(1024 << (int)BitConverter.ToUInt32(sb, 0x18));
Console.WriteLine($"Block count: {blockCount}, Block size: {blockSize}");
Console.WriteLine($"Total size: {(long)blockCount * blockSize / 1024 / 1024} MB");

// 如果分区 1 有非零 startLBA，也检查那个偏移处的 superblock
uint p1Start = BitConverter.ToUInt32(mbr, 446 + 8);
if (p1Start > 0)
{
    Console.WriteLine($"\n=== Superblock at partition 1 offset ({p1Start} sectors = {p1Start * 512L} bytes) ===");
    fs.Seek(p1Start * 512L + 1024, SeekOrigin.Begin);
    var sb2 = new byte[256];
    fs.Read(sb2, 0, 256);
    uint magic2 = BitConverter.ToUInt16(sb2, 0x38);
    Console.WriteLine($"Magic: 0x{magic2:X4} (should be 0xEF53 for ext4)");
}
