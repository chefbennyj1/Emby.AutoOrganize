using System;
using System.IO;
using Emby.AutoOrganize.Model;

namespace Emby.AutoOrganize.Core.PreProcessing
{
    
    public class FileCopy
    {
        //private static IProgress<double> Progress { get; set; }
        public delegate void ProgressChangedEventArgs(double percentage);

        public delegate void ProgressCompleteEventArgs();

        public void BeginFileCopyExtraction(string fileFullName, string fileName, AutoOrganizeOptions config)
        {
            
            //Progress = progress;

            var postProcessingFolderPath = config.PreProcessingFolderPath;

            var key         = Path.GetFileNameWithoutExtension(fileName);
            var extractPath = Path.Combine(postProcessingFolderPath, key);
            
            var source      = new FileInfo(fileName: fileFullName);
            var destination = new FileInfo(Path.Combine(extractPath, fileName));

            if (destination.Exists) destination.Delete();

            Directory.CreateDirectory(extractPath);
            
            CopyFile(source, destination);
        }


        //Copilot: write a method that copies a file using FileStream, and contain the ability to calculate the progress percentage in an event
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

        public event ProgressChangedEventArgs OnProgressChanged;
        public event ProgressCompleteEventArgs OnComplete;
    }

   
}