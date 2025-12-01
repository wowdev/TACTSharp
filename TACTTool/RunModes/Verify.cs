using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using TACTSharp;

namespace TACTTool.RunModes
{
    public static class Verify
    {
        public static void Run(BuildInstance buildInstance)
        {
            Console.WriteLine("Starting TACT CDN directory verification...");
            Console.WriteLine("!!!");
            Console.WriteLine("NOTE: This is currently a very basic implementation that only checks archive/loose file existence & sizes.");
            Console.WriteLine("!!!");

            if (buildInstance.Settings.CDNConfig is null)
            {
                Console.WriteLine("!!! Warning, no CDN config given so will check entire directory. This may take a long time and may fail on SMB mounted shares.");
                Console.WriteLine("Also, this is not yet implemented.");
                throw new NotImplementedException();
            }
            else
            {
                var config = new TACTSharp.Config(buildInstance.cdn, buildInstance.Settings.CDNConfig, false);

                var archiveCount = config.Values["archives"].Length;
                for (var i = 0; i < archiveCount; i++)
                {
                    var archiveIndex = config.Values["archives"][i];

                    var indexPath = Path.Combine(buildInstance.Settings.CDNDir, "tpr", "wow", "data", archiveIndex[0..2], archiveIndex[2..4], archiveIndex + ".index");
                    if (!File.Exists(indexPath))
                        throw new FileNotFoundException(indexPath);

                    var index = new IndexInstance(indexPath);

                    var allFiles = index.GetAllEntries();
                    var highestOffset = allFiles.Select(x => x.offset + x.size).Max();

                    var archiveFileInfo = new FileInfo(indexPath.Replace(".index", ""));
                    if (!archiveFileInfo.Exists)
                        throw new FileNotFoundException(archiveFileInfo.FullName);
                    var archiveLength = archiveFileInfo.Length;

                    if (highestOffset != archiveLength)
                    {
                        Console.WriteLine($"!!! Archive {archiveIndex} has wrong size! Expected {highestOffset} bytes but only found {archiveLength} bytes.");
                    }

                    Console.Write("Checking archives.. " + (i + 1) + "/" + archiveCount + "\r");
                }

                Console.WriteLine();

                if (config.Values.TryGetValue("file-index", out string[]? fileIndexName))
                {
                    var fileIndexPath = Path.Combine(buildInstance.Settings.CDNDir, "tpr", "wow", "data", fileIndexName[0][0..2], fileIndexName[0][2..4], fileIndexName[0] + ".index");
                    var fileIndex = new IndexInstance(fileIndexPath);
                    var allFiles = fileIndex.GetAllEntries();
                    var looseFileCount = allFiles.Count;
                    for(var i = 0; i < looseFileCount; i++)
                    {
                        Console.Write("Checking loose files.. " + (i + 1) + "/" + looseFileCount + "\r");
                        var looseFile = allFiles[i];
                        var looseFileName = Convert.ToHexStringLower(looseFile.eKey);
                        var looseFilePath = Path.Combine(buildInstance.Settings.CDNDir, "tpr", "wow", "data", looseFileName[0..2], looseFileName[2..4], looseFileName);
                        var looseFileInfo = new FileInfo(looseFilePath);
                        if (!looseFileInfo.Exists)
                            throw new FileNotFoundException(looseFilePath);

                        var looseFileSize = looseFileInfo.Length;
                        var looseFileSupposedSize = looseFile.size;

                        //var looseFileMD5 = Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(looseFilePath)));
                        //if(looseFileMD5 != looseFileName)
                        //{
                        //    Console.WriteLine($"!!! MD5 for file {looseFileName} is incorrect ({looseFileName} != {looseFileMD5}!");
                        //}

                        if (looseFileSize != looseFileSupposedSize)
                        {
                            Console.WriteLine($"!!! Loose file {looseFileName} has wrong size! Expected {looseFileSupposedSize} bytes but only found {looseFileSize} bytes.");
                        }
                    }
                }

                Console.WriteLine();
            }
        }
    }
}
