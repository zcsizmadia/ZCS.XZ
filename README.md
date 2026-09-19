# ZCS.XZ

[![Build](https://github.com/zcsizmadia/ZCS.XZ/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/zcsizmadia/ZCS.XZ/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/ZCS.XZ.svg)](https://www.nuget.org/packages/ZCS.XZ)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ZCS.XZ.svg)](https://www.nuget.org/packages/ZCS.XZ)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A high-performance .NET library for **XZ (LZMA2) compression and decompression**, built on top of the native [liblzma](https://tukaani.org/xz/) library via P/Invoke.

## Features

- **Streaming API** — standard `System.IO.Stream`-based compress/decompress, compatible with `CopyTo`, `ReadAsync`, pipelines, etc.
- **Zero-copy writes** — the compressor pins the caller's buffer directly via `unsafe fixed`, eliminating intermediate copies on the write path.
- **Multi-threaded compression** — optional parallel encoding via `lzma_stream_encoder_mt` with configurable thread count.
- **Multi-threaded decompression** — optional parallel `.xz` decoding via `lzma_stream_decoder_mt`, with soft and hard memory limits.
- **Auto-detection** — the decompressor automatically handles both `.xz` and legacy `.lzma` file formats.
- **Single-call buffer API** — `XZBuffer` compresses and decompresses in-memory data without the `Stream` machinery.
- **Legacy `.lzma` output** — write the older LZMA_Alone format for tools that cannot read `.xz`.
- **Checksums** — `XZChecksum` exposes liblzma's CRC-32 and CRC-64 implementations.
- **Custom filter chains** — BCJ (x86, ARM64, RISC-V and more) and delta filters via `XZFilterChain`, using the `xz` command-line syntax.
- **Concatenated streams** — supports concatenated `.xz` members (e.g., files produced by `xz --keep` with multiple appends).
- **Cross-platform** — ships native liblzma binaries for Windows, Linux, and macOS on x64 and ARM64.
- **Multi-targeting** — supports .NET 8, .NET 9, and .NET 10.
- **100 % code coverage** — comprehensive test suite with full line, branch, and method coverage.

## Installation

Install via the [NuGet Package Manager](https://www.nuget.org/packages/ZCS.XZ):

```shell
dotnet add package ZCS.XZ
```

Or via the Package Manager Console in Visual Studio:

```powershell
Install-Package ZCS.XZ
```

## Quick Start

### Compress data

```csharp
using ZCS.XZ;

byte[] data = GetData();

using var output = File.Create("data.xz");
using (var xz = new XZCompressStream(output))
{
    xz.Write(data);
}
// The .xz stream is finalized when the XZCompressStream is disposed.
```

### Decompress data

```csharp
using ZCS.XZ;

using var input = File.OpenRead("data.xz");
using var xz = new XZDecompressStream(input);
using var output = new MemoryStream();
xz.CopyTo(output);

byte[] decompressed = output.ToArray();
```

### Compress a file with options

```csharp
using ZCS.XZ;

var options = new XZCompressOptions
{
    Level = XZCompressionLevel.Maximum, // Maximum compression
    Extreme = true,                     // Marginally better ratio, slower
    Threads = 0,                        // Auto-detect thread count
    BufferSize = 131072,                // 128 KB internal buffer
};

using var input = File.OpenRead("largefile.bin");
using var output = File.Create("largefile.bin.xz");
using (var xz = new XZCompressStream(output, options))
{
    input.CopyTo(xz);
}
```

### Decompress with a memory limit

```csharp
using ZCS.XZ;

ulong maxMemory = 256 * 1024 * 1024; // 256 MB

using var input = File.OpenRead("data.xz");
using var xz = new XZDecompressStream(input, memoryLimit: maxMemory, leaveOpen: false);
using var output = new MemoryStream();
xz.CopyTo(output);
```

### Decompress with multiple threads

Threaded decoding handles `.xz` only — see the note under [`XZDecompressOptions`](#xzdecompressoptions).

```csharp
using ZCS.XZ;

using var input = File.OpenRead("data.xz");
using var xz = new XZDecompressStream(input, new XZDecompressOptions
{
    Threads = 0, // auto-detect, uses LibLzmaNativeMethods.CpuThreads
}, leaveOpen: false);
using var output = new MemoryStream();
xz.CopyTo(output);
```

### Compress a small payload in one call

```csharp
using ZCS.XZ;

byte[] packed = XZBuffer.Compress("some data"u8);
byte[] unpacked = XZBuffer.Decompress(packed);
```

### Compress an executable with a BCJ filter

```csharp
using ZCS.XZ;

// The x86 filter rewrites jump and call targets so LZMA2 can match them.
using var filters = XZFilterChain.Parse("x86 lzma2:preset=9e");

using var output = File.Create("program.xz");
using var xz = new XZCompressStream(output, new XZCompressOptions { Filters = filters });
xz.Write(File.ReadAllBytes("program.exe"));
```

No filter chain is needed to read it back — the `.xz` block header records the chain.

### Write the legacy `.lzma` format

```csharp
using ZCS.XZ;

using var output = File.Create("data.lzma");
using var xz = new XZCompressStream(output, new XZCompressOptions
{
    Format = XZFormat.LzmaAlone,
});
xz.Write(data);
```

### Size a memory limit before decompressing

```csharp
using ZCS.XZ;

var options = new XZCompressOptions { Level = XZCompressionLevel.Maximum };

Console.WriteLine($"Encoding needs ~{options.GetMemoryUsage() / (1024 * 1024)} MiB");
Console.WriteLine($"Decoding needs ~{options.GetDecoderMemoryUsage() / (1024 * 1024)} MiB");
```

## API Reference

### `XZCompressStream`

A **write-only** stream that compresses data and writes the `.xz` output to an underlying stream.

| Constructor | Description |
|---|---|
| `XZCompressStream(Stream)` | Default options, disposes the inner stream on close. |
| `XZCompressStream(Stream, XZCompressOptions)` | Custom options, disposes the inner stream on close. |
| `XZCompressStream(Stream, XZCompressOptions, bool leaveOpen)` | Full control over options and inner stream lifetime. |

| Member | Returns | Description |
|---|---|---|
| `GetProgress()` | `XZProgress` | Bytes consumed and produced so far. Accurate with multiple threads, unlike the stream counters. |

> **Important:** The stream **must be disposed** to finalize the `.xz` output (writes the stream footer). Failing to dispose produces a corrupt file.

`Flush()` picks the encoder action that the current configuration supports: `LZMA_SYNC_FLUSH` for single-threaded `.xz`, `LZMA_FULL_FLUSH` for multi-threaded `.xz` (which also ends the current block), and no encoder action at all for `.lzma`, which cannot flush mid-stream.

### `XZDecompressStream`

A **read-only** stream that decompresses `.xz` (or legacy `.lzma`) data from an underlying stream.

| Constructor | Description |
|---|---|
| `XZDecompressStream(Stream)` | Default settings, disposes the inner stream on close. |
| `XZDecompressStream(Stream, bool leaveOpen)` | Control inner stream lifetime. |
| `XZDecompressStream(Stream, ulong memoryLimit, bool leaveOpen)` | Set a decoder memory limit. |
| `XZDecompressStream(Stream, int bufferSize, bool leaveOpen)` | Custom internal buffer size. |
| `XZDecompressStream(Stream, ulong memoryLimit, int bufferSize, bool leaveOpen)` | Full control. |
| `XZDecompressStream(Stream, XZDecompressOptions, bool leaveOpen)` | Full control, including threaded decoding. |

| Property | Type | Description |
|---|---|---|
| `Check` | `LzmaCheck` | Integrity check type of the stream being decoded. Only meaningful after the first read. |
| `MemoryLimit` | `ulong` | Gets or sets the decoder memory limit. Raising it after an `LZMA_MEMLIMIT_ERROR` lets the same stream continue instead of forcing a rebuild. |
| `GetProgress()` | `XZProgress` | Bytes consumed and produced so far. Accurate with multiple threads, unlike the stream counters. |

### `XZDecompressOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `Threads` | `int` | `1` | Thread count. `0` = auto, `1` = single-threaded, `>1` = multi-threaded. |
| `MemoryLimit` | `ulong` | `ulong.MaxValue` | Hard memory limit. Decoding fails with `LZMA_MEMLIMIT_ERROR` if exceeded. |
| `MemoryLimitThreading` | `ulong` | `0` (= physical memory / 4) | Soft limit that reduces the thread count rather than failing. |
| `BufferSize` | `int` | `81920` | Internal I/O buffer size in bytes. |

> **Important:** Threaded decoding (`Threads > 1`) handles the **`.xz` format only**. It does not auto-detect legacy `.lzma` or `.lz` input the way the single-threaded path does. Parallelism also requires block headers carrying compressed and uncompressed sizes, which only the multi-threaded encoder writes — other streams decode single-threaded regardless of this setting.

### `XZCompressOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `Level` | `XZCompressionLevel` | `Default` (6) | Compression level 0–9. |
| `Extreme` | `bool` | `false` | Enable extreme mode for marginally better compression. |
| `Threads` | `int` | `1` | Thread count. `0` = auto, `1` = single-threaded, `>1` = multi-threaded. |
| `BufferSize` | `int` | `81920` | Internal I/O buffer size in bytes. |
| `Format` | `XZFormat` | `Xz` | Container format to produce. |
| `Filters` | `XZFilterChain?` | `null` | Explicit filter chain. When set, `Level` and `Extreme` are ignored. |

| Method | Returns | Description |
|---|---|---|
| `GetMemoryUsage()` | `ulong` | Estimated encoder memory usage for the current settings, accounting for `Threads`. |
| `GetDecoderMemoryUsage()` | `ulong` | Estimated memory needed to *decode* a stream produced with these settings. Useful for choosing `XZDecompressOptions.MemoryLimit`. |

### `XZBuffer`

Single-call compression and decompression for data already in memory, bypassing the `Stream` machinery. Output is an ordinary `.xz` stream.

| Method | Returns | Description |
|---|---|---|
| `Compress(ReadOnlySpan<byte>, XZCompressOptions?)` | `byte[]` | Compress into a new array. |
| `Compress(ReadOnlySpan<byte>, Span<byte>, XZCompressOptions?)` | `int` | Compress into a caller-supplied buffer; returns bytes written. |
| `Decompress(ReadOnlySpan<byte>, ulong memoryLimit)` | `byte[]` | Decompress into a new array, growing the buffer as needed. |
| `Decompress(ReadOnlySpan<byte>, Span<byte>, ulong memoryLimit)` | `int` | Decompress into a caller-supplied buffer; returns bytes written. |
| `GetMaxCompressedLength(int)` | `int` | Worst-case compressed size, for sizing an output buffer. |

> **Note:** Compression here is always single-threaded `.xz`. `XZCompressOptions.Threads`, `BufferSize`, and `Format` do not apply — use `XZCompressStream` when those matter.

### `XZChecksum`

The checksum functions liblzma uses for `.xz` integrity checks, including its hardware-accelerated paths. `Crc64` has no equivalent in the base class library.

| Method | Returns | Description |
|---|---|---|
| `Crc32(ReadOnlySpan<byte>, uint seed = 0)` | `uint` | CRC-32 (IEEE 802.3). |
| `Crc64(ReadOnlySpan<byte>, ulong seed = 0)` | `ulong` | CRC-64 (ECMA-182). |

Pass the previous result as `seed` to build a checksum up across several calls.

### `XZProgress`

A `readonly record struct` with `BytesIn` and `BytesOut`, returned by `XZCompressStream.GetProgress()` and `XZDecompressStream.GetProgress()`. Prefer it over the stream byte counters when using multiple threads, where in-flight work is not yet reflected in the totals.

### `XZFilterChain`

An explicit liblzma filter chain, parsed from the same syntax the `xz` command line uses. Putting a BCJ or delta filter in front of LZMA2 can improve the ratio dramatically on the right data. Implements `IDisposable`.

| Member | Returns | Description |
|---|---|---|
| `Parse(string spec, bool allFilters = false)` | `XZFilterChain` | Parse a chain. Throws `ArgumentException` with the offset and liblzma's message on bad input. |
| `Spec` | `string` | The specification it was parsed from. |
| `ToNormalizedString(bool decoderOptions = false)` | `string` | The chain as liblzma formats it, with implicit options filled in. |
| `GetEncoderMemoryUsage()` / `GetDecoderMemoryUsage()` | `ulong` | Estimated memory for the chain. |
| `ListSupportedFilters(bool allFilters = false)` | `string` | What the loaded liblzma accepts. |

**Syntax:** filters are separated by **spaces** (or `--`); each filter name is followed by `:` and a comma-separated option list. Commas separate options *within* one filter, not the filters themselves — `"x86,lzma2"` is a single unknown filter name, not two filters. Order matters: input flows into the leftmost filter first, and `lzma2` normally comes last.

Available filters: `lzma1`, `lzma2`, `x86`, `arm`, `armthumb`, `arm64`, `riscv`, `powerpc`, `ia64`, `sparc`, `delta`. `lzma1` cannot be stored in `.xz`, so it needs `allFilters: true`.

Measured on synthetic data in this repository's tests:

| Data | `lzma2` alone | With filter | |
|---|---|---|---|
| x86 machine code | 2,004 B | 164 B | `x86 lzma2:preset=6` |
| 32-bit counters | 20,616 B | 576 B | `delta:dist=4 lzma2:preset=6` |

> **Note:** Decoding needs no filter chain — an `.xz` stream records its own chain in each block header, so `XZDecompressStream` reads filtered streams without being configured.

### `XZFormat`

| Value | Description |
|---|---|
| `Xz` | The modern `.xz` format. Supports integrity checks, multithreading, and concatenated streams. Default. |
| `LzmaAlone` | The legacy `.lzma` format, for interoperability with tools that cannot read `.xz`. |

> **Note:** `LzmaAlone` carries no integrity check, cannot be encoded with more than one thread (this throws), and cannot flush mid-stream — `Flush()` only forwards to the underlying stream.

### `XZCompressionLevel`

| Value | Level | Description |
|---|---|---|
| `None` | 0 | No compression (store only). |
| `Fastest` | 1 | Fastest compression. |
| `Level2`–`Level5` | 2–5 | Increasing compression ratio. |
| `Default` | 6 | Recommended balance of speed and ratio. |
| `Level7`–`Level8` | 7–8 | Higher ratio, more CPU and memory. |
| `Maximum` | 9 | Highest ratio, most CPU and memory. |

### `XZException`

Thrown when liblzma returns an error. The `LzmaReturnCode` property contains the raw integer code (e.g., `LZMA_DATA_ERROR`, `LZMA_MEM_ERROR`).

### `LzmaCheck`

Enum for integrity check types: `None`, `Crc32`, `Crc64`, `Sha256`.

### `LibLzmaNativeMethods`

Runtime information about the loaded native library.

| Member | Type | Description |
|---|---|---|
| `NativeVersion` | `Version` | Runtime liblzma version, e.g. `5.8.4`. |
| `NativeVersionString` | `string` | Runtime liblzma version as a string. |
| `CpuThreads` | `int` | Hardware thread count as liblzma detects it — the same value `xz` uses to pick its default. Falls back to `Environment.ProcessorCount` if liblzma cannot detect it. |
| `PhysicalMemory` | `ulong` | Total physical memory in bytes, or `0` if liblzma cannot detect it. |

## Supported Platforms

| OS | Architecture | Native Library |
|---|---|---|
| Windows | x64, ARM64 | `liblzma.dll` |
| Linux | x64, ARM64 | `liblzma.so` |
| macOS | x64, ARM64 | `liblzma.dylib` |

The native liblzma binaries are bundled under the `runtimes/{rid}/native/` directory and resolved automatically at runtime.

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Native liblzma binaries placed under `ZCS.XZ/runtimes/{rid}/native/`

### Build

```shell
dotnet build
```

### Run Tests

```shell
dotnet test
```

### Run Tests with Code Coverage

```shell
dotnet test --collect:"XPlat Code Coverage"
```

To generate an HTML coverage report, install [ReportGenerator](https://github.com/danielpalme/ReportGenerator):

```shell
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:CoverageReport -reporttypes:Html
```

## Contributing

Contributions are welcome! Please follow these steps:

1. **Fork** the repository.
2. **Create a branch** for your feature or bug fix: `git checkout -b feature/my-feature`.
3. **Write tests** — aim to maintain 100 % code coverage.
4. **Build and test**: `dotnet build && dotnet test`.
5. **Submit a pull request** with a clear description of your changes.

Please open an [issue](https://github.com/zcsizmadia/ZCS.XZ/issues) first if you plan a large change, so we can discuss the approach.

## License

This project is licensed under the [MIT License](LICENSE).

## Acknowledgements

- [XZ Utils / liblzma](https://tukaani.org/xz/) — the underlying native compression library.
- [Lasse Collin](https://tukaani.org/xz/) and [Jia Tan](https://github.com/JiaT75) — liblzma authors.
- [Joveler.Compression.XZ](https://github.com/ied206/Joveler.Compression) — another excellent .NET XZ binding that served as a reference and inspiration for this project.
