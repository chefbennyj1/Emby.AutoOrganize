using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Core.FileOrganization;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Model.Organization;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace Emby.AutoOrganize.Core.WatchedFolderOrganization
{
    public class WatchedFolderOrganizer
    {
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly IFileOrganizationService _organizationService;
        private readonly IServerConfigurationManager _config;
        private readonly IProviderManager _providerManager;

        public WatchedFolderOrganizer(ILibraryManager libraryManager, ILogger logger, IFileSystem fileSystem, ILibraryMonitor libraryMonitor, IFileOrganizationService organizationService, IServerConfigurationManager config, IProviderManager providerManager)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
            _libraryMonitor = libraryMonitor;
            _organizationService = organizationService;
            _config = config;
            _providerManager = providerManager;
        }

        private bool EnableOrganization(FileSystemMetadata fileInfo, AutoOrganizeOptions options)
        {
            var minFileBytes = options.MinFileSizeMb * 1024 * 1024;

            try
            {
                
                return _libraryManager.IsVideoFile(fileInfo.FullName.AsSpan()) && 
                       fileInfo.Length >= minFileBytes && 
                       !IgnoredFileName(fileInfo, options.IgnoredFileNameContains.ToList())|| 
                       _libraryManager.IsSubtitleFile(fileInfo.FullName.AsSpan());
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error organizing file {0}", ex, fileInfo.Name);
            }

            return false;
        }

        private bool IsValidWatchLocation(string path, List<string> libraryFolderPaths)
        {
            if (IsPathAlreadyInMediaLibrary(path, libraryFolderPaths))
            {
                _logger.Info("Folder {0} is not eligible for auto-organize because it is also part of an Emby library", path);
                return false;
            }

            return true;
        }

        private bool IsPathAlreadyInMediaLibrary(string path, List<string> libraryFolderPaths)
        {
            return libraryFolderPaths.Any(i => string.Equals(i, path, StringComparison.Ordinal) || _fileSystem.ContainsSubPath(i.AsSpan(), path.AsSpan()));
        }

        public async Task Organize(AutoOrganizeOptions options, CancellationToken cancellationToken, IProgress<double> progress)
        {
            
            var libraryFolderPaths = _libraryManager.GetVirtualFolders().SelectMany(i => i.Locations).ToList();

            var watchLocations = new List<string>();
            
            //If we're not pre-processing files, check the watch locations directly
            if (!options.EnablePreProcessing)
            {
                watchLocations = options.WatchLocations.Where(i => IsValidWatchLocation(i, libraryFolderPaths)).ToList();
            }
            else
            {
                //We are preprocessing files so add the pre-processing destination folders as watched locations.
                if (!string.IsNullOrEmpty(options.PreProcessingFolderPath))
                {
                    watchLocations.Add(options.PreProcessingFolderPath);
                }
                else
                {
                    _logger.Warn("Pre-processing is enabled but no pre-processing folder is configured. No files will be organized.");
                    return;
                }
                
            }
            

            _logger.Info($"Auto Organize Watched Locations: {watchLocations.Count} folder(s)");
           
            //For each watch location
            var eligibleFiles = watchLocations.SelectMany(GetFilesToOrganize).OrderBy(_fileSystem.GetCreationTimeUtc).Where(i => EnableOrganization(i, options)).ToList();
            
            var step = 100.0 / eligibleFiles.Count;
            var currentProgress = 0.0;

            _logger.Info($"Eligible file count {eligibleFiles.Count}");

            var processedFolders = new HashSet<string>();

            
            if (!eligibleFiles.Any())
            {
                _logger.Info("Beginning watched processed directory clean up...");
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(99.0);

                try
                {
                    //var deleteExtensions = options.LeftOverFileExtensionsToDelete.Select(i => i.Trim().TrimStart('.'))
                    //    .Where(i => !string.IsNullOrEmpty(i)).Select(i => "." + i).ToList();

                    // Clean
                    CleanWatchedFolderFiles(options);

                }
                catch (Exception ex)
                {
                    _logger.Warn("Unable to clean watched folders: " + ex.Message);
                }

                return;
            }

            

            //Organize the subtitles last. This ensure that the media files have a home before accessing their subtitle files.
            eligibleFiles = eligibleFiles.OrderBy(f => _libraryManager.IsSubtitleFile(f.Name.AsSpan())).ToList();

            //if (eligibleFiles.Count > 20)
            //{
            //    _logger.Warn("Throttling eligible files for sorting. Sorting 20 files...");
            //    eligibleFiles = eligibleFiles.Take(20).ToList();
            //}
            
            
           
            foreach (var file in eligibleFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileOrganizerType = _organizationService.GetFileOrganizerType(file.Name);

                if (fileOrganizerType == FileOrganizerType.Episode)
                {
                    if (!options.EnableTelevisionOrganization) continue;

                    if (string.IsNullOrEmpty(options.DefaultSeriesLibraryPath))
                    {
                        _logger.Warn("No Default TV Show Library has been chosen in settings. Stopping Organization...");
                       
                        return;
                    }

                    var organizer = new EpisodeOrganizer(_organizationService, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);
                    try
                    {
                        FileOrganizationResult result;
                        //We really shouild bulk process files.
                        //Checking if there are more then 3 files will stop bulk processing. 
                        //However, if there are many files to process, then we will bulk process them.
                        if (eligibleFiles.Count > 3) 
                        {
                            result = organizer.OrganizeFile(false, file.FullName, options, cancellationToken).Result; //<== "Result"" will stop multiple organizations at once, and process one at a time.
                        }
                        else
                        {
                            result = await organizer.OrganizeFile(false, file.FullName, options, cancellationToken); //<== Process them all at once
                        }
                        

                        if (result.Status == FileSortingStatus.Success && !processedFolders.Contains(file.DirectoryName, StringComparer.OrdinalIgnoreCase))
                        {
                            processedFolders.Add(file.DirectoryName);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        
                        return;
                    }
                    catch (Exception)
                    {
                        _logger.Warn("Error organizing episode {0}", file.FullName);
                        continue;
                    }
                }

                if (fileOrganizerType == FileOrganizerType.Movie)
                {
                    if(!options.EnableMovieOrganization) continue;

                    if (string.IsNullOrEmpty(options.DefaultMovieLibraryPath))
                    {
                        _logger.Warn("No Default Movie Library has been chosen in settings. Stopping Organization...");
                        
                        return;
                    }

                    var movieOrganizer = new MovieOrganizer(_organizationService, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);
                    try
                    {
                        if (_libraryManager.IsVideoFile(file.FullName.AsSpan()))
                        {
                            FileOrganizationResult result;
                            //We really shouild bulk process files.
                            //Checking if there are more then 3 files will stop bulk processing. 
                            //However, if there are many files to process, then we will bulk process them.
                            if (eligibleFiles.Count > 3)
                            {
                                result = movieOrganizer.OrganizeFile(false, file.FullName, options, cancellationToken).Result;
                            }
                            else
                            {
                                result = await movieOrganizer.OrganizeFile(false, file.FullName, options, cancellationToken);
                            }
                           

                            if (result.Status == FileSortingStatus.Success && !processedFolders.Contains(file.DirectoryName, StringComparer.OrdinalIgnoreCase))
                            {
                                processedFolders.Add(file.DirectoryName);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("Error organizing the movie {0} - {1}", file.FullName, ex);
                        continue;
                    }
                }

                if (fileOrganizerType == FileOrganizerType.Unknown)
                {
                    _logger.Warn($"Sorting media type for {file.Name} is unknown. New episodes must contain the season index number, and episode index number in the file name.");
                    continue;
                }

                if (fileOrganizerType == FileOrganizerType.Subtitle)
                {
                    if (!options.EnableSubtitleOrganization) continue;
                    
                    if (string.IsNullOrEmpty(options.DefaultSeriesLibraryPath) || string.IsNullOrEmpty(options.DefaultMovieLibraryPath))
                    {
                        _logger.Warn("No Default Libraries have been chosen in settings. Stopping Organization...");
                        
                        return;
                    }
                    var subtitleOrganizer = new SubtitleOrganizer(_organizationService, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);
                    try
                    {
                        if (_libraryManager.IsSubtitleFile(file.FullName.AsSpan()))
                        {
                            var result = await subtitleOrganizer.OrganizeFile(true, file.FullName, options, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("Error organizing the subtitle file {0} - {1}", file.FullName, ex);
                    }
                }

                progress.Report((currentProgress += step) - 1);
               
            }

            //If we can delete some of the left over organized files in the pre-processing folder here, we should try.            
            CleanPreProcessingFolder(options);

        }
        
        private void CleanWatchedFolderFiles(AutoOrganizeOptions options)        
        {
            //var preProcessLocations = new List<string>() { options.PreProcessingFolderPath };
            var watchedLocations =  options.WatchLocations.ToList();
                       

            if (options.EnableCleanupOptions)
            {
                //var results = _organizationService.GetResults(new FileOrganizationResultQuery());

                foreach (var watchedFolder in watchedLocations)
                {
                    var folderData = Directory.GetDirectories(watchedFolder);

                    foreach (var folder in folderData)
                    {
                        _logger.Info($"Directory Clean up { folder }");

                        if (IsWatchFolder(folder, watchedLocations)) continue;
                        if (!IsDirectoryEmpty(folder))
                        {
                            _logger.Info($"{folder} is not empty.");
                            DeleteLeftOverFiles(folder, options.LeftOverFileExtensionsToDelete);
                            
                        }


                        //_logger.Info($"Checking{ folder } Organization Status...");
                        //var status = results.Items.FirstOrDefault(item => _fileSystem.GetDirectoryName(item.OriginalPath) == folder)?.Status;
                        //if (status != null)
                        //{
                        //    if (status != FileSortingStatus.Success)
                        //    {
                        //        _logger.Info($"{folder} status is { status }. Will wait to clean up directory.");
                        //        continue;
                        //    }
                        //}

                        //_logger.Info($"{folder} status is {(status.HasValue ? status.Value.ToString() : "Unknown")}: Removing watched folder item.");
                        if (IsDirectoryEmpty(folder))
                        {
                            try
                            {
                                _logger.Debug("Deleting directory {0}", folder);
                                _fileSystem.DeleteDirectory(folder, true);
                            }
                            catch (UnauthorizedAccessException)
                            {
                                throw new UnauthorizedAccessException("Unable to delete empty folders from watched folder. Access Denied.");
                            }
                            catch (IOException)
                            {
                                throw new IOException("Unable to delete empty folders from watched folder.");
                            }
                        }
                    }
                }

            }

            CleanPreProcessingFolder(options);

        }

       
        private void CleanPreProcessingFolder(AutoOrganizeOptions options)
        {
             //Clean out the pre-processing folder if the folders are empty.
            if (options.EnablePreProcessing)
            {
                var preProcessLocations = new List<string>() { options.PreProcessingFolderPath };
                foreach (var preProcessLocation in preProcessLocations)
                {
                    var folderData = Directory.GetDirectories(preProcessLocation);
                    foreach (var folder in folderData)
                    {
                        if (!IsDirectoryEmpty(folder)) continue;
                        try
                        {
                            _logger.Debug("Deleting directory {0} from pre-processing location", folder);
                            _fileSystem.DeleteDirectory(folder, true);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            throw new UnauthorizedAccessException("Unable to delete empty folders from pre-processing folder location. Access Denied.");
                        }
                        catch (IOException)
                        {
                            throw new IOException("Unable to delete empty folders from pre-processing folder location.");
                        }
                    }
                }
            }
        }
        private bool IsDirectoryEmpty(string path)
        {
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }

        /// <summary>
        /// Gets the files to organize.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>IEnumerable{FileInfo}.</returns>
        private IEnumerable<FileSystemMetadata> GetFilesToOrganize(string path)
        {
            try
            {
                return _fileSystem.GetFiles(path, true);
            }
            catch (DirectoryNotFoundException)
            {
                _logger.Info("Auto-Organize watch folder does not exist: {0}", path);

                return Enumerable.Empty<FileSystemMetadata>();
            }
            catch (IOException ex)
            {
                _logger.ErrorException("Error getting files from {0}", ex, path);

                return Enumerable.Empty<FileSystemMetadata>();
            }
        }

        /// <summary>
        /// Deletes the left over files.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="extensions">The extensions.</param>
        private void DeleteLeftOverFiles(string path, IEnumerable<string> extensions)
        {
            var eligibleFiles = _fileSystem.GetFilePaths(path, extensions.ToArray(), false, true)
                .ToList();

            if (!eligibleFiles.Any()) return;

            foreach (var file in eligibleFiles)
            {
                try
                {
                    _logger.Info($"Removing left over file {file}");
                    _fileSystem.DeleteFile(file);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error deleting file {0}", ex, file);
                }
            }
        }

        

        /// <summary>
        /// Determines if a given folder path is a folder folder
        /// </summary>
        /// <param name="path">The folder path to check.</param>
        /// <param name="watchLocations">A list of folders.</param>
        private bool IsWatchFolder(string path, IEnumerable<string> watchLocations)
        {
            return watchLocations.Contains(path, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IgnoredFileName(FileSystemMetadata fileInfo, List<string> ignoredFileNameContains)
        {
            if (ignoredFileNameContains.Count <= 0) return false;
            foreach (var ignoredString in ignoredFileNameContains)
            {
                if (string.IsNullOrEmpty(ignoredString)) continue;
                if (fileInfo.Name.ToLowerInvariant().Contains(ignoredString.ToLowerInvariant()))
                {
                    return true;
                }
            }
            return false;
        }
    }
}