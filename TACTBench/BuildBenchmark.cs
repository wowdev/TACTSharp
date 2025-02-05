using BenchmarkDotNet.Attributes;

using TACTSharp;

namespace TACTBench
{
    [MemoryDiagnoser]
    public class BuildBenchmark
    {

        [GlobalSetup]
        public async Task EnsureBuildDownloaded()
        {
            await LoadBuild();
        }

        public async Task LoadBuild()
        {
            var versions = await CDN.GetProductVersions("wow");
            foreach (var line in versions.Split('\n'))
            {
                if (!line.StartsWith("us|"))
                    continue;

                var splitLine = line.Split('|');

                Settings.BuildConfig ??= splitLine[1];
                Settings.CDNConfig ??= splitLine[2];
                break;
            }

            var _build = new BuildInstance(Settings.BuildConfig!, Settings.CDNConfig!);
            _build.Load();
        }

        [Benchmark]
        public async Task TestBuildLoad()
        {
            await LoadBuild();
        }
    }
}
