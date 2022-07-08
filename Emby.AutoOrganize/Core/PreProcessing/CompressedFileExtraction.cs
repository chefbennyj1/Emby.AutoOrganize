using System;
using System.IO;
using System.Linq;
using Emby.AutoOrganize.Model;
using MediaBrowser.Model.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;


// ReSharper disable TooManyArguments
namespace Emby.AutoOrganize.Core.PreProcessing
{
    
    public class CompressedFileExtraction
    {
        private static long TotalSize                  { get; set; }
        private static IProgress<double> Progress      { get; set; }

        public void BeginCompressedFileExtraction(string fullFileName, string fileName, ILogger log, IProgress<double> progress, AutoOrganizeOptions config)
        {
            Progress = progress;

            var postProcessingFolderPath = config.PreProcessingFolderPath;

            log.Info("File to Decompress: " + fileName);

            string extractPath = Path.Combine(postProcessingFolderPath, Path.GetFileNameWithoutExtension(fileName));

            log.Info("Creating Extraction Path: " + extractPath);

            Directory.CreateDirectory(extractPath);

            var archive = ArchiveFactory.Open(fullFileName);

            log.Info("Archive open: " + fullFileName);

            // Calculate the total extraction size.
            TotalSize = archive.TotalSize;
            log.Info("Archive Total Size: " + TotalSize);

            foreach (IArchiveEntry entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                archive.EntryExtractionEnd += FileMoveSuccess;
                archive.CompressedBytesRead += Archive_CompressedBytesRead;

                entry.WriteToDirectory(extractPath, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
            }
        }

        private static void Archive_CompressedBytesRead(object sender, CompressedBytesReadEventArgs e)
        {
            long compressedBytesRead = e.CompressedBytesRead;
            double compressedPercent = (compressedBytesRead / (double)TotalSize) * 100;
            
            Progress.Report(Math.Round(compressedPercent, 1));
        }

        private static void FileMoveSuccess(object sender, ArchiveExtractionEventArgs<IArchiveEntry> e)
        {
            if (!e.Item.IsComplete) return;
            TotalSize = 0;
        }
    }
}