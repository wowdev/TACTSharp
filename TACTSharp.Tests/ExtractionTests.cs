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
            // Pinned on 9.0.1.35078, should still be available as long as it is on wowe1.
            build = new BuildInstance();
            build.LoadConfigs("43a001a23efd4193a96266be43fe67d8", "c67fdeccf96e2a0ddf205e0a7e8f1927");
            build.Load();
        }

        [TestMethod]
        public void ExtractEXE()
        {
            var filename = "WowB.exe";
            var expectedMD5 = "923754949b474d581fd9fdd2c1c32912";

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
