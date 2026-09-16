using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CorOS.Kernel.Core;

internal static class Native
{
    public static class IO
    {
        // NativeAOT natively understands DllImport("*") for internal C symbols
        [DllImport("*", EntryPoint = "_native_io_write_byte")]
        public static extern void Write8(ushort Port, byte Value);

        [DllImport("*", EntryPoint = "_native_io_write_word")]
        public static extern void Write16(ushort Port, ushort Value);

        [DllImport("*", EntryPoint = "_native_io_write_dword")]
        public static extern void Write32(ushort Port, uint Value);

        [DllImport("*", EntryPoint = "_native_io_read_byte")]
        public static extern byte Read8(ushort Port);

        [DllImport("*", EntryPoint = "_native_io_read_word")]
        public static extern ushort Read16(ushort Port);

        [DllImport("*", EntryPoint = "_native_io_read_dword")]
        public static extern uint Read32(ushort Port);
    }

    public static class MMIO
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static void Write8(ulong Address, byte Value)
        {
            *(byte*)Address = Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static void Write16(ulong Address, ushort Value)
        {
            *(ushort*)Address = Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static void Write32(ulong Address, uint Value)
        {
            *(uint*)Address = Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static void Write64(ulong Address, ulong Value)
        {
            *(ulong*)Address = Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static byte Read8(ulong Address)
        {
            return *(byte*)Address;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static ushort Read16(ulong Address)
        {
            return *(ushort*)Address;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static uint Read32(ulong Address)
        {
            return *(uint*)Address;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static ulong Read64(ulong Address)
        {
            return *(ulong*)Address;
        }
    }

    public static class Debug
    {
        [DllImport("*", EntryPoint = "_native_debug_breakpoint")]
        public static extern void Breakpoint();

        [DllImport("*", EntryPoint = "_native_debug_breakpoint_soft")]
        public static extern void BreakpointSoft();
    }
}
