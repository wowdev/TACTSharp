using System.Diagnostics;

namespace TACTSharp
{
    public class Listfile
    {
        private Lock listfileLock = new();
        private HttpClient client = new();
        private Dictionary<ulong, uint> nameHashToFDID = new();
        private Jenkins96 hasher = new();

        private CDN CDN;
        private Settings Settings;

        public bool Initialized = false;

        public void Initialize(CDN cdn, Settings settings)
        {
            CDN = cdn;
            Settings = settings;

            lock (listfileLock)
            {
                if (Initialized)
                    return;

                var fileInfo = new FileInfo("listfile.csv");
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

                if (File.Exists("listfile.csv"))
                    File.Delete("listfile.csv");

                using (var file = new FileStream("listfile.csv", FileMode.OpenOrCreate, FileAccess.Write))
                    listfileResponse.Content.ReadAsStream().CopyTo(file);
            }
        }

        private void Load()
        {
            var sw = new Stopwatch();
            sw.Start();
            using (var file = new StreamReader("listfile.csv"))
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

        public uint GetFDID(string name)
        {
            return nameHashToFDID.TryGetValue(hasher.ComputeHash(name, true), out var fdid) ? fdid : 0;
        }
    }
}
