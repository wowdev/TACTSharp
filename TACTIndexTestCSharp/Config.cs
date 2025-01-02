namespace TACTSharp
{
    public class Config
    {
        public Dictionary<string, string[]> Values = [];
        public Config(string path, bool isFile)
        {
            if (!isFile)
            {
                _ = CDN.GetFile("wow", "config", path).Result;
                path = Path.Combine("cache", "wow", "config", path);
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var splitLine = line.Split('=');
                if (splitLine.Length > 1)
                    Values.Add(splitLine[0].Trim(), splitLine[1].Trim().Split(' '));
            }
        }
    }
}
