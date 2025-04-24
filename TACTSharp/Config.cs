using System.Text;

namespace TACTSharp
{
    public class Config
    {
        public Dictionary<string, string[]> Values = [];
        public Config(CDN cdn, string path, bool isFile)
        {
            if (!isFile)
            {
                var file = Encoding.UTF8.GetString(cdn.GetFile("config", path));
                if (file[0] != '#')
                    throw new IOException("Config file is unreadable");

                foreach (var line in file.Split('\n'))
                {
                    var splitLine = line.Split('=');
                    if (splitLine.Length > 1)
                        Values.Add(splitLine[0].Trim(), splitLine[1].Trim().Split(' '));
                }
            }
            else
            {
                var file = File.ReadAllLines(path);
                if (file[0][0] != '#')
                    throw new IOException("Config file is unreadable");

                foreach (var line in file)
                {
                    var splitLine = line.Split('=');
                    if (splitLine.Length > 1)
                        Values.Add(splitLine[0].Trim(), splitLine[1].Trim().Split(' '));
                }
            }
        }
    }
}
