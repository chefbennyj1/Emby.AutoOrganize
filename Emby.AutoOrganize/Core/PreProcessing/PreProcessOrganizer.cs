using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Naming.Common;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Emby.AutoOrganize.Core.PreProcessing
{
    public class PreProcessOrganizer
    {
        private ILogger Logger { get; }
        private IFileSystem FileSystem { get; }

        public PreProcessOrganizer(IFileSystem file, ILogger log)
        {
            FileSystem = file;
            Logger = log;
        }
        public async void Organize(IProgress<double> progress, List<string> watchLocations, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {

            if (!File.Exists(options.PreProcessingFolderPath))
            {
                Logger.Info("Pre-processing folder does not exist. Creating pre-processing folder.");

                try
                {
                    Directory.CreateDirectory(options.PreProcessingFolderPath);
                }
                catch (Exception)
                {
                    Logger.Error("Error creating pre-processing folder.");
                    return;
                }

            }

            foreach (var watchedFolder in watchLocations)
            {

                //Recording files may have been saved in a 'completed' directory, but do not have a parent folder.
                //We'll need a parent folder to mark the file as extracted.
                //Move the recording into a parent folder.
                //There will be more then one copy of the recorded file in the monitored folder.
                //Warn the user about this. 

                foreach (var sourceFile in Directory.GetFiles(watchedFolder))
                {
                    var sourceFileInfo = FileSystem.GetFileInfo(sourceFile);
                    var parentFolderPath = Path.Combine(watchedFolder, Path.GetFileNameWithoutExtension(sourceFileInfo.Name));

                    if (Directory.Exists(parentFolderPath)) continue; //<-The file is in a parent folder

                    Directory.CreateDirectory(parentFolderPath);

                    Logger.Info($"Parent Folder created: {parentFolderPath}");
                    try
                    {
                        Logger.Info($"Copying file {sourceFileInfo.FullName} to parent folder {parentFolderPath}");
                        if (sourceFileInfo.Name != null) File.Copy(sourceFileInfo.FullName, Path.Combine(parentFolderPath, sourceFileInfo.Name));
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"Unable to copy file into parent folder: {parentFolderPath}\n {ex.Message}");
                    }
                }


                //Now all the files have parent folders we'll continue to process the files.

                var monitoredDirectoryInfo = FileSystem.GetDirectories(path: watchedFolder);

                var monitoredDirectoryContents = monitoredDirectoryInfo.ToList();

                Logger.Info("Found: " + monitoredDirectoryContents.Count() + " total folders in " + watchedFolder);

                var foldersToProcess = monitoredDirectoryContents.Where(folder => !FileSystem.FileExists(Path.Combine(folder.FullName, "####emby.extracted####"))).ToList();

                Logger.Info("Found: " + foldersToProcess.Count() + " folders to process " + watchedFolder);

                foreach (var folder in monitoredDirectoryContents)
                {
                    //Exaction file is created after the file has been extracted
                    var extractionMarker = Path.Combine(folder.FullName, "####emby.extracted####");

                    //Ignore this folder if there is an 'extraction marker' file present. The contents of this folder have already been extracted for sorting.
                    if (FileSystem.FileExists(extractionMarker)) continue;

                    //The following folder contents will be extracted.
                    Logger.Info("New media found for extraction: " + folder.FullName);

                    IEnumerable<FileSystemMetadata> eligibleFiles;
                    try
                    {
                        eligibleFiles = FileSystem.GetFiles(folder.FullName);
                    }
                    catch (IOException) //The files are in use, get it next time.
                    {
                        continue;
                    }



                    foreach (var file in eligibleFiles)
                    {
                        //Ignore Sample files
                        if (file.FullName.IndexOf("sample", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                        var preProcessingFolderPath = options.PreProcessingFolderPath;

                        string source = file.FullName;
                        string destination = Path.Combine(preProcessingFolderPath, Path.GetFileNameWithoutExtension(file.Name), file.Name);

                        Logger.Info($"Creating destination file: {destination}");

                        //If the file is compressed, extract it.
                        if (file.Extension == ".rar")
                        {
                            var fileExtraction = new FileExtraction();
                            fileExtraction.OnProgressChanged += percentage => progress.Report(percentage);
                            fileExtraction.OnComplete += () => { progress.Report(100.0); };
                            Logger.Info("New compressed media file ready for extraction: " + file.Name);
                            fileExtraction.CompressedFileExtraction(source, destination);
                            continue;
                        }

                        //If the file is a video, extract it.
                        var namingOptions = new NamingOptions();
                        if (namingOptions.VideoFileExtensions.Contains(file.Extension))
                        {
                            var fileExtraction = new FileExtraction();
                            fileExtraction.OnProgressChanged += percentage => progress.Report(percentage);
                            fileExtraction.OnComplete += () => { progress.Report(100.0); };
                            Logger.Info("New media file ready for extraction: " + file.FullName);
                            fileExtraction.CopyFileExtraction(source, destination);
                        }

                        CreateExtractionMarker(folder.FullName, Logger);
                    }
                }

                //progress.Report(100.0);
            }
        }


        private static void CreateExtractionMarker(string folderPath, ILogger logger)
        {
            logger.Info("Creating extraction marker: " + folderPath + "\\####emby.extracted####");
            using (var sw = new StreamWriter(folderPath + "\\####emby.extracted####"))
            {
                sw.Flush();
            }
        }

    }
}
