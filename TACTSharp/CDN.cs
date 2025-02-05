using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace TACTSharp
{
    public class CDN
    {
        private readonly HttpClient Client = new();
        private readonly List<string> CDNServers = [];
        private readonly ConcurrentDictionary<string, Lock> FileLocks = [];
        private readonly Lock cdnLock = new();
        private bool HasLocal = false;
        private readonly Dictionary<byte, CASCIndexInstance> CASCIndexInstances = [];
        private Settings Settings;

        // TODO: Memory mapped cache file access?
        public CDN(Settings settings)
        {
            Settings = settings;
            if (Settings.BaseDir != null)
            {
                try
                {
                    var localTimer = new Stopwatch();
                    localTimer.Start();
                    LoadCASCIndices();
                    localTimer.Stop();
                    Console.WriteLine("Loaded local CASC indices in " + Math.Round(localTimer.Elapsed.TotalMilliseconds) + "ms");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to load CASC indices: " + e.Message);
                }
            }
        }

        public void SetCDNs(string[] cdns)
        {
            foreach (var cdn in cdns)
            {
                if (!CDNServers.Contains(cdn))
                {
                    lock (cdnLock)
                    {
                        CDNServers.Add(cdn);
                    }
                }
            }
        }

        private void LoadCDNs()
        {
            var timer = new Stopwatch();
            timer.Start();
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
                    var ping = new System.Net.NetworkInformation.Ping().Send(server, 400).RoundtripTime;
                    Console.WriteLine("Ping to " + server + ": " + ping + "ms");
                    return (server, ping);
                }));
            }

            var pings = Task.WhenAll(pingTasks).Result;

            CDNServers.AddRange(pings.OrderBy(p => p.ping).Select(p => p.server).ToList());

            timer.Stop();
            Console.WriteLine("Pinged " + CDNServers.Count + " in " + Math.Round(timer.Elapsed.TotalMilliseconds) + "ms, fastest CDNs in order: " + string.Join(", ", CDNServers));
        }
        private void LoadCASCIndices()
        {
            if (Settings.BaseDir != null)
            {
                var dataDir = Path.Combine(Settings.BaseDir, "Data", "data");
                if (Directory.Exists(dataDir))
                {
                    var indexFiles = Directory.GetFiles(dataDir, "*.idx");
                    foreach (var indexFile in indexFiles)
                    {
                        if (indexFile.Contains("tempfile"))
                            continue;

                        var indexBucket = Convert.FromHexString(Path.GetFileNameWithoutExtension(indexFile)[0..2])[0];
                        CASCIndexInstances.TryAdd(indexBucket, new CASCIndexInstance(indexFile));
                    }

                    HasLocal = true;
                }
            }
        }

        public async Task<string> GetProductVersions(string product)
        {
            return await Client.GetStringAsync($"https://{Settings.Region}.version.battle.net/{product}/versions");
        }

        private byte[] DownloadFile(string tprDir, string type, string hash, ulong size = 0, CancellationToken token = new())
        {
            if (HasLocal)
            {
                try
                {
                    if (type == "data" && hash.EndsWith(".index"))
                    {
                        var localIndexPath = Path.Combine(Settings.BaseDir, "Data", "indices", hash);
                        if (File.Exists(localIndexPath))
                            return File.ReadAllBytes(localIndexPath);
                    }
                    else if (type == "config")
                    {
                        var localConfigPath = Path.Combine(Settings.BaseDir, "Data", "config", hash[0] + "" + hash[1], hash[2] + "" + hash[3], hash);
                        if (File.Exists(localConfigPath))
                            return File.ReadAllBytes(localConfigPath);
                    }
                    else
                    {
                        if (TryGetLocalFile(hash, out var data))
                            return data.ToArray();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to read local file: " + e.Message);
                }
            }

            var cachePath = Path.Combine(Settings.CacheDir, tprDir, type, hash);
            FileLocks.TryAdd(cachePath, new Lock());

            if (File.Exists(cachePath))
            {
                if (size > 0 && (ulong)new FileInfo(cachePath).Length != size)
                    File.Delete(cachePath);
                else
                    lock (FileLocks[cachePath])
                        return File.ReadAllBytes(cachePath);
            }

            lock (cdnLock)
            {
                if (CDNServers.Count == 0)
                {
                    LoadCDNs();
                }
            }

            var success = false;
            for (var i = 0; i < CDNServers.Count; i++)
            {
                var url = $"http://{CDNServers[i]}/tpr/{tprDir}/{type}/{hash[0]}{hash[1]}/{hash[2]}{hash[3]}/{hash}";

                Console.WriteLine("Downloading " + url);

                var request = new HttpRequestMessage(HttpMethod.Get, url);

                var response = Client.Send(request, token);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Encountered HTTP " + response.StatusCode + " downloading " + hash + " from " + CDNServers[i]);
                    continue;
                }

                lock (FileLocks[cachePath])
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                    using (var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                    {
                        response.Content.ReadAsStream(token).CopyTo(fileStream);
                    }
                }

                return File.ReadAllBytes(cachePath);
            }

            if (!success)
                throw new FileNotFoundException("Exhausted all CDNs trying to download " + hash);

            return null;
        }

        public unsafe bool TryGetLocalFile(string eKey, out ReadOnlySpan<byte> data)
        {
            var eKeyBytes = Convert.FromHexString(eKey);
            var i = eKeyBytes[0] ^ eKeyBytes[1] ^ eKeyBytes[2] ^ eKeyBytes[3] ^ eKeyBytes[4] ^ eKeyBytes[5] ^ eKeyBytes[6] ^ eKeyBytes[7] ^ eKeyBytes[8];
            var indexBucket = (i & 0xf) ^ (i >> 4);

            var targetIndex = CASCIndexInstances[(byte)indexBucket];
            var (archiveOffset, archiveSize, archiveIndex) = targetIndex.GetIndexInfo(Convert.FromHexString(eKey));
            if (archiveOffset != -1)
            {
                // We will probably want to cache these but battle.net scares me so I'm not going to do it right now
                var archivePath = Path.Combine(Settings.BaseDir, "Data", "data", "data." + archiveIndex.ToString().PadLeft(3, '0'));
                var archiveLength = new FileInfo(archivePath).Length;

                using (var archive = MemoryMappedFile.CreateFromFile(archivePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
                using (var accessor = archive.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                using (var mmapViewHandle = accessor.SafeMemoryMappedViewHandle)
                {
                    byte* ptr = null;
                    try
                    {
                        mmapViewHandle.AcquirePointer(ref ptr);

                        if (archiveOffset + archiveSize > archiveLength)
                        {
                            Console.WriteLine("Skipping local file read: " + archiveOffset + " + " + archiveSize + " > " + archiveLength + " for archive " + "data." + archiveIndex.ToString().PadLeft(3, '0'));
                            data = null;
                            return false;
                        }

                        data = new ReadOnlySpan<byte>(ptr + archiveOffset, archiveSize).ToArray();
                        return true;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to read local file: " + e.Message);
                        data = null;
                        return false;
                    }
                    finally
                    {
                        mmapViewHandle.ReleasePointer();
                    }
                }
            }
            data = null;
            return false;
        }

        private byte[] DownloadFileFromArchive(string eKey, string tprDir, string archive, int offset, int size, CancellationToken token = new())
        {
            if (HasLocal)
            {
                try
                {
                    if (TryGetLocalFile(eKey, out var data))
                        return data.ToArray();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to read local file: " + e.Message);
                }
            }

            var cachePath = Path.Combine(Settings.CacheDir, tprDir, "data", eKey);
            FileLocks.TryAdd(cachePath, new Lock());

            if (File.Exists(cachePath))
            {
                if (new FileInfo(cachePath).Length == size)
                    lock (FileLocks[cachePath])
                        return File.ReadAllBytes(cachePath);
                else
                    File.Delete(cachePath);
            }

            lock (cdnLock)
            {
                if (CDNServers.Count == 0)
                {
                    LoadCDNs();
                }
            }

            var success = false;
            for (var i = 0; i < CDNServers.Count; i++)
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

                var response = Client.Send(request, token);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Encountered HTTP " + response.StatusCode + " downloading " + eKey + " (archive " + archive + ") from " + CDNServers[i]);
                    continue;
                }

                lock (FileLocks[cachePath])
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                    using (var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                    {
                        response.Content.ReadAsStream(token).CopyTo(fileStream);
                    }
                }

                return File.ReadAllBytes(cachePath);
            }

            if (!success)
                throw new FileNotFoundException("Exhausted all CDNs trying to download " + eKey + " (archive " + archive + ")");

            return null;
        }

        public byte[] GetFile(string tprDir, string type, string hash, ulong compressedSize = 0, ulong decompressedSize = 0, bool decoded = false, CancellationToken token = new())
        {
            var data = DownloadFile(tprDir, type, hash, compressedSize, token);
            if (!decoded)
                return data;
            else
                return BLTE.Decode(data, decompressedSize);
        }

        public byte[] GetFileFromArchive(string eKey, string tprDir, string archive, int offset, int length, ulong decompressedSize = 0, bool decoded = false, CancellationToken token = new())
        {
            var data = DownloadFileFromArchive(eKey, tprDir, archive, offset, length, token);
            if (!decoded)
                return data;
            else
                return BLTE.Decode(data, decompressedSize);
        }

        public string GetFilePath(string tprDir, string type, string hash, ulong compressedSize = 0, CancellationToken token = new())
        {
            var cachePath = Path.Combine(Settings.CacheDir, tprDir, type, hash);
            if (File.Exists(cachePath))
                return cachePath;

            var data = DownloadFile(tprDir, type, hash, compressedSize, token);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            FileLocks.TryAdd(cachePath, new Lock());
            lock (FileLocks[cachePath])
                File.WriteAllBytes(cachePath, data);

            return cachePath;
        }

        public string GetDecodedFilePath(string tprDir, string type, string hash, ulong compressedSize = 0, ulong decompressedSize = 0, CancellationToken token = new())
        {
            var cachePath = Path.Combine(Settings.CacheDir, tprDir, type, hash + ".decoded");
            if (File.Exists(cachePath))
                return cachePath;

            var data = DownloadFile(tprDir, type, hash, compressedSize, token);
            var decodedData = BLTE.Decode(data, decompressedSize);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            FileLocks.TryAdd(cachePath, new Lock());
            lock (FileLocks[cachePath])
                File.WriteAllBytes(cachePath, decodedData);

            return cachePath;
        }
    }
}
