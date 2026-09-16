using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;
using CorOS.Kernel.Storage;
using CorOS.Plugs;

namespace CorOS.Boot;

public enum FirmwareType
{
    Unknown,
    BIOS,
    UEFI
}

public sealed class BootDeviceInfo
{
    public bool HasDevice { get; init; }
    public string Name { get; init; } = "None";
    public ulong BlockSize { get; init; }
    public ulong BlockCount { get; init; }
    public ulong CapacityMiB { get; init; }
    public string PartitionTable { get; init; } = "None";
    public int PartitionCount { get; init; }
    public string BootPartitionFs { get; init; } = "Unknown";
    public string MountPoint { get; init; } = "Unmounted";
}

public sealed class FramebufferInfo
{
    public bool IsAvailable { get; init; }
    public string DriverName { get; init; } = "None";
    public int Width { get; init; }
    public int Height { get; init; }
    public int RefreshRate { get; init; }
    public int ColorDepthBits { get; init; }
    public int ConsoleCols { get; init; }
    public int ConsoleRows { get; init; }
    public bool Supports3D { get; init; }
}

public sealed class MemoryTopologyInfo
{
    public ulong PhysicalRamBytes { get; init; }
    public ulong PhysicalRamMiB { get; init; }
    public ulong TotalPages { get; init; }
    public ulong FreePages { get; init; }
    public ulong UsedPages { get; init; }
    public ulong PageSizeBytes { get; init; }
    public long ManagedHeapBytes { get; init; }
    public long TotalCommittedBytes { get; init; }
}

public sealed class CommandLineInfo
{
    public string Raw { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public bool HasSwitch(string switchName)
    {
        for (int i = 0; i < Arguments.Count; i++)
        {
            if (string.Equals(Arguments[i], switchName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Probes and caches low-level platform, firmware, bootloader, memory, and display data.
/// </summary>
public sealed partial class BootInfo
{
    private static BootInfo? s_current;
    public static BootInfo Current => s_current ??= Probe();

    public FirmwareType Firmware { get; private init; }
    public string BootloaderName { get; private init; } = "CORNEL";
    public ulong BootTimestampUtc { get; private init; }
    public BootDeviceInfo BootDevice { get; private init; } = new();
    public FramebufferInfo Framebuffer { get; private init; } = new();
    public MemoryTopologyInfo Memory { get; private init; } = new();
    public CommandLineInfo CommandLine { get; private init; } = new();

    private static partial class Native
    {
        [LibraryImport("*", EntryPoint = "cor_get_limine_firmware_type")]
        private static partial ulong GetLimineFirmwareType();

        public static ulong FirmwareType => GetLimineFirmwareType();
    }

    public static BootInfo Probe()
    {
        FirmwareType fw = DetectFirmware();
        ulong bootTime = QueryBootTime();
        CommandLineInfo cmd = ProbeCommandLine();
        FramebufferInfo fb = ProbeFramebuffer();
        MemoryTopologyInfo mem = ProbeMemory();
        BootDeviceInfo dev = ProbeBootDevice();

        return new BootInfo
        {
            Firmware = fw,
            BootloaderName = "CORNEL (v4)",
            BootTimestampUtc = bootTime,
            CommandLine = cmd,
            Framebuffer = fb,
            Memory = mem,
            BootDevice = dev
        };
    }

    private static FirmwareType DetectFirmware()
    {
#if ARCH_ARM64
        return FirmwareType.UEFI;
#else
        try
        {
            ulong firmwareType = Native.FirmwareType;

            return firmwareType switch
            {
                0 => FirmwareType.BIOS,
                1 => FirmwareType.UEFI,
                2 => FirmwareType.UEFI,
                _ => FirmwareType.Unknown
            };
        }
        catch
        {
            return FirmwareType.Unknown;
        }
#endif
    }

    private static ulong QueryBootTime()
    {
        try
        {
            LimineBootTimeRequest req = new();
            unsafe
            {
                if (req.Response != null)
                {
                    return (ulong)req.Response->BootTime;
                }
            }
        }
        catch
        {
        }
        return 0;
    }

    private static CommandLineInfo ProbeCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        string raw = string.Join(" ", args);

        return new CommandLineInfo
        {
            Raw = raw,
            Arguments = args
        };
    }

    private static FramebufferInfo ProbeFramebuffer()
    {
        if (!KernelConsole.IsInitialized)
        {
            return new FramebufferInfo { IsAvailable = false };
        }

        Canvas canvas = KernelConsole.Default.Canvas;
        bool is3d = canvas is Canvas3D;

        return new FramebufferInfo
        {
            IsAvailable = true,
            DriverName = canvas.Name,
            Width = canvas.Width,
            Height = canvas.Height,
            RefreshRate = canvas.RefreshRate,
            ColorDepthBits = (int)canvas.Mode.ColorDepth,
            ConsoleCols = KernelConsole.Default.Cols,
            ConsoleRows = KernelConsole.Default.Rows,
            Supports3D = is3d
        };
    }

    public static MemoryTopologyInfo ProbeMemory()
    {
        ulong totalPages = MemoryInfo.TotalPages;
        ulong freePages = MemoryInfo.FreePages;
        ulong pageSize = MemoryInfo.PageSizeBytes;
        ulong usedPages = totalPages >= freePages ? totalPages - freePages : 0;
        ulong ramBytes = MemoryInfo.RamSizeBytes;

        if (ramBytes == 0 && totalPages > 0)
        {
            ramBytes = totalPages * pageSize;
        }

        GCMemoryInfo gcInfo = GC.GetGCMemoryInfo();

        return new MemoryTopologyInfo
        {
            PhysicalRamBytes = ramBytes,
            PhysicalRamMiB = ramBytes / (1024 * 1024),
            TotalPages = totalPages,
            FreePages = freePages,
            UsedPages = usedPages,
            PageSizeBytes = pageSize,
            ManagedHeapBytes = gcInfo.HeapSizeBytes,
            TotalCommittedBytes = gcInfo.TotalCommittedBytes
        };
    }

    private static BootDeviceInfo ProbeBootDevice()
    {
        if (!CorStorageManager.IsInitialized || CorStorageManager.Disks.Count == 0)
        {
            return new BootDeviceInfo { HasDevice = false };
        }

        CorDisk primary = CorStorageManager.PrimaryDisk!;
        string fs = "None";
        string mnt = "Unmounted";

        if (primary.Partitions.Count > 0)
        {
            fs = primary.Partitions[0].DetectedFilesystem;
        }

        if (VfsManager.Mounts.Count > 0)
        {
            mnt = VfsManager.Mounts[0].MountPoint;
        }

        return new BootDeviceInfo
        {
            HasDevice = true,
            Name = primary.Name,
            BlockSize = primary.Device.BlockSize,
            BlockCount = primary.Device.BlockCount,
            CapacityMiB = primary.Device.CapacityMiB,
            PartitionTable = primary.PartitionScheme.ToString(),
            PartitionCount = primary.Partitions.Count,
            BootPartitionFs = fs,
            MountPoint = mnt
        };
    }
}
