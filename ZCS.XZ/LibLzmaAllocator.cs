using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZCS.XZ;

/// <summary>
/// A custom <c>lzma_allocator</c> that routes liblzma allocations through
/// <see cref="NativeMemory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Several liblzma functions hand back memory the caller is expected to release. With the
/// default allocator that means calling the C <c>free()</c> from the same heap liblzma
/// allocated on, which managed code cannot safely do: on Windows the native library and the
/// .NET runtime can be linked against different C runtimes, so freeing across that boundary
/// corrupts the heap.
/// </para>
/// <para>
/// Supplying this allocator makes both halves of the transaction ours, so anything liblzma
/// allocates can be released with <see cref="NativeMemory.Free"/>. It is used only for the
/// small, short-lived allocations of the filter API, never on the compression hot path.
/// </para>
/// </remarks>
internal static unsafe class LibLzmaAllocator
{
    /// <summary>
    /// Managed representation of the native lzma_allocator structure from lzma/base.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LzmaAllocator
    {
        public IntPtr Alloc;
        public IntPtr Free;
        public IntPtr Opaque;
    }

    private static readonly IntPtr _handle = Create();

    /// <summary>
    /// Gets a pointer to the native allocator struct, suitable for passing to any liblzma
    /// function that takes a <c>const lzma_allocator *</c>.
    /// </summary>
    /// <remarks>
    /// The struct lives for the lifetime of the process and is never freed, so the pointer
    /// stays valid for as long as any liblzma coder could reference it.
    /// </remarks>
    internal static IntPtr Handle => _handle;

    private static IntPtr Create()
    {
        var allocator = (LzmaAllocator*)NativeMemory.Alloc((nuint)sizeof(LzmaAllocator));

        allocator->Alloc = (IntPtr)(delegate* unmanaged[Cdecl]<void*, nuint, nuint, void*>)&AllocCallback;
        allocator->Free = (IntPtr)(delegate* unmanaged[Cdecl]<void*, void*, void>)&FreeCallback;
        allocator->Opaque = IntPtr.Zero;

        return (IntPtr)allocator;
    }

    /// <summary>
    /// Releases a block liblzma allocated through this allocator.
    /// </summary>
    /// <param name="ptr">The block to free. Null is ignored.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Free(IntPtr ptr) => NativeMemory.Free((void*)ptr);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void* AllocCallback(void* opaque, nuint nmemb, nuint size)
    {
        try
        {
            return NativeMemory.Alloc(nmemb, size);
        }
        catch (OutOfMemoryException)
        {
            // liblzma expects null on failure and turns it into LZMA_MEM_ERROR.
            // An exception must never cross back into native code.
            return null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FreeCallback(void* opaque, void* ptr) => NativeMemory.Free(ptr);
}
