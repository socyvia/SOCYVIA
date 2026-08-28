using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;

if (args.Length != 6 || args[0] != "deb")
{
    Console.Error.WriteLine("Usage: LinuxPackageTool deb <payload-dir> <icon.png> <desktop-file> <metainfo.xml> <output.deb>");
    return 2;
}

var payload = Path.GetFullPath(args[1]);
var icon = Path.GetFullPath(args[2]);
var desktop = Path.GetFullPath(args[3]);
var metainfo = Path.GetFullPath(args[4]);
var output = Path.GetFullPath(args[5]);
if (!Directory.Exists(payload)) throw new DirectoryNotFoundException(payload);
foreach (var path in new[] { icon, desktop, metainfo })
    if (!File.Exists(path)) throw new FileNotFoundException(path);

var installedBytes = Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories)
    .Sum(path => new FileInfo(path).Length) + new FileInfo(icon).Length + new FileInfo(desktop).Length + new FileInfo(metainfo).Length;
var installedKiB = (installedBytes + 1023) / 1024;
var control = string.Join('\n', new[]
{
    "Package: socyvia",
    "Version: 1.0.0",
    "Section: science",
    "Priority: optional",
    "Architecture: amd64",
    "Maintainer: SOCYVIA <contact@socyvia.com>",
    $"Installed-Size: {installedKiB.ToString(CultureInfo.InvariantCulture)}",
    "Depends: libx11-6, libfontconfig1, libfreetype6, libice6, libsm6, libxext6, libxrender1, libxcb1, libgl1",
    "Homepage: https://socyvia.com",
    "Description: Scientific experimentation and research analysis",
    " SOCYVIA is a bilingual desktop environment for controlled computational",
    " social-science research.",
    ""
});

var executableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                     UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                     UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
var regularMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                  UnixFileMode.GroupRead | UnixFileMode.OtherRead;
var directoryMode = executableMode;
var epoch = DateTimeOffset.FromUnixTimeSeconds(0);

byte[] BuildTarGz(Action<TarWriter> writeEntries)
{
    using var result = new MemoryStream();
    using (var gzip = new GZipStream(result, CompressionLevel.SmallestSize, leaveOpen: true))
    using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        writeEntries(writer);
    return result.ToArray();
}

void AddDirectory(TarWriter writer, string name)
{
    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name.TrimEnd('/') + "/")
    {
        Mode = directoryMode,
        Uid = 0,
        Gid = 0,
        ModificationTime = epoch
    });
}

void AddBytes(TarWriter writer, string name, byte[] bytes, UnixFileMode mode)
{
    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
    {
        DataStream = new MemoryStream(bytes, writable: false),
        Mode = mode,
        Uid = 0,
        Gid = 0,
        ModificationTime = epoch
    });
}

void AddFile(TarWriter writer, string name, string source, UnixFileMode mode)
{
    using var input = File.OpenRead(source);
    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
    {
        DataStream = input,
        Mode = mode,
        Uid = 0,
        Gid = 0,
        ModificationTime = epoch
    });
}

var controlArchive = BuildTarGz(writer => AddBytes(writer, "control", Encoding.UTF8.GetBytes(control), regularMode));
var dataArchive = BuildTarGz(writer =>
{
    foreach (var directory in new[]
    {
        "opt", "opt/socyvia", "usr", "usr/bin", "usr/share", "usr/share/applications",
        "usr/share/icons", "usr/share/icons/hicolor", "usr/share/icons/hicolor/256x256",
        "usr/share/icons/hicolor/256x256/apps", "usr/share/metainfo"
    }) AddDirectory(writer, directory);

    foreach (var path in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var relative = Path.GetRelativePath(payload, path).Replace('\\', '/');
        AddFile(writer, $"opt/socyvia/{relative}", path,
            relative == "SOCYVIA" ? executableMode : regularMode);
    }

    AddBytes(writer, "usr/bin/socyvia",
        Encoding.UTF8.GetBytes("#!/bin/sh\nexec /opt/socyvia/SOCYVIA \"$@\"\n"), executableMode);
    AddFile(writer, "usr/share/applications/socyvia.desktop", desktop, regularMode);
    AddFile(writer, "usr/share/icons/hicolor/256x256/apps/socyvia.png", icon, regularMode);
    AddFile(writer, "usr/share/metainfo/com.socyvia.desktop.metainfo.xml", metainfo, regularMode);
});

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
if (File.Exists(output)) File.Delete(output);
using var archive = File.Create(output);
archive.Write(Encoding.ASCII.GetBytes("!<arch>\n"));
WriteArMember(archive, "debian-binary", Encoding.ASCII.GetBytes("2.0\n"), 33188);
WriteArMember(archive, "control.tar.gz", controlArchive, 33188);
WriteArMember(archive, "data.tar.gz", dataArchive, 33188);

Console.WriteLine(output);
return 0;

static void WriteArMember(Stream output, string name, byte[] data, int mode)
{
    if (name.Length > 15) throw new ArgumentOutOfRangeException(nameof(name));
    var header = string.Concat(
        (name + "/").PadRight(16),
        "0".PadRight(12),
        "0".PadRight(6),
        "0".PadRight(6),
        Convert.ToString(mode, 8)!.PadRight(8),
        data.Length.ToString(CultureInfo.InvariantCulture).PadRight(10),
        "`\n");
    var headerBytes = Encoding.ASCII.GetBytes(header);
    if (headerBytes.Length != 60) throw new InvalidDataException("Invalid ar member header.");
    output.Write(headerBytes);
    output.Write(data);
    if ((data.Length & 1) != 0) output.WriteByte((byte)'\n');
}
