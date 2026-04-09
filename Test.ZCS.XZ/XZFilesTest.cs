using ZCS.XZ;

namespace Test.ZCS.XZ
{
    public class XZFilesTest
    {
        // .lz files with trailing data are rejected when LZMA_CONCATENATED is enabled
        // because liblzma tries to parse the trailing data as another member.
        // This matches the behavior of Joveler.Compression.XZ.
        private static readonly HashSet<string> IgnoreGoodFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "good-1-v0-trailing-1.lz",
            "good-1-v1-trailing-1.lz",
            "good-1-v1-trailing-2.lz",
        };

        // unsupported-check.xz uses an unsupported integrity check type,
        // but liblzma can still decompress the data (just can't verify the check).
        private static readonly HashSet<string> IgnoreUnsupportedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "unsupported-check.xz"
        };

        public static IEnumerable<object[]> GoodFilesTestData()
        {
            foreach (var fileName in Directory.EnumerateFiles("files", "good*.*"))
            {
                // Check if file is ignored
                if (IgnoreGoodFiles.Contains(Path.GetFileName(fileName)))
                    continue;

                yield return new object[] { fileName };
            }
        }

        public static IEnumerable<object[]> BadFilesTestData()
        {
            foreach (var fileName in Directory.EnumerateFiles("files", "bad*.*"))
            {
                yield return new object[] { fileName };
            }
        }

        public static IEnumerable<object[]> UnsupportedFilesTestData()
        {
            foreach (var fileName in Directory.EnumerateFiles("files", "unsupported*.*"))
            {
                if (IgnoreUnsupportedFiles.Contains(Path.GetFileName(fileName)))
                {
                    continue;
                }
                yield return new object[] { fileName };
            }
        }

        private void DecompressFile(string fileName)
        {
            using var fs = File.OpenRead(fileName);
            using var xz = new XZDecompressStream(fs);
            using var ms = new MemoryStream();
            xz.CopyTo(ms);
        }

        [Theory]
        [MemberData(nameof(GoodFilesTestData))]
        public void GoodFiles(string fileName) => DecompressFile(fileName);

        [Theory]
        [MemberData(nameof(BadFilesTestData))]
        public void BadFiles(string fileName) => Assert.Throws<XZException>(() => DecompressFile(fileName));

        [Theory]
        [MemberData(nameof(UnsupportedFilesTestData))]
        public void UnsupportedFiles(string fileName) => Assert.Throws<XZException>(() => DecompressFile(fileName));
    }
}
