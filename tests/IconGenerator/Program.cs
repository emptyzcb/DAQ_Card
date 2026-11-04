using System.Drawing.Imaging;
using IndustrialFieldDataAcquisitionTerminal;

var output = Path.GetFullPath(args.Length > 0 ? args[0] : "daq-workbench.ico");
if (!string.Equals(Path.GetExtension(output), ".ico", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("图标生成器只允许写入 .ico 文件。", nameof(args));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var sizes = new[] { 16, 32, 48, 256 };
var images = sizes.Select(size =>
{
    using var bitmap = AppBrand.CreateBitmap(size);
    using var buffer = new MemoryStream();
    bitmap.Save(buffer, ImageFormat.Png);
    return buffer.ToArray();
}).ToArray();

using var stream = File.Create(output);
using var writer = new BinaryWriter(stream);
writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)sizes.Length);
var offset = 6 + sizes.Length * 16;
for (var index = 0; index < sizes.Length; index++)
{
    writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
    writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(images[index].Length);
    writer.Write(offset);
    offset += images[index].Length;
}
foreach (var image in images) writer.Write(image);
Console.WriteLine(output);
