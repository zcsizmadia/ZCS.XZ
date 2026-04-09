# ZCS.XZ

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/ZCS.XZ.svg)](https://www.nuget.org/packages/ZCS.XZ)

A high-performance .NET library for **XZ (LZMA2) compression and decompression**, built on top of the native [liblzma](https://tukaani.org/xz/) library via P/Invoke.

## Features

- **Streaming API** — standard `System.IO.Stream`-based compress/decompress, compatible with `CopyTo`, `ReadAsync`, pipelines, etc.
- **Zero-copy writes** — the compressor pins the caller's buffer directly via `unsafe fixed`, eliminating intermediate copies on the write path.
- **Multi-threaded compression** — optional parallel encoding via `lzma_stream_encoder_mt` with configurable thread count.
- **Auto-detection** — the decompressor automatically handles both `.xz` and legacy `.lzma` file formats.
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
    Level = XZCompressionLevel.Level9,  // Maximum compression
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

## API Reference

### `XZCompressStream`

A **write-only** stream that compresses data and writes the `.xz` output to an underlying stream.

| Constructor | Description |
|---|---|
| `XZCompressStream(Stream)` | Default options, disposes the inner stream on close. |
| `XZCompressStream(Stream, XZCompressOptions)` | Custom options, disposes the inner stream on close. |
| `XZCompressStream(Stream, XZCompressOptions, bool leaveOpen)` | Full control over options and inner stream lifetime. |

> **Important:** The stream **must be disposed** to finalize the `.xz` output (writes the stream footer). Failing to dispose produces a corrupt file.

### `XZDecompressStream`

A **read-only** stream that decompresses `.xz` (or legacy `.lzma`) data from an underlying stream.

| Constructor | Description |
|---|---|
| `XZDecompressStream(Stream)` | Default settings, disposes the inner stream on close. |
| `XZDecompressStream(Stream, bool leaveOpen)` | Control inner stream lifetime. |
| `XZDecompressStream(Stream, ulong memoryLimit, bool leaveOpen)` | Set a decoder memory limit. |
| `XZDecompressStream(Stream, int bufferSize, bool leaveOpen)` | Custom internal buffer size. |
| `XZDecompressStream(Stream, ulong memoryLimit, int bufferSize, bool leaveOpen)` | Full control. |

### `XZCompressOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `Level` | `XZCompressionLevel` | `Default` (6) | Compression level 0–9. |
| `Extreme` | `bool` | `false` | Enable extreme mode for marginally better compression. |
| `Threads` | `int` | `1` | Thread count. `0` = auto, `1` = single-threaded, `>1` = multi-threaded. |
| `BufferSize` | `int` | `81920` | Internal I/O buffer size in bytes. |

### `XZCompressionLevel`

| Value | Level | Description |
|---|---|---|
| `None` | 0 | No compression (store only). |
| `Fastest` | 1 | Fastest compression. |
| `Level2`–`Level5` | 2–5 | Increasing compression ratio. |
| `Default` | 6 | Recommended balance of speed and ratio. |
| `Level7`–`Level9` | 7–9 | Higher ratio, more CPU and memory. |
| `Maximum` | 9 | Alias for `Level9`. |

### `XZException`

Thrown when liblzma returns an error. The `LzmaReturnCode` property contains the raw integer code (e.g., `LZMA_DATA_ERROR`, `LZMA_MEM_ERROR`).

### `LzmaCheck`

Enum for integrity check types: `None`, `Crc32`, `Crc64`, `Sha256`.

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
