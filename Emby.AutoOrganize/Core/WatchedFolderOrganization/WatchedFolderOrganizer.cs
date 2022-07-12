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
            
            if (!options.EnablePreProcessing)
            {
                watchLocations = options.WatchLocations.Where(i => IsValidWatchLocation(i, libraryFolderPaths)).ToList();
            }
            else
            {
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
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(99.0);

                try
                {
                    var deleteExtensions = options.LeftOverFileExtensionsToDelete.Select(i => i.Trim().TrimStart('.'))
                        .Where(i => !string.IsNullOrEmpty(i)).Select(i => "." + i).ToList();

                    // Normal Clean
                    Clean(processedFolders, watchLocations, options.DeleteEmptyFolders, deleteExtensions);

                    // Extended Clean
                    if (options.ExtendedClean)
                    {
                        Clean(watchLocations, watchLocations, options.DeleteEmptyFolders, deleteExtensions);
                    }

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
                        var result = await organizer.OrganizeFile(false, file.FullName, options, cancellationToken);

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
                            var result = await movieOrganizer.OrganizeFile(false, file.FullName, options, cancellationToken);

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
                    
                    if (string.IsNullOrEmpty(options.DefaultSeriesLibraryPath) && string.IsNullOrEmpty(options.DefaultMovieLibraryPath))
                    {
                        _logger.Warn("No Default Libraries have been chosen in settings. Stopping Organization...");
                        
                        return;
                    }
                    var subtitleOrganizer = new SubtitleOrganizer(_organizationService, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);
                    try
                    {
                        //TODO: need to check for different languages
                        // TODO: need to account for extra types SDH (HEARING, FORCED etc)
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
        }

        private void Clean(IEnumerable<string> paths, List<string> watchLocations, bool deleteEmptyFolders, List<string> deleteExtensions)
        {
            foreach (var path in paths)
            {
                if (deleteExtensions.Count > 0)
                {
                    DeleteLeftOverFiles(path, deleteExtensions);
                }

                if (deleteEmptyFolders)
                {
                    DeleteEmptyFolders(path, watchLocations);
                }

            }
        }

        /// <summary>
        /// Gets the files to organize.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>IEnumerable{FileInfo}.</returns>
        private List<FileSystemMetadata> GetFilesToOrganize(string path)
        {
            try
            {
                return _fileSystem.GetFiles(path, true).ToList();
            }
            catch (DirectoryNotFoundException)
            {
                _logger.Info("Auto-Organize watch folder does not exist: {0}", path);

                return new List<FileSystemMetadata>();
            }
            catch (IOException ex)
            {
                _logger.ErrorException("Error getting files from {0}", ex, path);

                return new List<FileSystemMetadata>();
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

            foreach (var file in eligibleFiles)
            {
                try
                {
                    _fileSystem.DeleteFile(file);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error deleting file {0}", ex, file);
                }
            }
        }

        /// <summary>
        /// Deletes the empty folders.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="watchLocations">The path.</param>
        private void DeleteEmptyFolders(string path, List<string> watchLocations)
        {
            try
            {
                foreach (var d in _fileSystem.GetDirectoryPaths(path))
                {
                    DeleteEmptyFolders(d, watchLocations);
                }

                var entries = _fileSystem.GetFileSystemEntryPaths(path);
                _logger.Info($"{entries.Count()} directory entries");
                if (!entries.Any() && !IsWatchFolder(path, watchLocations))
                {
                    try
                    {
                        _logger.Debug("Deleting empty directory {0}", path);
                        _fileSystem.DeleteDirectory(path, false);
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Warn("Unable to delete empty folders. Access Denied.");
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