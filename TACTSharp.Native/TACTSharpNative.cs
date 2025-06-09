using System.Runtime.InteropServices;

namespace TACTSharp.Native
{
    public static class TACTSharpNative
    {
        private static BuildInstance build = new();

        [UnmanagedCallersOnly(EntryPoint = "SetBaseDir")]
        public static void SetBaseDir(nint wowFolderPtr)
        {
            if (wowFolderPtr == 0)
                throw new ArgumentNullException("WoW folder pointer is null.");

            var wowFolder = Marshal.PtrToStringAnsi(wowFolderPtr);
            if (string.IsNullOrEmpty(wowFolder))
                throw new ArgumentException("WoW folder is empty.");

            build.Settings.BaseDir = wowFolder;
        }

        [UnmanagedCallersOnly(EntryPoint = "SetConfigs")]
        public static void SetConfigs(nint buildConfigPtr, nint cdnConfigPtr)
        {
            if (buildConfigPtr == 0 || cdnConfigPtr == 0)
                throw new ArgumentNullException("BuildConfig or CDNConfig pointer is null.");

            var buildConfig = Marshal.PtrToStringAnsi(buildConfigPtr);
            var cdnConfig = Marshal.PtrToStringAnsi(cdnConfigPtr);
            if (string.IsNullOrEmpty(buildConfig) || string.IsNullOrEmpty(cdnConfig))
                throw new ArgumentException("BuildConfig or CDNConfig is empty.");

            build.LoadConfigs(buildConfig, cdnConfig);
        }

        [UnmanagedCallersOnly(EntryPoint = "Load")]
        public static void Load()
        {
            build.Load();
        }

        [UnmanagedCallersOnly(EntryPoint = "GetBuildString")]
        public static nint GetBuildString()
        {
            if (build == null)
                throw new InvalidOperationException("TACTSharpNative is not initialized. Call Initialize first.");

            var splitName = build.BuildConfig.Values["build-name"][0].Replace("WOW-", "").Split("patch");
            var buildString = splitName[1].Split("_")[0] + "." + splitName[0];

            var buildStringPtr = Marshal.StringToHGlobalAnsi(buildString);
            return buildStringPtr;
        }

        [UnmanagedCallersOnly(EntryPoint = "GetFileByID")]
        public static nint GetFileByID(uint fileDataID)
        {
            if (build == null)
                throw new InvalidOperationException("TACTSharpNative is not initialized. Call Initialize first.");

            if(build.Root == null)
                throw new InvalidOperationException("Build is not loaded. Call Load first.");

            var fileData = build.OpenFileByFDID(fileDataID);
            if (fileData == null)
                throw new FileNotFoundException($"File with ID {fileDataID} not found.");

            var fileDataPtr = Marshal.AllocHGlobal(fileData.Length);
            if (fileDataPtr == IntPtr.Zero)
                throw new OutOfMemoryException("Failed to allocate memory for file data.");

            Marshal.Copy(fileData, 0, fileDataPtr, fileData.Length);

            return fileDataPtr;
        }

        [UnmanagedCallersOnly(EntryPoint = "FileExistsByID")]
        public static bool FileExistsByID(uint fileDataID)
        {
            if (build == null)
                throw new InvalidOperationException("TACTSharpNative is not initialized. Call Initialize first.");

            if (build.Root == null)
                throw new InvalidOperationException("Build is not loaded. Call Load first.");

            return build.Root.FileExists(fileDataID);
        }

        [UnmanagedCallersOnly(EntryPoint = "GetFileSizeByID")]
        public static ulong GetFileSizeByID(uint fileDataID)
        {
            if (build == null)
                throw new InvalidOperationException("TACTSharpNative is not initialized. Call Initialize first.");

            if (build.Root == null || build.Encoding == null)
                throw new InvalidOperationException("Build is not loaded. Call Load first.");

            var rootEntries = build.Root.GetEntriesByFDID(fileDataID);

            if (rootEntries == null || rootEntries.Count == 0)
                throw new FileNotFoundException($"File with ID {fileDataID} not found.");

            var encodingResult = build.Encoding.FindContentKey(rootEntries[0].md5.AsSpan());
            if (encodingResult.Length == 0)
                throw new FileNotFoundException($"File with ID {fileDataID} not found in encoding.");

            return encodingResult.DecodedFileSize;
        }
    }
}
