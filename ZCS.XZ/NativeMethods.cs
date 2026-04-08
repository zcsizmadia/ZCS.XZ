using System;
using System.Runtime.InteropServices;

namespace ZCS.XZ;

public static class NativeMethods
{
    private const string LibName = "liblzma";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lzma_easy_encoder(ref LzmaStream stream, uint preset, int check);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lzma_stream_decoder(ref LzmaStream stream, ulong memlimit, uint flags);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lzma_code(ref LzmaStream stream, int action);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lzma_end(ref LzmaStream stream);

    [DllImport("liblzma", CallingConvention = CallingConvention.Cdecl)]
    public static extern int lzma_stream_encoder_mt(ref LzmaStream stream, ref LzmaMt options);

    [DllImport("liblzma", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint lzma_cputhreads(); // Optional: helps detect hardware thread count

    [StructLayout(LayoutKind.Sequential)]
    public struct LzmaStream
    {
        public IntPtr next_in;
        public UIntPtr avail_in;
        public ulong total_in;
        public IntPtr next_out;
        public UIntPtr avail_out;
        public ulong total_out;
        // ... (Remaining reserved fields must be padded to match native struct size)
        public IntPtr internal_ptr; 
        private unsafe fixed byte reserved[72]; 
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LzmaMt
    {
        public uint flags;        // Set to 0 (no flags currently defined for common use)
        public uint threads;      // Number of worker threads (e.g., Environment.ProcessorCount)
        public ulong block_size;  // 0 = let liblzma decide (recommended)
        public uint timeout;      // 0 = no timeout
        public uint preset;       // Compression level (0-9)
        public int check;         // Integrity check (e.g., 1 for CRC32, 2 for CRC64)
        public IntPtr filters;    // Filters (set to IntPtr.Zero if using preset)
        
        // Padding for reserved fields (6 pointers + 1 uint32_t)
        private UIntPtr reserved_ptr1; private UIntPtr reserved_ptr2;
        private UIntPtr reserved_ptr3; private UIntPtr reserved_ptr4;
        private UIntPtr reserved_ptr5; private UIntPtr reserved_ptr6;
        private uint reserved_int1; private uint reserved_int2;
    }
}