using System;
using ZCS.XZ;

try
{
    // Generate test data
    byte[] original = new byte[100_000];
    Random.Shared.NextBytes(original);

    // Compress
    byte[] compressed;
    using(var output = new MemoryStream())
    {
        using(var input = new MemoryStream(original))
        using(var xz = new XZCompressStream(output))
        {
            input.CopyTo(xz);
        }
        compressed = output.ToArray();
    }

    // Decompress
    byte[] decompressed;
    using(var output = new MemoryStream())
    {
        using(var input = new MemoryStream(compressed))
        using(var xz = new XZDecompressStream(input))
        {
            xz.CopyTo(output);
        }
        decompressed = output.ToArray();
    }
            
    if (!original.SequenceEqual(decompressed))
    {
        throw new InvalidOperationException(" Decompressed data does not match original!");
    }

    Console.WriteLine("Success.");
}
catch(Exception ex)
{
    Console.Error.WriteLine($"Error: {ex}");
    return 1;
}

return 0;
