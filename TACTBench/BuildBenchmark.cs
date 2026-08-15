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

            var versionService = new TACTSharp.VersionServices.Ribbit();
            var version = versionService.GetVersion("wow", "us");
            _build.Settings.BuildConfig = version.BuildConfig;
            _build.Settings.CDNConfig = version.CDNConfig;

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
