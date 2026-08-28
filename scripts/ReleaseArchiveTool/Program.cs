using System.Formats.Tar;
using System.IO.Compression;

if (args.Length != 5 || args[0] is not ("zip" or "tar"))
{
    Console.Error.WriteLine("Usage: ReleaseArchiveTool <zip|tar> <source> <archive> <root-name|-> <executable-relative-path>");
    return 2;
}

var operation = args[0];
var source = Path.GetFullPath(args[1]);
var archive = Path.GetFullPath(args[2]);
var rootName = args[3] == "-" ? string.Empty : args[3].Trim('/', '\\');
var executableRelative = args[4].Replace('\\', '/').TrimStart('/');
if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
if (File.Exists(archive)) File.Delete(archive);

string EntryName(string path)
{
    var relative = Path.GetRelativePath(source, path).Replace('\\', '/');
    return string.IsNullOrEmpty(rootName) ? relative : $"{rootName}/{relative}";
}

bool IsExecutable(string path) =>
    string.Equals(Path.GetRelativePath(source, path).Replace('\\', '/'), executableRelative, StringComparison.Ordinal);

if (operation == "zip")
{
    using var file = File.Create(archive);
    using var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);
    foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var entry = zip.CreateEntry(EntryName(path), CompressionLevel.Optimal);
        var unixMode = IsExecutable(path) ? 0x81ED : 0x81A4; // regular 0755 / regular 0644
        entry.ExternalAttributes = unchecked(unixMode << 16);
        using var input = File.OpenRead(path);
        using var output = entry.Open();
        input.CopyTo(output);
    }
}
else
{
    using var file = File.Create(archive);
    using var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: false);
    using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
    foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var executableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                             UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                             UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        var regularMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                          UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        var entry = new PaxTarEntry(TarEntryType.RegularFile, EntryName(path))
        {
            DataStream = File.OpenRead(path),
            Mode = IsExecutable(path) ? executableMode : regularMode,
            ModificationTime = DateTimeOffset.UtcNow
        };
        writer.WriteEntry(entry);
        entry.DataStream.Dispose();
    }
}

Console.WriteLine(archive);
return 0;
