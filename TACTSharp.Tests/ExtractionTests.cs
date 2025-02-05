using System.Security.Cryptography;

namespace TACTSharp.Tests
{
    [TestClass]
    public sealed class ExtractionTests
    {
        private BuildInstance build;

        [TestInitialize]
        public void Initialize()
        {
            // Pinned on 11.1.0.58945, should remain available with archive fallbacks if needed.
            build = new BuildInstance("f243bb339503142f617fd44d9170338a", "61ae809fa4cead855609d40da0d815e1");
            build.Load();
        }

        [TestMethod]
        public void ExtractEXE()
        {
            var filename = "WowT.exe";
            var expectedMD5 = "a9d720ccc4d81a27bae481bafae6be3a";

            var fileEntries = build.Install.Entries.Where(x => x.name.Equals(filename, StringComparison.InvariantCultureIgnoreCase)).ToList();
            if (fileEntries.Count == 0)
                Assert.Fail("Failed to find " + filename + " in install");

            byte[] targetCKey;
            if (fileEntries.Count > 1)
            {
                var filter = fileEntries.Where(x => x.tags.Contains("4=US")).Select(x => x.md5);
                if (filter.Any())
                {
                    Console.WriteLine("Multiple results found in install for file " + filename + ", using US version..");
                    targetCKey = filter.First();
                }
                else
                {
                    Console.WriteLine("Multiple results found in install for file " + filename + ", using first result..");
                    targetCKey = fileEntries[0].md5;
                }
            }
            else
            {
                targetCKey = fileEntries[0].md5;
            }

            // Check if CKey is the one we expect it to be
            Assert.AreEqual(expectedMD5, Convert.ToHexStringLower(targetCKey));

            var fileEncodingKeys = build.Encoding.FindContentKey(targetCKey);
            if (!fileEncodingKeys)
                Assert.Fail("EKey not found in encoding");

            var fileBytes = build.OpenFileByEKey(fileEncodingKeys[0], fileEncodingKeys.DecodedFileSize);

            // Check if exe is the same as CKey (MD5)
            Assert.IsTrue(MD5.HashData(fileBytes).SequenceEqual(targetCKey));
        }
    }
}
