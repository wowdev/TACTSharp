using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;

using TACTSharp;

namespace TACTBench
{
    [MemoryDiagnoser]
    public class EncodingBenchmark
    {
        private BuildInstance? _build;

        [GlobalSetup]
        public async Task SpecificSetup()
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

            _build = new BuildInstance(Settings.BuildConfig!, Settings.CDNConfig!);
            await _build.Load();
        }

        [Benchmark]
        public RootInstance.Record TestRootLookup()
        {
            ref readonly var fileEntry = ref _build!.Root!.FindFileDataID(1349477);
            Debug.Assert(!Unsafe.IsNullRef(in fileEntry));
            return fileEntry;
        }
    }
}
