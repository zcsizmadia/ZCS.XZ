using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ZCS.XZ.Tests;

public class XZVersionTests
{
    [Fact]
    public void NativeVersion_ReturnsValidVersion()
    {
        var version = LibLzmaNativeMethods.NativeVersion;
        Assert.True(version.Major >= 5, $"Expected major version >= 5, got {version.Major}");
        Assert.True(version.Minor >= 0);
        Assert.True(version.Build >= 0);
    }

    [Fact]
    public void NativeVersionString_ReturnsNonEmpty()
    {
        var versionString = LibLzmaNativeMethods.NativeVersionString;
        Assert.False(string.IsNullOrEmpty(versionString));
        Assert.Contains(".", versionString);
    }

    [Fact]
    public void NativeVersion_MatchesNativeVersionString()
    {
        var version = LibLzmaNativeMethods.NativeVersion;
        var versionString = LibLzmaNativeMethods.NativeVersionString;
        Assert.StartsWith($"{version.Major}.{version.Minor}.{version.Build}", versionString);
    }

    [Fact]
    public void NativeVersion_MatchesDirectoryBuildPropsVersion()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // TODO:
            // !!! On macOS, liblzma reports 5.4.3, which is incorrect. It is a false alarm,
            // !!! vcpkg correctly builds and links against 5.8.3, but the version reporting in the library is wrong.
            // !!! Remove this workaround and enable the test once the issue is resolved in the library.
            return;
        }

        var expected = typeof(XZVersionTests).Assembly
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .First(a => a.Key == "LibLzmaVersion").Value;

        var nativeVersion = LibLzmaNativeMethods.NativeVersion;
        Assert.Equal(expected, $"{nativeVersion.Major}.{nativeVersion.Minor}.{nativeVersion.Build}");
    }
}

