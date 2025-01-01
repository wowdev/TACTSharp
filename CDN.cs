namespace TACTIndexTestCSharp
{
    public static class CDN
    {
        private static readonly HttpClient Client = new();

        private static readonly List<string> CDNServers = ["archive.wow.tools"];

        static CDN()
        {
            if (!Directory.Exists("cache"))
                Directory.CreateDirectory("cache");
        }

        private static async Task<byte[]> DownloadFile(string tprDir, string type, string hash, ulong size = 0)
        {
            var cachePath = Path.Combine("cache", tprDir, type, hash);
            if (File.Exists(cachePath))
            {
                if (size > 0 && (ulong)new FileInfo(cachePath).Length != size)
                    File.Delete(cachePath);
                else
                    return File.ReadAllBytes(cachePath);
            }

            var url = $"http://{CDNServers[0]}/tpr/{tprDir}/{type}/{hash[0]}{hash[1]}/{hash[2]}{hash[3]}/{hash}";

            Console.WriteLine("Downloading " + url);

            var response = await Client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new Exception("Failed to download " + url);

            var data = await response.Content.ReadAsByteArrayAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllBytes(cachePath, data);
            return data;
        }

        private static async Task<byte[]> DownloadFileFromArchive(string eKey, string tprDir, string archive, int offset, int size)
        {
            var cachePath = Path.Combine("cache", tprDir, "wow", eKey);
            if (File.Exists(cachePath))
            {
                if (new FileInfo(cachePath).Length == size)
                    return File.ReadAllBytes(cachePath);
                else
                    File.Delete(cachePath);
            }

            var url = $"http://{CDNServers[0]}/tpr/{tprDir}/data/{archive[0]}{archive[1]}/{archive[2]}{archive[3]}/{archive}";

            Console.WriteLine("Downloading file " + eKey + " from archive " + archive + " at offset " + offset + " with size " + size);

            var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers =
                {
                    Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + size - 1)
                }
            };

            var response = await Client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new Exception("Failed to download " + url);

            var data = await response.Content.ReadAsByteArrayAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllBytes(cachePath, data);
            return data;
        }

        public static async Task<string> GetFilePath(string tprDir, string type, string hash, ulong decompressedSize, ulong compressedSize, bool decoded = false)
        {
            var decodedPath = Path.Combine("cache", tprDir, type, hash + ".decoded");
            if (decoded && File.Exists(decodedPath))
            {
                var currentDecompLength = (ulong)new FileInfo(decodedPath).Length;
                if (currentDecompLength == decompressedSize)
                    return decodedPath;
            }

            var data = await DownloadFile(tprDir, type, hash, compressedSize);
            if (!decoded)
            {
                return Path.Combine("cache", tprDir, type, hash);
            }
            else
            {
                var decodedData = BLTE.Decode(data, decompressedSize);
                Directory.CreateDirectory(Path.Combine("cache", tprDir, type));
                await File.WriteAllBytesAsync(decodedPath, decodedData);
                return decodedPath;
            }
        }
        public static async Task<string> GetFilePathFromArchive(string eKey, string tprDir, string archive, int offset, int length, ulong decompressedSize, bool decoded = false)
        {
            var decodedPath = Path.Combine("cache", tprDir, "data", eKey + ".decoded");
            if (decoded && File.Exists(decodedPath))
            {
                var currentDecompLength = (ulong)new FileInfo(decodedPath).Length;
                if (currentDecompLength == decompressedSize)
                    return decodedPath;
            }

            var data = await DownloadFileFromArchive(eKey, tprDir, archive, offset, length);
            if (!decoded)
            {
                return Path.Combine("cache", tprDir, "data", eKey);
            }
            else
            {
                var decodedData = BLTE.Decode(data, decompressedSize);
                Directory.CreateDirectory(Path.Combine("cache", tprDir, "data"));
                await File.WriteAllBytesAsync(decodedPath, decodedData);
                return decodedPath;
            }
        }
    }
}
