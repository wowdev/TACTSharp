using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace TACTSharp
{
    public class CDN
    {
        private readonly HttpClient Client = new();
        private List<string> CDNServers = [];
        private readonly ConcurrentDictionary<string, Lock> FileLocks = [];
        private readonly Lock cdnLock = new();
        private bool HasLocal = false;
        private readonly Dictionary<byte, CASCIndexInstance> CASCIndexInstances = [];
        private Settings Settings;

        // TODO: The implementation around this needs improving. For local installations, this comes from .build.info. For remote this is set by the first CDN server it retrieves.
        // However, if ProductDirectory is accessed before the first CDN is loaded (or if not set through .build.info loading) it'll be null.
        public string ProductDirectory = string.Empty;

        public string ArmadilloKeyName = string.Empty;

        // TODO: Memory mapped cache file access?
        public CDN(Settings settings)
        {
            Settings = settings;
        }

        public void OpenLocal()
        {
            if (string.IsNullOrEmpty(Settings.BaseDir))
                return;

            if (CASCIndexInstances.Count > 0)
                return;

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

            var cdnsFile = GetPatchServiceFile(Settings.Product, "cdns").Result;

            foreach (var line in cdnsFile.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (!line.StartsWith(Settings.Region + "|"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 2)
                    continue;

                if (string.IsNullOrEmpty(ProductDirectory))
                    ProductDirectory = splitLine[1];

                CDNServers.AddRange(splitLine[2].Trim().Split(' '));
            }

            CDNServers.AddRange(Settings.AdditionalCDNs);

            var pingTasks = new List<Task<(string server, long ping)>>();
            foreach (var server in CDNServers.Distinct())
            {
                pingTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var ping = new System.Net.NetworkInformation.Ping().Send(server, 400).RoundtripTime;
                        Console.WriteLine("Ping to " + server + ": " + ping + "ms");
                        return (server, ping);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to ping CDN " + server + ": " + (e.InnerException != null ? e.InnerException.Message : e.Message));
                        return (server, 99999);
                    }
                }));
            }

            var pings = Task.WhenAll(pingTasks).Result;

            CDNServers = [.. pings.OrderBy(p => p.ping).Where(p => p.ping != 99999).Select(p => p.server)];

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
                    if (CASCIndexInstances.Count > 0)
                        return;

                    var indexFiles = Directory.GetFiles(dataDir, "*.idx");
                    var highestIndexPerBucket = new Dictionary<byte, int>(16);

                    foreach (var indexFile in indexFiles)
                    {
                        if (indexFile.Contains("tempfile"))
                            continue;

                        var indexBucket = Convert.ToByte(Path.GetFileNameWithoutExtension(indexFile)[0..2], 16);
                        var indexVersion = Convert.ToInt32(Path.GetFileNameWithoutExtension(indexFile)[2..], 16);

                        if (highestIndexPerBucket.TryGetValue(indexBucket, out var highestIndex))
                        {
                            if (indexVersion > highestIndex)
                                highestIndexPerBucket[indexBucket] = indexVersion;
                        }
                        else
                        {
                            highestIndexPerBucket.Add(indexBucket, indexVersion);
                        }
                    }

                    foreach (var index in highestIndexPerBucket)
                    {
                        var indexFile = Path.Combine(dataDir, index.Key.ToString("x2") + index.Value.ToString("x2").PadLeft(8, '0') + ".idx");
                        CASCIndexInstances.Add(index.Key, new CASCIndexInstance(indexFile));
                    }

                    HasLocal = true;
                }
            }
        }

        public async Task<string> GetPatchServiceFile(string product, string file = "versions")
        {
            return await Client.GetStringAsync($"https://{Settings.Region}.version.battle.net/v2/products/{product}/{file}");
        }

        private byte[] DownloadFile(string type, string hash, ulong size = 0, CancellationToken token = new())
        {
            if (HasLocal)
            {
                try
                {
                    if (type == "data" && hash.EndsWith(".index"))
                    {
                        var localIndexPath = Path.Combine(Settings.BaseDir!, "Data", "indices", hash);
                        if (File.Exists(localIndexPath))
                            return File.ReadAllBytes(localIndexPath);
                    }
                    else if (type == "config")
                    {
                        var localConfigPath = Path.Combine(Settings.BaseDir!, "Data", "config", hash[0] + "" + hash[1], hash[2] + "" + hash[3], hash);
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

            lock (cdnLock)
            {
                if (CDNServers.Count == 0)
                {
                    LoadCDNs();
                }
            }

            var cachePath = Path.Combine(Settings.CacheDir, ProductDirectory, type, hash);
            FileLocks.TryAdd(cachePath, new Lock());

            if (File.Exists(cachePath))
            {
                if (size > 0 && (ulong)new FileInfo(cachePath).Length != size)
                    File.Delete(cachePath);
                else
                    lock (FileLocks[cachePath])
                        return File.ReadAllBytes(cachePath);
            }

            if (!string.IsNullOrEmpty(Settings.CDNDir))
            {
                // TODO: How do we handle encrypted local CDN copies?

                var cdnPath = Path.Combine(Settings.CDNDir, ProductDirectory, type, $"{hash[0]}{hash[1]}", $"{hash[2]}{hash[3]}", hash);
                FileLocks.TryAdd(cdnPath, new Lock());

                if (File.Exists(cdnPath))
                {
                    if (size > 0 && (ulong)new FileInfo(cdnPath).Length != size)
                        Console.WriteLine("Warning! Found " + hash + " in CDN dir but size does not match! " + size + " != " + new FileInfo(cachePath).Length + ", continuing to download.");
                    else
                        lock (FileLocks[cdnPath])
                            return File.ReadAllBytes(cdnPath);
                }
            }

            if (!Settings.TryCDN)
                throw new FileNotFoundException();

            for (var i = 0; i < CDNServers.Count; i++)
            {
                var url = $"http://{CDNServers[i]}/{ProductDirectory}/{type}/{hash[0]}{hash[1]}/{hash[2]}{hash[3]}/{hash}";

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

                    try
                    {
                        using (var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                        {
                            if (string.IsNullOrEmpty(ArmadilloKeyName))
                            {
                                response.Content.ReadAsStream(token).CopyTo(fileStream);
                            }
                            else
                            {
                                using (var ms = new MemoryStream())
                                {
                                    response.Content.ReadAsStream(token).CopyTo(ms);
                                    ms.Position = 0;
                                    if (!BLTE.TryDecryptArmadillo(hash, ArmadilloKeyName, ms.ToArray(), out var output))
                                    {
                                        Console.WriteLine("Failed to decrypt file " + hash + " downloaded from " + CDNServers[i]);
                                        File.Delete(cachePath);
                                        continue;
                                    }
                                    fileStream.Write(output);
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to download file: " + e.Message);
                        File.Delete(cachePath);
                        continue;
                    }
                }

                return File.ReadAllBytes(cachePath);
            }

            throw new FileNotFoundException("Exhausted all CDNs trying to download " + hash);
        }

        public unsafe bool TryGetLocalFile(string eKey, out ReadOnlySpan<byte> data)
        {
            if (string.IsNullOrEmpty(Settings.BaseDir))
                throw new DirectoryNotFoundException("Base directory not set");

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

        private byte[] DownloadFileFromArchive(string eKey, string archive, int offset, int size, CancellationToken token = new())
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

            var cachePath = Path.Combine(Settings.CacheDir, ProductDirectory, "data", eKey);
            FileLocks.TryAdd(cachePath, new Lock());

            if (File.Exists(cachePath))
            {
                if (new FileInfo(cachePath).Length == size)
                    lock (FileLocks[cachePath])
                        return File.ReadAllBytes(cachePath);
                else
                    File.Delete(cachePath);
            }

            if (!string.IsNullOrEmpty(Settings.CDNDir))
            {
                // TODO: How do we handle encrypted local CDN copies?

                var cdnPath = Path.Combine(Settings.CDNDir, ProductDirectory, "data", $"{archive[0]}{archive[1]}", $"{archive[2]}{archive[3]}", archive);
                FileLocks.TryAdd(cdnPath, new Lock());
                if (File.Exists(cdnPath))
                {
                    if (new FileInfo(cdnPath).Length < (offset + size))
                        Console.WriteLine("Warning! Found " + archive + " in CDN dir but size is lower than offset+size " + offset + size + " != " + new FileInfo(cdnPath).Length + ", continuing to download.");
                    else
                    {
                        lock (FileLocks[cdnPath])
                        {
                            using (var fs = new FileStream(cdnPath, FileMode.Open, FileAccess.Read))
                            {
                                var buffer = new byte[size];
                                fs.Seek(offset, SeekOrigin.Begin);
                                fs.ReadExactly(buffer);
                                return buffer;
                            }
                        }
                    }
                }
            }

            if (!Settings.TryCDN)
                throw new FileNotFoundException();

            lock (cdnLock)
            {
                if (CDNServers.Count == 0)
                {
                    LoadCDNs();
                }
            }

            for (var i = 0; i < CDNServers.Count; i++)
            {
                var url = $"http://{CDNServers[i]}/{ProductDirectory}/data/{archive[0]}{archive[1]}/{archive[2]}{archive[3]}/{archive}";

                Console.WriteLine("Downloading file " + eKey + " from archive " + archive + " at offset " + offset + " with size " + size + " from " + CDNServers[i]);

                var request = new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Headers =
                    {
                        Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + size - 1)
                    }
                };

                try
                {
                    var response = Client.Send(request, token);

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Encountered HTTP " + response.StatusCode + " downloading " + eKey + " (archive " + archive + ") from " + CDNServers[i]);
                        continue;
                    }

                    lock (FileLocks[cachePath])
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                        try
                        {
                            using (var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                            {
                                if (string.IsNullOrEmpty(ArmadilloKeyName))
                                {
                                    response.Content.ReadAsStream(token).CopyTo(fileStream);
                                }
                                else
                                {
                                    using (var ms = new MemoryStream())
                                    {
                                        response.Content.ReadAsStream(token).CopyTo(ms);
                                        ms.Position = 0;
                                        if (!BLTE.TryDecryptArmadillo(archive, ArmadilloKeyName, ms.ToArray(), out var output, offset))
                                        {
                                            Console.WriteLine("Failed to decrypt file " + eKey + " from archive " + archive + " downloaded from " + CDNServers[i]);
                                            File.Delete(cachePath);
                                            continue;
                                        }
                                        fileStream.Write(output);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Failed to download file: " + ex.Message);
                            File.Delete(cachePath);
                            continue;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Encountered exception " + e.Message + " downloading " + eKey + " (archive " + archive + ") from " + CDNServers[i]);
                    continue;
                }

                return File.ReadAllBytes(cachePath);
            }

            throw new FileNotFoundException("Exhausted all CDNs trying to download " + eKey + " (archive " + archive + ")");
        }

        public byte[] GetFile(string type, string hash, ulong compressedSize = 0, ulong decompressedSize = 0, bool decoded = false, CancellationToken token = new())
        {
            var data = DownloadFile(type, hash, compressedSize, token);
            if (!decoded)
                return data;
            else
                return BLTE.Decode(data, decompressedSize);
        }

        public byte[] GetFileFromArchive(string eKey, string archive, int offset, int length, ulong decompressedSize = 0, bool decoded = false, CancellationToken token = new())
        {
            var data = DownloadFileFromArchive(eKey, archive, offset, length, token);
            if (!decoded)
                return data;
            else
                return BLTE.Decode(data, decompressedSize);
        }

        public string GetFilePath(string type, string hash, ulong compressedSize = 0, CancellationToken token = new())
        {
            var cachePath = Path.Combine(Settings.CacheDir, ProductDirectory, type, hash);
            if (File.Exists(cachePath))
                return cachePath;

            var data = DownloadFile(type, hash, compressedSize, token);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            FileLocks.TryAdd(cachePath, new Lock());
            lock (FileLocks[cachePath])
                File.WriteAllBytes(cachePath, data);

            return cachePath;
        }

        public string GetDecodedFilePath(string type, string hash, ulong compressedSize = 0, ulong decompressedSize = 0, CancellationToken token = new())
        {
            var cachePath = Path.Combine(Settings.CacheDir, ProductDirectory, type, hash + ".decoded");
            if (File.Exists(cachePath))
                return cachePath;

            var data = DownloadFile(type, hash, compressedSize, token);
            var decodedData = BLTE.Decode(data, decompressedSize);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            FileLocks.TryAdd(cachePath, new Lock());
            lock (FileLocks[cachePath])
                File.WriteAllBytes(cachePath, decodedData);

            return cachePath;
        }

        public string GetProductConfig(string hash, CancellationToken token = new())
        {
            lock (cdnLock)
            {
                if (CDNServers.Count == 0)
                {
                    LoadCDNs();
                }
            }

            var cachePath = Path.Combine(Settings.CacheDir, "tpr/configs/data", hash);
            FileLocks.TryAdd(cachePath, new Lock());

            if (File.Exists(cachePath))
                return File.ReadAllText(cachePath);

            for (var i = 0; i < CDNServers.Count; i++)
            {
                var url = $"http://{CDNServers[i]}/tpr/configs/data/{hash[0]}{hash[1]}/{hash[2]}{hash[3]}/{hash}";

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

                    try
                    {
                        using (var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                            response.Content.ReadAsStream(token).CopyTo(fileStream);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to download file: " + e.Message);
                        File.Delete(cachePath);
                        continue;
                    }
                }

                return File.ReadAllText(cachePath);
            }

            throw new FileNotFoundException("Exhausted all CDNs trying to download " + hash);
        }
    }
}
