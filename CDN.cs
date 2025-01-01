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

        private static async Task<byte[]> DownloadFile(string tprDir, string type, string hash)
        {
            var cachePath = Path.Combine("cache", tprDir, type, hash);
            if (File.Exists(cachePath))
                return File.ReadAllBytes(cachePath);

            var url = $"http://{CDNServers[0]}/tpr/{tprDir}/{type}/{hash[0]}{hash[1]}/{hash[2]}{hash[3]}/{hash}";

            Console.WriteLine("Downloading " + url);

            var response = await Client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new Exception("Failed to download " + url);

            var data = await response.Content.ReadAsByteArrayAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            File.WriteAllBytes(cachePath, data);
            return data;
        }

        private static async Task<byte[]> DownloadFileFromArchive(string eKey, string tprDir, string archive, int offset, int size)
        {
            var cachePath = Path.Combine("cache", tprDir, "wow", eKey);
            if (File.Exists(cachePath))
                return File.ReadAllBytes(cachePath);

            var url = $"http://{CDNServers[0]}/tpr/{tprDir}/data/{archive[0]}{archive[1]}/{archive[2]}{archive[3]}/{archive}";

            Console.WriteLine("Downloading file " + eKey + " from archive " + archive + " at offset " + offset + " with size " + size);

            var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers =
                {
                    Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + size)
                }
            };

            var response = await Client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new Exception("Failed to download " + url);

            var data = await response.Content.ReadAsByteArrayAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            File.WriteAllBytes(cachePath, data);
            return data;
        }


        public static async Task<byte[]> GetFile(string tprDir, string type, string hash, bool decoded = false)
        {
            if (decoded && File.Exists(Path.Combine("cache", tprDir, type, hash + ".decoded")))
                return File.ReadAllBytes(Path.Combine("cache", tprDir, type, hash + ".decoded"));

            var data = await DownloadFile(tprDir, type, hash);
            if (!decoded)
            {
                return data;
            }
            else
            {
                var decodedData = BLTE.Decode(data);
                Directory.CreateDirectory(Path.Combine("cache", tprDir, type));
                await File.WriteAllBytesAsync(Path.Combine("cache", tprDir, type, hash + ".decoded"), decodedData);
                return decodedData;
            }
        }

        public static async Task<string> GetFilePath(string tprDir, string type, string hash, bool decoded = false)
        {
            if (decoded && File.Exists(Path.Combine("cache", tprDir, type, hash + ".decoded")))
                return Path.Combine("cache", tprDir, type, hash + ".decoded");

            var data = await DownloadFile(tprDir, type, hash);
            if (!decoded)
            {
                return Path.Combine("cache", tprDir, type, hash);
            }
            else
            {
                var decodedData = BLTE.Decode(data);
                Directory.CreateDirectory(Path.Combine("cache", tprDir, type));
                await File.WriteAllBytesAsync(Path.Combine("cache", tprDir, type, hash + ".decoded"), decodedData);
                return Path.Combine("cache", tprDir, type, hash + ".decoded");
            }
        }
        public static async Task<string> GetFilePathFromArchive(string eKey, string tprDir, string archive, int offset, int size, bool decoded = false)
        {
            if (decoded && File.Exists(Path.Combine("cache", tprDir, "data", eKey + ".decoded")))
                return Path.Combine("cache", tprDir, "data", eKey + ".decoded");

            var data = await DownloadFileFromArchive(eKey, tprDir, archive, offset, size);
            if (!decoded)
            {
                return Path.Combine("cache", tprDir, "data", eKey);
            }
            else
            {
                var decodedData = BLTE.Decode(data);
                Directory.CreateDirectory(Path.Combine("cache", tprDir, "data"));
                await File.WriteAllBytesAsync(Path.Combine("cache", tprDir, "data", eKey + ".decoded"), decodedData);
                return Path.Combine("cache", tprDir, "data", eKey + ".decoded");
            }
        }
    }
}
