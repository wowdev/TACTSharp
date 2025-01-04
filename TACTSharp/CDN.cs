using System.Collections.Concurrent;

namespace TACTSharp
{
    public static class CDN
    {
        private static readonly HttpClient Client = new();
        private static readonly List<string> CDNServers = [];
        private static readonly ConcurrentDictionary<string, Lock> FileLocks = new();

        // TODO: Memory mapped cache file access?
        // TODO: Product is build-specific so that might not be good to have statically in Settings/used below
        static CDN()
        {
            var cdnsFile = Client.GetStringAsync($"http://{Settings.Region}.patch.battle.net:1119/{Settings.Product}/cdns").Result;

            foreach (var line in cdnsFile.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (!line.StartsWith(Settings.Region + "|"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 2)
                    continue;

                CDNServers.AddRange(splitLine[2].Trim().Split(' '));
            }

            CDNServers.Add("archive.wow.tools");

            var pingTasks = new List<Task<(string server, long ping)>>();
            foreach (var server in CDNServers)
            {
                pingTasks.Add(Task.Run(() =>
                {
                    var ping = new System.Net.NetworkInformation.Ping().Send(server).RoundtripTime;
                    return (server, ping);
                }));
            }

            var pings = Task.WhenAll(pingTasks).Result;

            CDNServers = pings.OrderBy(p => p.ping).Select(p => p.server).ToList();

            Console.WriteLine("Fastest CDNs in order: " + string.Join(", ", CDNServers));
        }

        public static async Task<string> GetProductVersions(string product)
        {
            return await Client.GetStringAsync($"https://{Settings.Region}.version.battle.net/{product}/versions");
        }

        private static async Task<byte[]> DownloadFile(string tprDir, string type, string hash, ulong size = 0, CancellationToken token = new())
        {
            var cachePath = Path.Combine("cache", tprDir, type, hash);
            FileLocks.TryAdd(cachePath, new Lock());

            if (File.Exists(cachePath))
            {
                if (size > 0 && (ulong)new FileInfo(cachePath).Length != size)
                    File.Delete(cachePath);
                else
                    lock (FileLocks[cachePath])
                        return File.ReadAllBytes(cachePath);
            }

            var success = false;
            for (var i = 0; i < CDNServers.Count; i++)
            {
                try
                {
                    var url = $"http://{CDNServers[i]}/tpr/{tprDir}/{type}/{hash[0]}{hash[1]}/{hash[2]}{hash[3]}/{hash}";

                    Console.WriteLine("Downloading " + url);

                    var response = await Client.GetAsync(url, token);
                    if (!response.IsSuccessStatusCode)
                        throw new Exception("Encountered HTTP " + response.StatusCode + " downloading " + hash + " from " + CDNServers[i]);

                    var data = await response.Content.ReadAsByteArrayAsync(token);

                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                    lock (FileLocks[cachePath])
                        File.WriteAllBytes(cachePath, data);

                    success = true;

                    return data;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to download: " + e.Message);
                    continue;
                }
            }

            if (!success)
                throw new FileNotFoundException("Exhausted all CDNs trying to download " + hash);

            return null;
        }

        private static async Task<byte[]> DownloadFileFromArchive(string eKey, string tprDir, string archive, int offset, int size, CancellationToken token = new())
        {
            var cachePath = Path.Combine("cache", tprDir, "data", eKey);
            FileLocks.TryAdd(cachePath, new Lock());

            if (File.Exists(cachePath))
            {
                if (new FileInfo(cachePath).Length == size)
                    lock (FileLocks[cachePath])
                        return File.ReadAllBytes(cachePath);
                else
                    File.Delete(cachePath);
            }

            var success = false;
            for (var i = 0; i < CDNServers.Count; i++)
            {
                try
                {
                    var url = $"http://{CDNServers[i]}/tpr/{tprDir}/data/{archive[0]}{archive[1]}/{archive[2]}{archive[3]}/{archive}";

                    Console.WriteLine("Downloading file " + eKey + " from archive " + archive + " at offset " + offset + " with size " + size);

                    var request = new HttpRequestMessage(HttpMethod.Get, url)
                    {
                        Headers =
                        {
                            Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + size - 1)
                        }
                    };

                    var response = await Client.SendAsync(request, token);

                    if (!response.IsSuccessStatusCode)
                        throw new Exception("Encountered HTTP " + response.StatusCode + " downloading " + eKey + " (archive " + archive + ") from " + CDNServers[i]);

                    var data = await response.Content.ReadAsByteArrayAsync(token);
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                    lock (FileLocks[cachePath])
                        File.WriteAllBytes(cachePath, data);
                    return data;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to download: " + e.Message);
                    continue;
                }
            }

            if (!success)
                throw new FileNotFoundException("Exhausted all CDNs trying to download " + eKey + " (archive " + archive + ")");

            return null;
        }

        public static async Task<byte[]> GetFile(string tprDir, string type, string hash, ulong compressedSize = 0, ulong decompressedSize = 0, bool decoded = false, CancellationToken token = new())
        {
            var data = await DownloadFile(tprDir, type, hash, compressedSize, token);
            if (!decoded)
                return data;
            else
                return BLTE.Decode(data, decompressedSize);
        }

        public static async Task<byte[]> GetFileFromArchive(string eKey, string tprDir, string archive, int offset, int length, ulong decompressedSize = 0, bool decoded = false, CancellationToken token = new())
        {
            var data = await DownloadFileFromArchive(eKey, tprDir, archive, offset, length, token);
            if (!decoded)
                return data;
            else
                return BLTE.Decode(data, decompressedSize);
        }

        public static async Task<string> GetFilePath(string tprDir, string type, string hash, ulong compressedSize = 0, CancellationToken token = new())
        {
            var cachePath = Path.Combine("cache", tprDir, type, hash);
            if (File.Exists(cachePath))
                return cachePath;

            var data = await DownloadFile(tprDir, type, hash, compressedSize, token);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            FileLocks.TryAdd(cachePath, new Lock());
            lock (FileLocks[cachePath])
                File.WriteAllBytes(cachePath, data);

            return cachePath;
        }

        public static async Task<string> GetDecodedFilePath(string tprDir, string type, string hash, ulong compressedSize = 0, ulong decompressedSize = 0, CancellationToken token = new())
        {
            var cachePath = Path.Combine("cache", tprDir, type, hash + ".decoded");
            if (File.Exists(cachePath))
                return cachePath;

            var data = await DownloadFile(tprDir, type, hash, compressedSize, token);
            var decodedData = BLTE.Decode(data, decompressedSize);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            FileLocks.TryAdd(cachePath, new Lock());
            lock (FileLocks[cachePath])
                File.WriteAllBytes(cachePath, decodedData);

            return cachePath;
        }
    }
}
