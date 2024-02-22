using System;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;


namespace Emby.AutoOrganize.Core.PreProcessing
{
    public class FileExtraction
    {
        private static long TotalSize                  { get; set; }
        
        public delegate void ProgressChangedEventArgs(double percentage);

        public delegate void ProgressCompleteEventArgs();

        public void CompressedFileExtraction(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath))
            {
                return;
            }

            if (string.IsNullOrEmpty(destinationPath)) return;
            // ReSharper disable once AssignNullToNotNullAttribute <== it will not be null or empty here
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

            var archive = ArchiveFactory.Open(sourcePath);
            
            // Calculate the total extraction size.
            TotalSize = archive.TotalSize;
            foreach (IArchiveEntry entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                archive.EntryExtractionEnd += FileMoveSuccess;
                archive.CompressedBytesRead += Archive_CompressedBytesRead;
                try
                {
                    entry.WriteToDirectory(Path.GetDirectoryName(destinationPath), new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
                catch(ExtractionException)
                {
                    if (Directory.Exists(destinationPath))
                    {
                        Directory.Delete(destinationPath);
                    }
                    archive.Dispose();
                    return;
                }
                
            }
            
        }

        public void CopyFileExtraction(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath)) return;
           

            var source      = new FileInfo(fileName: sourcePath);
            var destination = new FileInfo(destinationPath);

            if (destination.Exists) destination.Delete();

            // ReSharper disable once AssignNullToNotNullAttribute <== it will not be null or empty here
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            
            CopyFile(source, destination);
        }

        private void CopyFile(FileInfo sourceFile, FileInfo destinationFile)
        {
            byte[] buffer = new byte[1024 * 1024]; // 1MB buffer

            var source = new FileStream(sourceFile.FullName, FileMode.Open, FileAccess.Read);
            
            long fileLength = source.Length;

            //Copilot: 
            //Create the destination file stream
            //Open it for writing
            //Don't allow any other application to access the file until it has finished writing and is closed.
            //This will allow the Auto Organize to mark the file as "InUse" while copying.
            var dest = new FileStream(destinationFile.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            long totalBytes = 0;
            int currentBlockSize;

            while ((currentBlockSize = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalBytes += currentBlockSize;
                double percentage = totalBytes * 100.0 / fileLength;

                dest.Write(buffer, 0, currentBlockSize);

                if (OnProgressChanged != null) OnProgressChanged(percentage);
            }

            source.Close();
            dest.Close();

            if (OnComplete != null) OnComplete();
        }

        private void Archive_CompressedBytesRead(object sender, CompressedBytesReadEventArgs e)
        {
            long compressedBytesRead = e.CompressedBytesRead;
            double compressedPercent = (compressedBytesRead / (double)TotalSize) * 100;

            if(OnProgressChanged != null) OnProgressChanged(Math.Round(compressedPercent, 1));
            //Progress.Report(Math.Round(compressedPercent, 1));
        }

        private void FileMoveSuccess(object sender, ArchiveExtractionEventArgs<IArchiveEntry> e)
        {
            if (!e.Item.IsComplete) return;
            TotalSize = 0;
            if (OnComplete != null) OnComplete();
        }
        

        public event ProgressChangedEventArgs OnProgressChanged;
        public event ProgressCompleteEventArgs OnComplete;
    }
}