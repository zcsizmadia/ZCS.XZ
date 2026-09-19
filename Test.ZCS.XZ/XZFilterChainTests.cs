namespace ZCS.XZ.Tests;

public class XZFilterChainTests
{
    /// <summary>
    /// Synthetic x86 machine code: a run of CALL rel32 instructions all targeting the same
    /// absolute address. The BCJ x86 filter rewrites relative operands to absolute ones,
    /// which turns every operand into the same bytes.
    /// </summary>
    private static byte[] MakeX86Code(int instructions)
    {
        var data = new byte[instructions * 8];
        const int target = 0x1000;

        for (int i = 0; i < instructions; i++)
        {
            int pos = i * 8;
            data[pos] = 0xE8; // call rel32
            BitConverter.GetBytes(target - (pos + 5)).CopyTo(data, pos + 1);
            data[pos + 5] = 0x90; // nop
            data[pos + 6] = 0x90;
            data[pos + 7] = 0x90;
        }

        return data;
    }

    /// <summary>
    /// Little-endian 32-bit counter. The delta filter with dist=4 reduces it to a constant.
    /// </summary>
    private static byte[] MakeCounters(int count)
    {
        var data = new byte[count * 4];
        for (int i = 0; i < count; i++)
        {
            BitConverter.GetBytes(i * 7).CopyTo(data, i * 4);
        }
        return data;
    }

