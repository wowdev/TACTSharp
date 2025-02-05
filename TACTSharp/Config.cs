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
                foreach (var line in Encoding.UTF8.GetString(cdn.GetFile("wow", "config", path)).Split('\n'))
                {
                    var splitLine = line.Split('=');
                    if (splitLine.Length > 1)
                        Values.Add(splitLine[0].Trim(), splitLine[1].Trim().Split(' '));
                }
            }
            else
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var splitLine = line.Split('=');
                    if (splitLine.Length > 1)
                        Values.Add(splitLine[0].Trim(), splitLine[1].Trim().Split(' '));
                }
            }
        }
    }
}
