using System.Diagnostics;
using System.Text;

namespace ZCS.XZ.Tests;

/// <summary>
/// Cross-validates XZCompressStream and XZDecompressStream against the system xz executable
/// across all compression levels (0–9) and extreme flag combinations.
/// </summary>
public class XZInteropTests
{
    private const int TestDataLength = 123_456;
    private static readonly byte[] TestData;

    public static IEnumerable<object[]> AllLevelAndExtremeCombinations()
    {
        foreach(var level in Enum.GetValues<XZCompressionLevel>())
        {
            yield return new object[] { (int)level, false };
            yield return new object[] { (int)level, true };
        }
    }

    static XZInteropTests()
    {
        TestData = new byte[TestDataLength];
        var rng = new Random((int)DateTime.Now.Ticks);
        rng.NextBytes(TestData);
    }

    /// <summary>
    /// Compress with the system xz executable at every level/extreme combination,
    /// then decompress with XZDecompressStream and verify the output matches.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllLevelAndExtremeCombinations))]
    public void Decompress_XzCompressed_AllLevelsAndExtreme(int level, bool extreme)
    {
        var compressed = CompressWithXzExecutable(TestData, level, extreme, out var exitCode);
        Assert.Equal(0, exitCode);

        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);
        using var result = new MemoryStream();
        xz.CopyTo(result);

        Assert.Equal(TestData, result.ToArray());
    }

    /// <summary>
    /// Compress with XZCompressStream at every level/extreme combination,
    /// then decompress with the system xz executable and verify the output matches.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllLevelAndExtremeCombinations))]
    public void Compress_XzDecompressed_AllLevelsAndExtreme(int level, bool extreme)
    {
        var opts = new XZCompressOptions
        {
            Level = (XZCompressionLevel)level,
            Extreme = extreme,
        };

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var xz = new XZCompressStream(ms, opts, leaveOpen: true))
            {
                xz.Write(TestData, 0, TestData.Length);
            }
            compressed = ms.ToArray();
        }

        var decompressed = DecompressWithXzExecutable(compressed, out var exitCode);
        Assert.Equal(0, exitCode);

        Assert.Equal(TestData, decompressed);
    }

    [Theory]
    [MemberData(nameof(AllLevelAndExtremeCombinations))]
    public void Compress_XzTested_AllLevelsAndExtreme(int level, bool extreme)
    {
        var opts = new XZCompressOptions
        {
            Level = (XZCompressionLevel)level,
            Extreme = extreme,
        };

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var xz = new XZCompressStream(ms, opts, leaveOpen: true))
            {
                xz.Write(TestData, 0, TestData.Length);
            }
            compressed = ms.ToArray();
        }

        var captured = TestWithXzExecutable(compressed, out var exitCode);
        Assert.Equal(0, exitCode);
        Assert.Equal(Array.Empty<byte>(), captured);
    }

    private static byte[] CompressWithXzExecutable(byte[] data, int level, bool extreme, out int exitCode) =>
        RunXz($"-{level}{(extreme ? "e" : "")} -c --format=xz", data, out exitCode);

    private static byte[] DecompressWithXzExecutable(byte[] data, out int exitCode) =>
        RunXz("-d -c", data, out exitCode);

    private static byte[] TestWithXzExecutable(byte[] data, out int exitCode) =>
        RunXz("-t -c", data, out exitCode);

    private static byte[] RunXz(string arguments, byte[] stdin, out int exitCode)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "xz",
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start xz!");
        }

        // Create a memory stream for the captured output.
        // We can't use process.StandardOutput.BaseStream directly because we need to read it asynchronously
        // while writing to stdin to avoid deadlocks if xz produces a lot of output.
        using var output = new MemoryStream(stdin.Length);

        // Start reading stdout and stderr concurrently before writing to stdin to avoid deadlocks
        Task readOutputTask = Task.Run(() => process.StandardOutput.BaseStream.CopyTo(output));
        Task<string> readErrorTask = Task.Run(() => process.StandardError.ReadToEnd());

        // Write stdin and close it so xz knows input is complete
        process.StandardInput.BaseStream.Write(stdin, 0, stdin.Length);
        process.StandardInput.BaseStream.Flush();
        process.StandardInput.BaseStream.Close();

        // Wait for both output and error reading tasks to complete
        Task.WaitAll(readOutputTask, readErrorTask);

        var stderr = readErrorTask.Result;

        // Wait for the process to exit and check the exit code
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"xz exited with code {process.ExitCode}: {stderr}");
        }

        exitCode = process.ExitCode;

        return output.ToArray();
    }

    private static byte[] GenerateTestData(int size)
    {
        var data = new byte[size];
        var rng = new Random((int)DateTime.Now.Ticks);
        rng.NextBytes(data);
        return data;
    }
}