    private static byte[] Compress(byte[] data, XZCompressOptions options)
    {
        using var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, options, leaveOpen: true))
        {
            xz.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using var src = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(src, leaveOpen: true);
        using var ms = new MemoryStream();
        xz.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Parse_AcceptsFilterChain()
    {
        using var chain = XZFilterChain.Parse("x86 lzma2:preset=6");

        Assert.Equal("x86 lzma2:preset=6", chain.Spec);
        Assert.Equal("x86 lzma2:preset=6", chain.ToString());
    }

    [Fact]
    public void Parse_AcceptsBarePreset()
    {
        using var chain = XZFilterChain.Parse("6");

        Assert.Equal("6", chain.Spec);
    }

    [Fact]
    public void Parse_NullSpec_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => XZFilterChain.Parse(null!));
    }

    [Fact]
    public void Parse_UnknownFilter_ThrowsWithPositionAndMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => XZFilterChain.Parse("nosuchfilter lzma2"));

        Assert.Contains("offset 0", ex.Message);
        Assert.Contains("Unknown filter name", ex.Message);
    }

    [Fact]
    public void Parse_BadOptionValue_ReportsOffsetPastTheFilterName()
    {
        // delta accepts dist=1..256, so 999 is out of range.
        var ex = Assert.Throws<ArgumentException>(() => XZFilterChain.Parse("delta:dist=999 lzma2"));

        Assert.DoesNotContain("offset 0,", ex.Message);
        Assert.Contains("delta:dist=999", ex.Message);
    }

    [Fact]
    public void Parse_CommaSeparatedFilters_IsRejected()
    {
        // Commas separate options within one filter, not the filters themselves.
        // "x86,lzma2" therefore reads as a single unknown filter name.
        var ex = Assert.Throws<ArgumentException>(() => XZFilterChain.Parse("x86,lzma2"));

        Assert.Contains("Unknown filter name", ex.Message);
    }

    [Fact]
    public void Parse_Lzma1_RequiresAllFilters()
    {
        // LZMA1 cannot be stored in .xz, so it is rejected unless allFilters is set.
        Assert.Throws<ArgumentException>(() => XZFilterChain.Parse("lzma1:preset=6"));

        using var chain = XZFilterChain.Parse("lzma1:preset=6", allFilters: true);
        Assert.Equal("lzma1:preset=6", chain.Spec);
    }

    [Fact]
    public void BcjX86Filter_SubstantiallyImprovesCompressionOfMachineCode()
    {
        byte[] original = MakeX86Code(20_000);

        byte[] plain = Compress(original, new XZCompressOptions());

        using var chain = XZFilterChain.Parse("x86 lzma2:preset=6");
        byte[] filtered = Compress(original, new XZCompressOptions { Filters = chain });

        // Measured at roughly 2004 -> 164 bytes; assert a conservative 4x improvement.
        Assert.True(filtered.Length * 4 < plain.Length,
            $"Expected the x86 filter ({filtered.Length}) to beat plain lzma2 ({plain.Length}) by 4x");
        Assert.Equal(original, Decompress(filtered));
    }

    [Fact]
    public void DeltaFilter_SubstantiallyImprovesCompressionOfCounters()
    {
        byte[] original = MakeCounters(50_000);

        byte[] plain = Compress(original, new XZCompressOptions());

        using var chain = XZFilterChain.Parse("delta:dist=4 lzma2:preset=6");
        byte[] filtered = Compress(original, new XZCompressOptions { Filters = chain });

        // Measured at roughly 20616 -> 576 bytes; assert a conservative 4x improvement.
        Assert.True(filtered.Length * 4 < plain.Length,
            $"Expected delta=4 ({filtered.Length}) to beat plain lzma2 ({plain.Length}) by 4x");
        Assert.Equal(original, Decompress(filtered));
    }

    [Fact]
    public void DeltaFilter_WrongDistanceMakesCompressionWorse()
    {
        // A negative control: if the dist option were ignored, this would match dist=4.
        byte[] original = MakeCounters(50_000);

        using var good = XZFilterChain.Parse("delta:dist=4 lzma2:preset=6");
        using var bad = XZFilterChain.Parse("delta:dist=1 lzma2:preset=6");

        byte[] withGoodDistance = Compress(original, new XZCompressOptions { Filters = good });
        byte[] withBadDistance = Compress(original, new XZCompressOptions { Filters = bad });

        Assert.True(withBadDistance.Length > withGoodDistance.Length * 10,
            $"dist=1 ({withBadDistance.Length}) should be far worse than dist=4 ({withGoodDistance.Length})");
        Assert.Equal(original, Decompress(withBadDistance));
    }

    [Fact]
    public void FilteredStream_DecodesWithoutBeingToldTheChain()
    {
        // The .xz block header records the chain, so the decoder needs no configuration.
        byte[] original = MakeX86Code(5_000);

        using var chain = XZFilterChain.Parse("x86 lzma2:preset=1");
        byte[] compressed = Compress(original, new XZCompressOptions { Filters = chain });

        using var src = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(src, leaveOpen: true);
        using var ms = new MemoryStream();
        xz.CopyTo(ms);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact]
    public void Filters_WorkWithMultipleThreads()
    {
        byte[] original = MakeX86Code(50_000);

        using var chain = XZFilterChain.Parse("x86 lzma2:preset=1");
        byte[] filtered = Compress(original, new XZCompressOptions { Filters = chain, Threads = 4 });
        byte[] plain = Compress(original, new XZCompressOptions { Level = XZCompressionLevel.Fastest, Threads = 4 });

        // Compare sizes rather than only round-tripping: if lzma_mt.filters were left null
        // the threaded encoder would silently fall back to the preset and still round-trip.
        Assert.True(filtered.Length * 4 < plain.Length,
            $"Expected the threaded encoder to apply the filter ({filtered.Length} vs {plain.Length})");
        Assert.Equal(original, Decompress(filtered));
    }

    [Fact]
    public void Filters_CanBeDisposedImmediatelyAfterConstructingTheStream()
    {
        // liblzma copies the chain during encoder init, so the caller need not keep it alive.
        byte[] original = MakeX86Code(2_000);

        using var ms = new MemoryStream();
        var chain = XZFilterChain.Parse("x86 lzma2:preset=1");
        using (var xz = new XZCompressStream(ms, new XZCompressOptions { Filters = chain }, leaveOpen: true))
        {
            chain.Dispose();
            xz.Write(original, 0, original.Length);
        }

        Assert.Equal(original, Decompress(ms.ToArray()));
    }

    [Fact]
    public void Filters_WithLzmaAloneFormat_Throws()
    {
        using var chain = XZFilterChain.Parse("x86 lzma2:preset=6");
        var options = new XZCompressOptions { Filters = chain, Format = XZFormat.LzmaAlone };

        Assert.Throws<ArgumentException>(() =>
            new XZCompressStream(new MemoryStream(), options, leaveOpen: true));
    }

    [Fact]
    public void ToNormalizedString_ExpandsImplicitOptions()
    {
        using var chain = XZFilterChain.Parse("x86 lzma2:preset=9e");

        string normalized = chain.ToNormalizedString();

        Assert.StartsWith("x86 lzma2:", normalized);
        Assert.Contains("dict=", normalized);
        Assert.Contains("mf=", normalized);
    }

    [Fact]
    public void MemoryUsage_IsReportedForBothDirections()
    {
        using var chain = XZFilterChain.Parse("x86 lzma2:preset=9e");

        ulong encoder = chain.GetEncoderMemoryUsage();
        ulong decoder = chain.GetDecoderMemoryUsage();

        Assert.NotEqual(ulong.MaxValue, encoder);
        Assert.NotEqual(ulong.MaxValue, decoder);
        Assert.True(decoder < encoder, $"Decoding ({decoder}) should cost less than encoding ({encoder})");
    }

    [Fact]
    public void CompressOptions_MemoryUsage_UsesTheFilterChain()
    {
        using var chain = XZFilterChain.Parse("x86 lzma2:preset=9e");
        var withFilters = new XZCompressOptions { Filters = chain };
        var withPreset = new XZCompressOptions { Level = XZCompressionLevel.Fastest };

        Assert.Equal(chain.GetEncoderMemoryUsage(), withFilters.GetMemoryUsage());
        Assert.Equal(chain.GetDecoderMemoryUsage(), withFilters.GetDecoderMemoryUsage());
        Assert.True(withFilters.GetMemoryUsage() > withPreset.GetMemoryUsage());
    }

    [Fact]
    public void ListSupportedFilters_IncludesTheExpectedFilters()
    {
        string list = XZFilterChain.ListSupportedFilters();

        Assert.Contains("lzma2", list);
        Assert.Contains("x86", list);
        Assert.Contains("arm64", list);
        Assert.Contains("delta", list);
    }

    [Fact]
    public void ListSupportedFilters_AllFilters_AddsLzma1()
    {
        Assert.DoesNotContain("lzma1", XZFilterChain.ListSupportedFilters());
        Assert.Contains("lzma1", XZFilterChain.ListSupportedFilters(allFilters: true));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var chain = XZFilterChain.Parse("lzma2");

        chain.Dispose();
        chain.Dispose();

        Assert.Throws<ObjectDisposedException>(() => chain.GetEncoderMemoryUsage());
        Assert.Throws<ObjectDisposedException>(() => chain.ToNormalizedString());
    }

    [Fact]
    public void Spec_SurvivesDisposal()
    {
        var chain = XZFilterChain.Parse("lzma2:preset=3");
        chain.Dispose();

        Assert.Equal("lzma2:preset=3", chain.Spec);
    }
}
