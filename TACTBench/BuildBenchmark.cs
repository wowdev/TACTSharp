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
            var _build = new BuildInstance();

            var versions = await _build.cdn.GetPatchServiceFile("wow", "versions");
            foreach (var line in versions.Split('\n'))
            {
                if (!line.StartsWith("us|"))
                    continue;

                var splitLine = line.Split('|');

                _build.Settings.BuildConfig ??= splitLine[1];
                _build.Settings.CDNConfig ??= splitLine[2];
                break;
            }

            _build.LoadConfigs(_build.Settings.BuildConfig!, _build.Settings.CDNConfig!);
            _build.Load();
        }

        [Benchmark]
        public async Task TestBuildLoad()
        {
            await LoadBuild();
        }
    }
}
