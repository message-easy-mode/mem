using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Mem.Migrate.Core.Security;

public static class UnixFileTypeSafety
{
    private const int AtFileDescriptorCurrentWorkingDirectory = -100;
    private const int AtSymbolicLinkNoFollow = 0x100;
    private const uint StatxTypeAndLinkCount = 0x00000005;
    private const ushort FileTypeMask = 0xF000;
    private const ushort RegularFileType = 0x8000;
    private const ushort DirectoryFileType = 0x4000;

    public static void EnsureRegularFile(string path, bool rejectHardLinks = true)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var status = Read(path);

        if ((status.Mode & FileTypeMask) != RegularFileType)
        {
            throw new InvalidDataException(
                $"Filesystem entry is not a regular file: {Path.GetFullPath(path)}");
        }

        if (rejectHardLinks && status.LinkCount > 1)
        {
            throw new InvalidDataException(
                $"Filesystem entry has multiple hard links and is not accepted for migration capture: {Path.GetFullPath(path)}");
        }
    }

    public static void EnsureDirectory(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var status = Read(path);

        if ((status.Mode & FileTypeMask) != DirectoryFileType)
        {
            throw new InvalidDataException(
                $"Filesystem entry is not a directory: {Path.GetFullPath(path)}");
        }
    }

    private static FileStatus Read(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var result = Statx(
            AtFileDescriptorCurrentWorkingDirectory,
            fullPath,
            AtSymbolicLinkNoFollow,
            StatxTypeAndLinkCount,
            out var status);

        if (result != 0)
        {
            throw new IOException(
                $"Could not inspect filesystem entry type: {fullPath}",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        return new FileStatus(status.Mode, status.LinkCount);
    }

#pragma warning disable SYSLIB1054
    [DllImport(
        "libc",
        EntryPoint = "statx",
        SetLastError = true,
        CharSet = CharSet.Ansi)]
    private static extern int Statx(
        int directoryFileDescriptor,
        string path,
        int flags,
        uint mask,
        out NativeStatx status);
#pragma warning restore SYSLIB1054

    private sealed record FileStatus(ushort Mode, uint LinkCount);

#pragma warning disable CS0649, CA1815
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStatx
    {
        public uint Mask;
        public uint BlockSize;
        public ulong Attributes;
        public uint LinkCount;
        public uint UserId;
        public uint GroupId;
        public ushort Mode;
        public ushort Spare0;
        public ulong Inode;
        public ulong Size;
        public ulong Blocks;
        public ulong AttributesMask;
        public NativeStatxTimestamp AccessTime;
        public NativeStatxTimestamp BirthTime;
        public NativeStatxTimestamp ChangeTime;
        public NativeStatxTimestamp ModificationTime;
        public uint DeviceMajor;
        public uint DeviceMinor;
        public uint FileSystemDeviceMajor;
        public uint FileSystemDeviceMinor;
        public ulong MountId;
        public uint DirectIoMemoryAlignment;
        public uint DirectIoOffsetAlignment;
        public ulong Spare00;
        public ulong Spare01;
        public ulong Spare02;
        public ulong Spare03;
        public ulong Spare04;
        public ulong Spare05;
        public ulong Spare06;
        public ulong Spare07;
        public ulong Spare08;
        public ulong Spare09;
        public ulong Spare10;
        public ulong Spare11;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStatxTimestamp
    {
        public long Seconds;
        public uint Nanoseconds;
        public int Reserved;
    }
#pragma warning restore CS0649, CA1815
}
