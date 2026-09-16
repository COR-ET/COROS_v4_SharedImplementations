using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Diagnostics;
using CorOS.Kernel.Core;

namespace CorOS.Plugs;

public static class RtcEarlyInterceptor
{
    private const ushort CMOSAddress = 0x70;
    private const ushort CMOSData = 0x71;


    public static bool IsUefiBoot { get; private set; } = false;

    [ModuleInitializer]
    public static unsafe void InterceptEarlyRtc()
    {
        try
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name ?? string.Empty;

                if (!name.Contains("Limine", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("HAL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (Type type in asm.GetTypes())
                {
                    FieldInfo[] fields = type.GetFields(
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);

                    for (int i = 0; i < fields.Length; i++)
                    {
                        FieldInfo field = fields[i];

                        if (field.FieldType.Name.Contains("EfiSystemTable", StringComparison.OrdinalIgnoreCase))
                        {
                            object? val = field.GetValue(null);
                            if (HasActiveEfiResponse(val))
                            {
                                IsUefiBoot = true;
                            }
                            field.SetValue(null, null);
                        }
                    }

                    PropertyInfo[] properties = type.GetProperties(
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);

                    for (int i = 0; i < properties.Length; i++)
                    {
                        PropertyInfo property = properties[i];

                        if (property.PropertyType.Name.Contains("EfiSystemTable", StringComparison.OrdinalIgnoreCase))
                        {
                            object? val = property.GetValue(null);
                            if (HasActiveEfiResponse(val))
                            {
                                IsUefiBoot = true;
                            }
                            if (property.CanWrite)
                            {
                                property.SetValue(null, null);
                            }
                        }
                    }
                }
            }

            Native.IO.Write8(CMOSAddress, 0x0A);
            byte statusA = Native.IO.Read8(CMOSData);

            if (statusA != 0xFF)
            {
                Log.WriteString("[RTCFix] Early CMOS hardware verified online.\n");
            }

            if (IsUefiBoot)
            {
                Log.WriteString("[RTCFix] Firmware: Native UEFI boot confirmed.\n");
            }
            else
            {
                Log.WriteString("[RTCFix] Firmware: Legacy BIOS boot confirmed.\n");
            }
        }
        catch
        {
        }
    }

    private static unsafe bool HasActiveEfiResponse(object? obj)
    {
        if (obj == null) return false;

        if (obj is IntPtr ptr) return ptr != IntPtr.Zero;
        if (obj is UIntPtr uptr) return uptr != UIntPtr.Zero;

        if (obj.GetType().FullName == "System.Reflection.Pointer")
        {
            void* p = System.Reflection.Pointer.Unbox(obj);
            return p != null;
        }

        // If it is the LimineEfiSystemTableRequest struct, inspect its Response pointer
        FieldInfo? respField = obj.GetType().GetField("Response", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (respField != null)
        {
            object? respVal = respField.GetValue(obj);
            return HasActiveEfiResponse(respVal);
        }

        return false;
    }
}

/// <summary>
/// Replaces Cosmos X64 RTC implementation with a direct CMOS RTC path. Seems to be working on realward hardware and QEMU. However it might affect drivers detection (like Keyboard etc), so you might need to handle the rest by yourself :>
/// </summary>
[Plug(TargetName = "Cosmos.Kernel.HAL.X64.Devices.Clock.RTC")]
public static class RTCPlug
{
    private const ushort CMOSAddress = 0x70;
    private const ushort CMOSData = 0x71;

    private const byte RegSeconds = 0x00;
    private const byte RegMinutes = 0x02;
    private const byte RegHours = 0x04;
    private const byte RegDay = 0x07;
    private const byte RegMonth = 0x08;
    private const byte RegYear = 0x09;
    private const byte RegCentury = 0x32;
    private const byte RegStatusA = 0x0A;
    private const byte RegStatusB = 0x0B;

    [PlugMember]
    public static void Initialize(object aThis)
    {
        Log.WriteString("[RTCPlug] Direct CMOS RTC initialization.\n");

        try
        {
            DateTime now = ReadTimeFromCMOS();
            Log.WriteString("[RTCPlug] RTC time: ");
            LogTimeRaw(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);
        }
        catch (Exception ex)
        {
            Log.WriteString("[RTCPlug] Initialization warning: ");
            Log.WriteString(ex.Message);
            Log.WriteString("\n");
        }
    }

    [PlugMember]
    public static DateTime ReadTime(object aThis)
    {
        return ReadTimeFromCMOS();
    }

    [PlugMember]
    public static ulong GetCurrentTicks(object aThis)
    {
        return (ulong)ReadTimeFromCMOS().Ticks;
    }

    private static DateTime ReadTimeFromCMOS()
    {
        int timeout = 100000;
        while (IsUpdating() && --timeout > 0) { }

        byte statusB = ReadRegister(RegStatusB);
        bool isBinary = (statusB & 0x04) != 0;
        bool is24Hour = (statusB & 0x02) != 0;

        byte rawSecond = ReadRegister(RegSeconds);
        byte rawMinute = ReadRegister(RegMinutes);
        byte rawHour = ReadRegister(RegHours);
        byte rawDay = ReadRegister(RegDay);
        byte rawMonth = ReadRegister(RegMonth);
        byte rawYear = ReadRegister(RegYear);
        byte rawCentury = ReadRegister(RegCentury);

        bool isPm = (rawHour & 0x80) != 0;

        byte second = rawSecond;
        byte minute = rawMinute;
        byte hour = (byte)(rawHour & 0x7F);
        byte day = rawDay;
        byte month = rawMonth;
        byte year = rawYear;
        byte century = rawCentury;

        if (!isBinary)
        {
            second = BcdToBinary(second);
            minute = BcdToBinary(minute);
            hour = BcdToBinary(hour);
            day = BcdToBinary(day);
            month = BcdToBinary(month);
            year = BcdToBinary(year);
            century = BcdToBinary(century);
        }

        if (!is24Hour)
        {
            if (hour == 12)
            {
                hour = isPm ? (byte)12 : (byte)0;
            }
            else if (isPm)
            {
                hour = (byte)(hour + 12);
            }
        }

        int fullYear;
        if (century >= 20 && century <= 21)
        {
            fullYear = (century * 100) + year;
        }
        else
        {
            fullYear = year < 80 ? (2000 + year) : (1900 + year);
        }

        if (month < 1 || month > 12) month = 1;
        if (day < 1 || day > 31) day = 1;
        if (hour > 23) hour = 0;
        if (minute > 59) minute = 0;
        if (second > 59) second = 0;

        return new DateTime(fullYear, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    private static byte ReadRegister(byte register)
    {
        Native.IO.Write8(CMOSAddress, (byte)(0x80 | register));
        return Native.IO.Read8(CMOSData);
    }

    private static bool IsUpdating()
    {
        Native.IO.Write8(CMOSAddress, RegStatusA);
        return (Native.IO.Read8(CMOSData) & 0x80) != 0;
    }

    private static byte BcdToBinary(byte value)
    {
        return (byte)(((value >> 4) * 10) + (value & 0x0F));
    }

    private static void LogTimeRaw(int year, int month, int day, int hour, int minute, int second)
    {
        Log.WriteNumber((ulong)year);
        Log.WriteString("-");
        if (month < 10) Log.WriteString("0");
        Log.WriteNumber((ulong)month);
        Log.WriteString("-");
        if (day < 10) Log.WriteString("0");
        Log.WriteNumber((ulong)day);
        Log.WriteString(" ");
        if (hour < 10) Log.WriteString("0");
        Log.WriteNumber((ulong)hour);
        Log.WriteString(":");
        if (minute < 10) Log.WriteString("0");
        Log.WriteNumber((ulong)minute);
        Log.WriteString(":");
        if (second < 10) Log.WriteString("0");
        Log.WriteNumber((ulong)second);
        Log.WriteString(" UTC\n");
    }
}