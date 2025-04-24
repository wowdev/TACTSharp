using System.Diagnostics;

namespace TACTSharp
{
    public class Listfile
    {
        private Lock listfileLock = new();
        private HttpClient client = new();
        private Dictionary<ulong, uint> nameHashToFDID = new();
        private Dictionary<uint, string> fdidToName = new();
        private Jenkins96 hasher = new();

        private CDN CDN;
        private Settings Settings;
        private string ListfilePath = "listfile.csv";

        public bool Initialized = false;

        public void Initialize(CDN cdn, Settings settings, string path = "listfile.csv", bool useExisting = false)
        {
            CDN = cdn;
            Settings = settings;
            ListfilePath = path;

            lock (listfileLock)
            {
                if(useExisting && File.Exists(path))
                {
                    Load();
                    Initialized = true;
                    return;
                }

                if (Initialized)
                    return;

                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists)
                {
                    Console.WriteLine("Downloading listfile.csv");
                    Download();
                }
                else
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Head, Settings.ListfileURL);
                        var response = client.Send(request);
                        if (response.Content.Headers.LastModified > fileInfo.LastWriteTimeUtc)
                        {
                            Console.WriteLine("Downloading listfile.csv as it is outdated (server last modified " + response.Content.Headers.LastModified + " > client last write " + fileInfo.LastWriteTimeUtc + ")");
                            Download();
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to check if listfile.csv is outdated: " + e.Message + ", downloading regardless..");
                        Download();
                    }
                }

                Load();

                Initialized = true;
            }
        }

        private void Download()
        {
            if (string.IsNullOrEmpty(Settings.ListfileURL))
                throw new Exception("Listfile URL is not set or empty");

            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Get, Settings.ListfileURL);
                var listfileResponse = client.Send(request);

                if (!listfileResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("Failed to download listfile: HTTP " + listfileResponse.StatusCode);
                    return;
                }

                if (File.Exists(ListfilePath))
                    File.Delete(ListfilePath);

                using (var file = new FileStream(ListfilePath, FileMode.OpenOrCreate, FileAccess.Write))
                    listfileResponse.Content.ReadAsStream().CopyTo(file);
            }
        }

        private void Load()
        {
            var sw = new Stopwatch();
            sw.Start();
            using (var file = new StreamReader(ListfilePath))
            {
                while (!file.EndOfStream)
                {
                    var line = file.ReadLine();
                    if (line == null)
                        continue;

                    var parts = line.Split(';');
                    if (parts.Length < 2)
                        continue;

                    if (uint.TryParse(parts[0], out var fdid))
                        nameHashToFDID[hasher.ComputeHash(parts[1], true)] = fdid;
                }
            }

            sw.Stop();

            Console.WriteLine("Loaded " + nameHashToFDID.Count + " listfile entries in " + sw.Elapsed.TotalMilliseconds + "ms");
        }

        private void LoadFilenames()
        {
            var sw = new Stopwatch();
            sw.Start();
            using (var file = new StreamReader(ListfilePath))
            {
                while (!file.EndOfStream)
                {
                    var line = file.ReadLine();
                    if (line == null)
                        continue;

                    var parts = line.Split(';');
                    if (parts.Length < 2)
                        continue;

                    if (uint.TryParse(parts[0], out var fdid))
                        fdidToName[fdid] = parts[1];
                }
            }

            sw.Stop();

            Console.WriteLine("Loaded " + fdidToName.Count + " listfile filename entries in " + sw.Elapsed.TotalMilliseconds + "ms");
        }

        public uint GetFDID(string name)
        {
            return nameHashToFDID.TryGetValue(hasher.ComputeHash(name, true), out var fdid) ? fdid : 0;
        }

        public void SetListfileEntry(uint fdid, string name)
        {
            nameHashToFDID[hasher.ComputeHash(name, true)] = fdid;
            fdidToName[fdid] = name;
        }

        public string? GetFilename(uint fdid)
        {
            if (fdidToName.Count == 0)
                LoadFilenames();

            if (fdidToName.TryGetValue(fdid, out var name))
                return name;
            else
                return null;
        }
    }
}
