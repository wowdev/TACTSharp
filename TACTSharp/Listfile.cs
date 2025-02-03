using System.Diagnostics;

namespace TACTSharp
{
    public static class Listfile
    {
        private static Lock listfileLock = new();
        private static HttpClient client = new();
        private static Dictionary<ulong, uint> nameHashToFDID = new();
        private static Jenkins96 hasher = new();

        public static bool Initialized = false;

        public static void Initialize()
        {
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

        private static void Download()
        {
            if (string.IsNullOrEmpty(Settings.ListfileURL))
                throw new Exception("Listfile URL is not set or empty");

            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Get, Settings.ListfileURL);
                var listfileStream = client.Send(request);

                using (var file = new FileStream("listfile.csv", FileMode.OpenOrCreate, FileAccess.Write))
                    listfileStream.Content.CopyToAsync(file);
            }
        }

        private static void Load()
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

        public static uint GetFDID(string name)
        {
            return nameHashToFDID.TryGetValue(hasher.ComputeHash(name, true), out var fdid) ? fdid : 0;
        }
    }
}
