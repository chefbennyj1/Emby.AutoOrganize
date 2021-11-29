using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Naming.Common;
using Emby.Naming.TV;
using MediaBrowser.Common.Events;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;

namespace Emby.AutoOrganize.Core.FileOrganization
{
    public class EpisodeOrganizer
    {
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly IFileOrganizationService _organizationService;
        private readonly IProviderManager _providerManager;
        private readonly CultureInfo _usCulture = new CultureInfo("en-US");
        public static event EventHandler<GenericEventArgs<FileOrganizationResult>> ItemUpdated;
        public EpisodeOrganizer(IFileOrganizationService organizationService, IFileSystem fileSystem, ILogger logger, ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, IProviderManager providerManager)
        {
            _organizationService = organizationService;
            _fileSystem = fileSystem;
            _logger = logger;
            _libraryManager = libraryManager;
            _libraryMonitor = libraryMonitor;
            _providerManager = providerManager;
        }

        private NamingOptions _namingOptions;
        private NamingOptions GetNamingOptionsInternal()
        {
            if (_namingOptions == null)
            {
                var options = new NamingOptions();

                _namingOptions = options;
            }

            return _namingOptions;
        }

        private FileOrganizerType CurrentFileOrganizerType => FileOrganizerType.Episode;

        public async Task<FileOrganizationResult> OrganizeEpisodeFile(
            bool? requestToOverwriteExistingFile,
            string path,
            EpisodeFileOrganizationOptions options,
            CancellationToken cancellationToken)
        {
            _logger.Info("Sorting file {0}", path);

            var result = new FileOrganizationResult
            {
                Date = DateTime.UtcNow,
                OriginalPath = path,
                OriginalFileName = Path.GetFileName(path),
                ExtractedResolution = GetStreamResolutionFromFileName(Path.GetFileName(path)),
                Type = FileOrganizerType.Unknown,
                FileSize = _fileSystem.GetFileInfo(path).Length
            };

            var dbResult = _organizationService.GetResultBySourcePath(path);
            if(dbResult != null)
            {
                //We are processing, return the result
                if (dbResult.IsInProgress)
                {
                    return dbResult;
                }

                //If the User has choose to monitor movies and episodes in the same folder.
                //Stop the episode sort here if the item has been identified as a Movie.
                //If the item was found to be an movie and the result was not a failure then return that movie data instead of attempting episode matches.
                if(dbResult.Type == FileOrganizerType.Movie && dbResult.Status != FileSortingStatus.Failure)
                {
                    return dbResult;
                }                

                result = dbResult;
            }


            try
            {
                if (_libraryMonitor.IsPathLocked(path.AsSpan()) && result.Status != FileSortingStatus.Processing)
                {
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = "Path is locked by other processes. Please try again later.";
                    _logger.Info("Auto-organize Path is locked by other processes. Please try again later.");
                    return result;
                }

                if (_libraryMonitor.IsPathLocked(path.AsSpan()) && result.Status == FileSortingStatus.Processing)
                {
                    result.Status = FileSortingStatus.Processing;
                    result.StatusMessage = "Path is processing. Please try again later.";
                    _logger.Info("Auto-organize Path is locked by other processes. Please try again later.");
                    return result;
                }
                
                var namingOptions = GetNamingOptionsInternal();
                var resolver = new EpisodeResolver(namingOptions);

                var episodeInfo = resolver.Resolve(path, false) ?? new Emby.Naming.TV.EpisodeInfo();

                var seriesName = episodeInfo.SeriesName;
                int? seriesYear = null;

                if (!string.IsNullOrEmpty(seriesName))
                {
                    var seriesParseResult = _libraryManager.ParseName(seriesName.AsSpan());

                    seriesName = seriesParseResult.Name;
                    seriesYear = seriesParseResult.Year;
                }

                if (string.IsNullOrWhiteSpace(seriesName))
                {
                    seriesName = episodeInfo.SeriesName;
                }

                if (!string.IsNullOrEmpty(seriesName))
                {
                    var seasonNumber = episodeInfo.SeasonNumber;

                    result.ExtractedSeasonNumber = seasonNumber;

                    // Passing in true will include a few extra regex's
                    var episodeNumber = episodeInfo.EpisodeNumber;

                    result.ExtractedEpisodeNumber = episodeNumber;

                    var premiereDate = episodeInfo.IsByDate ? new DateTime(episodeInfo.Year.Value, episodeInfo.Month.Value, episodeInfo.Day.Value) : (DateTime?)null;

                    if (episodeInfo.IsByDate || (seasonNumber.HasValue && episodeNumber.HasValue))
                    {
                        if (episodeInfo.IsByDate)
                        {
                            _logger.Debug("Extracted information from {0}. Series name {1}, Date {2}", path, seriesName, premiereDate.Value);
                        }
                        else
                        {
                            _logger.Debug("Extracted information from {0}. Series name {1}, Season {2}, Episode {3}", path, seriesName, seasonNumber, episodeNumber);
                        }

                        // We detected an airdate or (an season number and an episode number)
                        // We have all the chance that the media type is an Episode
                        // if an earlier result exist with an different type, we update it
                        result.Type = CurrentFileOrganizerType;

                        var endingEpisodeNumber = episodeInfo.EndingEpisodeNumber;

                        result.ExtractedEndingEpisodeNumber = endingEpisodeNumber;

                        await OrganizeEpisode(
                            requestToOverwriteExistingFile, 
                            path,
                            seriesName,
                            seriesYear,
                            seasonNumber,
                            episodeNumber,
                            endingEpisodeNumber,
                            premiereDate,
                            options,
                            false,
                            result,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var msg = string.Format("Unable to determine episode number from {0}", path);
                        result.Status = FileSortingStatus.Failure;
                        result.StatusMessage = msg;
                        _logger.Warn(msg);
                    }
                }
                else
                {
                    var msg = string.Format("Unable to determine series name from {0}", path);
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = msg;
                    _logger.Warn(msg);
                }

                // Handle previous result
                var previousResult = _organizationService.GetResultBySourcePath(path);

                if ((previousResult != null && result.Type == FileOrganizerType.Unknown) || (previousResult?.Status == result.Status &&
                                                                                             previousResult?.StatusMessage == result.StatusMessage &&
                                                                                             result.Status != FileSortingStatus.Success))
                {
                    // Don't keep saving the same result over and over if nothing has changed
                    return previousResult;
                }

            }
            catch (OrganizationException ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                _logger.ErrorException("Error organizing file", ex);
            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                _logger.ErrorException("Error organizing file", ex);
            }

            _organizationService.SaveResult(result, CancellationToken.None);

            return result;
        }

        private async Task<Series> AutoDetectSeries(
            string seriesName,
            int? seriesYear,
            EpisodeFileOrganizationOptions options,
            CancellationToken cancellationToken)
        {
            if (options.AutoDetectSeries)
            {
                string metadataLanguage = null;
                string metadataCountryCode = null;
                BaseItem targetFolder = null;

                if (!string.IsNullOrEmpty(options.DefaultSeriesLibraryPath))
                {
                    targetFolder = _libraryManager.FindByPath(options.DefaultSeriesLibraryPath, true);
                }

                if (targetFolder != null)
                {
                    metadataLanguage = targetFolder.GetPreferredMetadataLanguage();
                    metadataCountryCode = targetFolder.GetPreferredMetadataCountryCode();
                }

                var seriesInfo = new SeriesInfo
                {
                    Name = seriesName,
                    Year = seriesYear,
                    MetadataCountryCode = metadataCountryCode,
                    MetadataLanguage = metadataLanguage
                };

                var searchResultsTask = await _providerManager.GetRemoteSearchResults<Series, SeriesInfo>(new RemoteSearchQuery<SeriesInfo>
                {
                    SearchInfo = seriesInfo

                }, targetFolder, cancellationToken);

                var finalResult = searchResultsTask.FirstOrDefault();

                if (finalResult != null)
                {
                    // We are in the good position, we can create the item
                    var organizationRequest = new EpisodeFileOrganizationRequest
                    {
                        NewSeriesName = finalResult.Name,
                        NewSeriesProviderIds = finalResult.ProviderIds,
                        NewSeriesYear = finalResult.ProductionYear,
                        TargetFolder = options.DefaultSeriesLibraryPath
                    };

                    return CreateNewSeries(organizationRequest, targetFolder, finalResult, options, cancellationToken);
                }
            }

            return null;
        }

        private Series CreateNewSeries(
            EpisodeFileOrganizationRequest request,
            BaseItem targetFolder,
            RemoteSearchResult result,
            EpisodeFileOrganizationOptions options,
            CancellationToken cancellationToken)
        {
            Series series;

            series = GetMatchingSeries(request.NewSeriesName, request.NewSeriesYear, targetFolder, null);

            if (series != null)
            {
                return series;
            }

            var seriesFolderName = GetSeriesDirectoryName(request.NewSeriesName, request.NewSeriesYear, options);

            var seriesName = request.NewSeriesName;
            var seriesPath = Path.Combine(request.TargetFolder, seriesFolderName);

            return new Series
            {
                Name = seriesName,
                Path = seriesPath,
                ProviderIds = request.NewSeriesProviderIds,
                ProductionYear = request.NewSeriesYear
            };
        }

        public async Task<FileOrganizationResult> OrganizeWithCorrection(
            EpisodeFileOrganizationRequest request,
            EpisodeFileOrganizationOptions options,
            CancellationToken cancellationToken)
        {
            var result = _organizationService.GetResult(request.ResultId);

            try
            {
                Series series = null;

                if (request.NewSeriesProviderIds.Count > 0)
                {
                    BaseItem targetFolder = null;

                    if (!string.IsNullOrEmpty(options.DefaultSeriesLibraryPath))
                    {
                        targetFolder = _libraryManager.FindByPath(options.DefaultSeriesLibraryPath, true);
                    }

                    series = CreateNewSeries(request, targetFolder, null, options, cancellationToken);
                }

                if (series == null)
                {
                    // Existing Series
                    series = (Series)_libraryManager.GetItemById(request.SeriesId);
                }

                // We manually set the media as Series 
                result.Type = CurrentFileOrganizerType;

                await OrganizeEpisode(request.RequestToMoveFile, result.OriginalPath,
                   series,
                   request.SeasonNumber,
                   request.EpisodeNumber,
                   request.EndingEpisodeNumber,
                   null,
                   options,
                   request.RememberCorrection,
                   result,
                   cancellationToken).ConfigureAwait(false);

                _organizationService.SaveResult(result, CancellationToken.None);
            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
            }

            return result;
        }

        private async Task OrganizeEpisode(bool? requestToMoveFile, 
            string sourcePath,
            string seriesName,
            int? seriesYear,
            int? seasonNumber,
            int? episodeNumber,
            int? endingEpisodeNumber,
            DateTime? premiereDate,
            EpisodeFileOrganizationOptions options,
            bool rememberCorrection,
            FileOrganizationResult result,
            CancellationToken cancellationToken)
        {
            var series = GetMatchingSeries(seriesName, seriesYear, null, result);

            if (series == null)
            {
                series = await AutoDetectSeries(seriesName, seriesYear, options, cancellationToken).ConfigureAwait(false);

                if (series == null)
                {
                    var msg = string.Format("Unable to find series in library matching name {0}", seriesName);
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = msg;
                    _logger.Warn(msg);
                    return;
                }
            }

            await OrganizeEpisode(requestToMoveFile, 
                sourcePath,
                series,
                seasonNumber,
                episodeNumber,
                endingEpisodeNumber,
                premiereDate,
                options,
                rememberCorrection,
                result,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Organize part responsible of Season AND Episode recognition
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="series"></param>
        /// <param name="seasonNumber"></param>
        /// <param name="episodeNumber"></param>
        /// <param name="endingEpisodeNumber"></param>
        /// <param name="premiereDate"></param>
        /// <param name="options"></param>
        /// <param name="smartMatch"></param>
        /// <param name="rememberCorrection"></param>
        /// <param name="result"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task OrganizeEpisode(
            bool? requestToMoveFile,
            string sourcePath,
            Series series,
            int? seasonNumber,
            int? episodeNumber,
            int? endingEpisodeNumber,
            DateTime? premiereDate,
            EpisodeFileOrganizationOptions options,
            bool rememberCorrection,
            FileOrganizationResult result,
            CancellationToken cancellationToken)
        {
            var episode = await GetMatchingEpisode(series, seasonNumber, episodeNumber, endingEpisodeNumber, result, premiereDate, cancellationToken);

            var season = !string.IsNullOrEmpty(episode.Season?.Path) ? episode.Season : GetMatchingSeason(series, episode, options, cancellationToken);

            // Now we can check the episode Path <-- we shouldn't check the path here, we should just create the path.
            //if (string.IsNullOrEmpty(episode.Path))
            //{
                episode.Path = SetEpisodeFileName(sourcePath, series.Name, season, episode, options);
            //}

            OrganizeEpisode(requestToMoveFile, sourcePath, series, episode, options, rememberCorrection, result, cancellationToken);

        }

        private void OrganizeEpisode(
            bool? requestToMoveFile, 
            string sourcePath,
            Series series,
            Episode episode,
            EpisodeFileOrganizationOptions options,
            bool rememberCorrection,
            FileOrganizationResult result,
            CancellationToken cancellationToken)
        {
            _logger.Info("Beginning Episode Organization");
            _logger.Info("Sorting file {0} into series {1}", sourcePath, series.Path);

            
           
            var originalExtractedSeriesString = result.ExtractedName;

            bool isNew = string.IsNullOrWhiteSpace(result.Id);

            if (isNew)
            {
                _organizationService.SaveResult(result, cancellationToken);
            }

            if (!_organizationService.AddToInProgressList(result, isNew))
            {
                throw new OrganizationException("File is currently processed otherwise. Please try again later.");
            }

            try
            {
                // Proceed to sort the file
                var resultTargetPath = episode.Path;

                if (string.IsNullOrEmpty(resultTargetPath))
                {
                    var msg = $"Unable to sort {sourcePath} because target path could not be determined.";
                    throw new OrganizationException(msg);
                }

                _logger.Info("Sorting file {0} to new path {1}", sourcePath, resultTargetPath);
                result.TargetPath = resultTargetPath;
               
                var fileExists = _fileSystem.FileExists(result.TargetPath);
                
                var existingEpisodeFilesButWithDifferentPath = GetExistingEpisodeFilesButWithDifferentPath(result.TargetPath, series, episode);
                result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;

                //The source path might be in use. The file could still be copying from it's origin location into watched folder. Status maybe "Waiting"
                if(IsCopying(sourcePath, result, _fileSystem) && !result.IsInProgress && result.Status != FileSortingStatus.Processing)
                {
                    var msg = $"File '{sourcePath}' is currently in use, stopping organization";
                    _logger.Info(msg);
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = msg;
                    result.TargetPath = resultTargetPath;
                    return;
                }

                if (!options.OverwriteExistingEpisodes)
                {
                    _logger.Info("Plugin options: no overwrite episode");
                    if (requestToMoveFile != null)
                    {                         
                        if (requestToMoveFile == true) //User is forcing sorting from the UI
                        {
                            _logger.Info("request to overwrite episode: " + requestToMoveFile);
                            PerformFileSorting(options, result, cancellationToken);
                            return;
                        }
                    }

                    if (options.CopyOriginalFile && fileExists && IsSameEpisode(sourcePath, resultTargetPath))
                    {
                        var msg = $"File '{sourcePath}' already copied to new path '{resultTargetPath}', stopping organization";
                        _logger.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        return;
                    }
                    
                    if (fileExists)
                    {
                        var msg = $"File '{sourcePath}' already exists as '{resultTargetPath}', stopping organization";
                        _logger.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        result.TargetPath = resultTargetPath;
                        return;
                    }

                    if (existingEpisodeFilesButWithDifferentPath.Count > 0)
                    {
                        var msg = $"File '{sourcePath}' already exists as these:'{string.Join("', '", existingEpisodeFilesButWithDifferentPath)}'. Stopping organization";
                        _logger.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;
                        return;
                    } 
                   
                    PerformFileSorting(options, result, cancellationToken);
                    
                }
                
                if (options.OverwriteExistingEpisodes)
                {
                    var hasRenamedFiles = false;

                    foreach (var path in existingEpisodeFilesButWithDifferentPath)
                    {
                        _logger.Debug("Removing episode(s) from file system {0}", path);

                        _libraryMonitor.ReportFileSystemChangeBeginning(path);

                        var renameRelatedFiles = !hasRenamedFiles &&
                            string.Equals(_fileSystem.GetDirectoryName(path), _fileSystem.GetDirectoryName(result.TargetPath), StringComparison.OrdinalIgnoreCase);

                        if (renameRelatedFiles)
                        {
                            hasRenamedFiles = true;
                        }

                        try
                        {
                            DeleteLibraryFile(path, renameRelatedFiles, result.TargetPath);
                        }
                        catch (IOException ex)
                        {
                            _logger.ErrorException("Error removing episode(s)", ex, path);
                        }
                        finally
                        {
                            _libraryMonitor.ReportFileSystemChangeComplete(path, true);
                        }
                    }

                    PerformFileSorting(options, result, cancellationToken);
                }

            }
            catch (Exception ex)
            {
                _logger.Warn("Exception Thrown in OrganizeEpisode Class");
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                _logger.Warn(ex.Message);
                return;
            }
            finally
            {
                _organizationService.RemoveFromInprogressList(result);
            }

            if (rememberCorrection)
            {
                SaveSmartMatchString(originalExtractedSeriesString, series.Name, cancellationToken);
            }
        }

        private void SaveSmartMatchString(string matchString, string seriesName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(matchString) || matchString.Length < 3)
            {
                return;
            }

            var info = _organizationService.GetSmartMatchInfos().Items.FirstOrDefault(i => string.Equals(i.ItemName, seriesName, StringComparison.OrdinalIgnoreCase));

            if (info == null)
            {
                info = new SmartMatchResult
                {
                    ItemName = seriesName,
                    OrganizerType = CurrentFileOrganizerType,
                    DisplayName = seriesName
                };
            }

            if (!info.MatchStrings.Contains(matchString, StringComparer.OrdinalIgnoreCase))
            {
                info.MatchStrings.Add(matchString);
                _organizationService.SaveResult(info, cancellationToken);
            }
        }

        private void DeleteLibraryFile(string path, bool renameRelatedFiles, string targetPath)
        {
            _fileSystem.DeleteFile(path);

            if (!renameRelatedFiles)
            {
                return;
            }

            // Now find other metadata files
            var originalFilenameWithoutExtension = Path.GetFileNameWithoutExtension(path);
            var directory = _fileSystem.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(originalFilenameWithoutExtension) && !string.IsNullOrWhiteSpace(directory))
            {
                // Get all related files, e.g. metadata, images, etc
                var files = _fileSystem.GetFilePaths(directory)
                    .Where(i => (Path.GetFileNameWithoutExtension(i) ?? string.Empty).StartsWith(originalFilenameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var targetFilenameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);

                foreach (var file in files)
                {
                    directory = _fileSystem.GetDirectoryName(file);
                    var filename = Path.GetFileName(file);

                    filename = filename.Replace(originalFilenameWithoutExtension, targetFilenameWithoutExtension, StringComparison.OrdinalIgnoreCase);

                    var destination = Path.Combine(directory, filename);

                    _fileSystem.MoveFile(file, destination); //Pretty much renaming these files.
                }
            }
        }

        private List<string> GetExistingEpisodeFilesButWithDifferentPath(string targetPath, Series series, Episode episode)
        {
            // TODO: Support date-naming?
            if (!series.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue)
            {
                return new List<string>();
            }

            if (IsNewSeries(series))
            {
                return new List<string>();
            }

            var episodePaths = series.GetRecursiveChildren()
                .OfType<Episode>()
                .Where(i =>
                {
                    var locationType = i.LocationType;

                    // Must be file system based and match exactly
                    if (locationType != LocationType.Virtual &&
                        i.ParentIndexNumber.HasValue && i.ParentIndexNumber.Value == series.ParentIndexNumber &&
                        i.IndexNumber.HasValue && i.IndexNumber.Value == episode.IndexNumber)
                    {

                        if (episode.IndexNumberEnd.HasValue || i.IndexNumberEnd.HasValue)
                        {
                            return episode.IndexNumberEnd.HasValue && i.IndexNumberEnd.HasValue && episode.IndexNumberEnd.Value == i.IndexNumberEnd.Value;
                        }

                        return true;
                    }

                    return false;

                }).Select(i => i.Path).ToList();

            var folder = _fileSystem.GetDirectoryName(targetPath);
            var targetFileNameWithoutExtension = _fileSystem.GetFileNameWithoutExtension(targetPath);

            try
            {
                var filesOfOtherExtensions = _fileSystem.GetFilePaths(folder)
                    .Where(i => _libraryManager.IsVideoFile(i.AsSpan()) && string.Equals(_fileSystem.GetFileNameWithoutExtension(i), targetFileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));

                episodePaths.AddRange(filesOfOtherExtensions);
            }
            catch (IOException)
            {
                // No big deal. Maybe the season folder doesn't already exist.
            }

            return episodePaths.Where(i => !string.Equals(i, targetPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void PerformFileSorting(EpisodeFileOrganizationOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            _logger.Info("Perform Sorting");
            result.Status = FileSortingStatus.Processing;
            _logger.Info($"Auto organize adding {result.TargetPath} to inprogress list");
            _organizationService.AddToInProgressList(result, true);
            _organizationService.SaveResult(result, cancellationToken);
            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
            
            // We should probably handle this earlier so that we never even make it this far
            //Yup, because if OverwriteExisting files is turned on, and these to paths are the same, it will have deleted the source file.
            if (string.Equals(result.OriginalPath, result.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = FileSortingStatus.Failure;
                _organizationService.RemoveFromInprogressList(result);
                _organizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
                return;
            }

            _libraryMonitor.ReportFileSystemChangeBeginning(result.TargetPath);

            try 
            {
                _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(result.TargetPath));
            } 
            catch {} //It is possible we are overwriting a file, and therefore can not create this directory.

            var targetAlreadyExists = _fileSystem.FileExists(result.TargetPath) || result.DuplicatePaths.Count > 0;

            try
            {
                if (targetAlreadyExists || options.CopyOriginalFile)
                {
                    _logger.Info("Copying File");
                    
                    try
                    {
                        _fileSystem.CopyFile(result.OriginalPath, result.TargetPath, true);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("disk space"))
                        {
                            result.Status = FileSortingStatus.NotEnoughDiskSpace;
                            result.StatusMessage = "There is not enough disk space on the drive to move this file";
                        } 
                        else if (ex.Message.Contains("used by another process"))
                        {
                             
                            result.Status = FileSortingStatus.InUse;
                            result.StatusMessage = "The file maybe being streaming to a emby device. Please try again later.";
                           
                        }

                        _logger.Warn(ex.Message);
                        _organizationService.RemoveFromInprogressList(result);
                        _organizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
                        return;
                    }
                   
                }
                else
                {
                    _logger.Info("Moving File");
                    try
                    {
                         _fileSystem.MoveFile(result.OriginalPath, result.TargetPath);
                    }
                    catch (Exception ex)
                    {
                       if (ex.Message.Contains("disk space"))
                       {
                           result.Status = FileSortingStatus.NotEnoughDiskSpace;
                           result.StatusMessage = "There is not enough disk space on the drive to move this file";
                       } 
                       else if (ex.Message.Contains("used by another process"))
                       {
                           
                           result.Status = FileSortingStatus.InUse;
                           result.StatusMessage = "The file is being streamed to a emby device. Please try again later.";
                       }
                       _logger.Warn(ex.Message);
                       _organizationService.RemoveFromInprogressList(result);
                       _organizationService.SaveResult(result, cancellationToken);
                       EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
                       return;
                    } 
                   
                }

                result.Status = FileSortingStatus.Success;
                result.StatusMessage = string.Empty;               
                _organizationService.RemoveFromInprogressList(result);
                _organizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
            }
            catch (IOException ex)
            {
                if(ex.Message.Contains("being used by another process"))
                {                    
                    var errorMsg =
                        $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse; //We're waiting for the file to become available.
                    result.StatusMessage = errorMsg;
                    _logger.ErrorException(errorMsg, ex);
                    _organizationService.RemoveFromInprogressList(result);
                    _organizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("We have encountered an error during Processing. Most likely copying the file!");
                var errorMsg = $"Failed to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = errorMsg;
                _logger.ErrorException(errorMsg, ex);
                _organizationService.RemoveFromInprogressList(result);
                _organizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
                return;
            }
            finally
            {
                _libraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
            }

            if (targetAlreadyExists && !options.CopyOriginalFile)
            {
                try
                {
                    _fileSystem.DeleteFile(result.OriginalPath);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error deleting {0}", ex, result.OriginalPath);
                }
            }
        }

        private bool IsNewSeries(Series series)
        {
            return series.InternalId.Equals(0);
        }

        private async Task<Episode> GetMatchingEpisode(Series series,
            int? seasonNumber,
            int? episodeNumber,
            int? endingEpiosdeNumber,
            FileOrganizationResult result,
            DateTime? premiereDate,
            CancellationToken cancellationToken)
        {
            Episode episode = null;

            if (!IsNewSeries(series))
            {
                episode = series
                   .GetRecursiveChildren().OfType<Episode>()
                   .FirstOrDefault(e => e.ParentIndexNumber == seasonNumber
                           && e.IndexNumber == episodeNumber
                           && e.IndexNumberEnd == endingEpiosdeNumber
                           && e.LocationType == LocationType.FileSystem
                           && Path.GetExtension(e.Path) == Path.GetExtension(result.OriginalPath));
            }

            if (episode == null)
            {
                return await CreateNewEpisode(series, seasonNumber, episodeNumber, endingEpiosdeNumber, premiereDate, cancellationToken);
            }

            return episode;
        }

        private Season GetMatchingSeason(Series series, Episode episode, EpisodeFileOrganizationOptions options, CancellationToken cancellationToken)
        {
            var season = episode.Season;

            if (season == null)
            {
                if (!IsNewSeries(series))
                {
                    season = series
                        .GetRecursiveChildren().OfType<Season>()
                        .FirstOrDefault(e => e.IndexNumber == episode.ParentIndexNumber
                                             && e.LocationType == LocationType.FileSystem);
                }

                if (season == null)
                {
                    if (!episode.ParentIndexNumber.HasValue)
                    {
                        var msg = string.Format("No season found for {0} season {1} episode {2}", series.Name,
                            episode.ParentIndexNumber, episode.IndexNumber);
                        _logger.Warn(msg);
                        throw new OrganizationException(msg);
                    }

                    season = new Season
                    {
                        Id = Guid.NewGuid(),
                        SeriesId = series.InternalId,
                        IndexNumber = episode.ParentIndexNumber,
                    };
                }
            }

            if (string.IsNullOrEmpty(season.Path))
            {
                season.Path = GetSeasonFolderPath(series, episode.ParentIndexNumber.Value, options);
            }

            return season;
        }

        private Series GetMatchingSeries(string seriesName, int? seriesYear, BaseItem targetFolder, FileOrganizationResult result)
        {
            if (result != null)
            {
                result.ExtractedName = seriesName;
                result.ExtractedYear = seriesYear;
            }

            var series = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Series).Name },
                Recursive = true,
                DtoOptions = new DtoOptions(true),
                AncestorIds = targetFolder == null ? Array.Empty<long>() : new[] { targetFolder.InternalId },
                SearchTerm = seriesName,
                Years = seriesYear.HasValue ? new[] { seriesYear.Value } : Array.Empty<int>()
            })
                .Cast<Series>()
                .FirstOrDefault();

            if (series == null)
            {
                var info = _organizationService.GetSmartMatchInfos().Items.FirstOrDefault(e => e.MatchStrings.Contains(seriesName, StringComparer.OrdinalIgnoreCase));

                if (info != null)
                {
                    series = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { typeof(Series).Name },
                        Recursive = true,
                        AncestorIds = targetFolder == null ? Array.Empty<long>() : new[] { targetFolder.InternalId },
                        Name = info.ItemName,
                        DtoOptions = new DtoOptions(true)

                    }).Cast<Series>().FirstOrDefault();
                }
            }

            return series;
        }

        /// <summary>
        /// Get the new series name
        /// </summary>
        /// <param name="series"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        private string GetSeriesDirectoryName(string seriesName, int? seriesYear, EpisodeFileOrganizationOptions options)
        {
            var seriesFullName = seriesName;

            if (seriesYear.HasValue)
            {
                seriesFullName = string.Format("{0} ({1})", seriesFullName, seriesYear);
            }

            var seasonFolderName = options.SeriesFolderPattern
                .Replace("%sn", seriesName)
                .Replace("%s.n", seriesName.Replace(" ", "."))
                .Replace("%s_n", seriesName.Replace(" ", "_"))
                .Replace("%fn", seriesFullName);

            if (seriesYear.HasValue)
            {
                seasonFolderName = seasonFolderName.Replace("%sy", seriesYear.Value.ToString());
            }
            else
            {
                seasonFolderName = seasonFolderName.Replace("%sy", string.Empty);
            }

            // Don't try to create a series folder ending in a period
            // https://emby.media/community/index.php?/topic/77680-auto-organize-shows-with-periods-qnap-and-cache
            return _fileSystem.GetValidFilename(seasonFolderName).TrimEnd(new[] { '.', ' ' });
        }

        /// <summary>
        /// CreateNewEpisode
        /// </summary>
        /// <param name="series">The series.</param>
        /// <param name="seasonNumber">The season number.</param>
        /// <param name="episodeNumber">The episode number.</param>
        /// <param name="endingEpisodeNumber">The ending episode number.</param>
        /// <param name="premiereDate">The premiere date.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>System.String.</returns>
        private async Task<Episode> CreateNewEpisode(
            Series series,
            int? seasonNumber,
            int? episodeNumber,
            int? endingEpisodeNumber,
            DateTime? premiereDate,
            CancellationToken cancellationToken)
        {
            var episodeInfo = new EpisodeInfo
            {
                IndexNumber = episodeNumber,
                IndexNumberEnd = endingEpisodeNumber,
                MetadataCountryCode = series.GetPreferredMetadataCountryCode(),
                MetadataLanguage = series.GetPreferredMetadataLanguage(),
                ParentIndexNumber = seasonNumber,
                SeriesProviderIds = series.ProviderIds,
                PremiereDate = premiereDate
            };

            var searchResults = await _providerManager.GetRemoteSearchResults<Episode, EpisodeInfo>(new RemoteSearchQuery<EpisodeInfo>
            {
                SearchInfo = episodeInfo

            }, series, cancellationToken).ConfigureAwait(false);

            var episodeSearch = searchResults.FirstOrDefault();

            if (episodeSearch == null)
            {
                var msg = string.Format("No provider metadata found for {0} season {1} episode {2}", series.Name, seasonNumber, episodeNumber);
                _logger.Warn(msg);
                throw new OrganizationException(msg);
            }

            seasonNumber = seasonNumber ?? episodeSearch.ParentIndexNumber;
            episodeNumber = episodeNumber ?? episodeSearch.IndexNumber;
            endingEpisodeNumber = endingEpisodeNumber ?? episodeSearch.IndexNumberEnd;

            var episode = new Episode
            {
                ParentIndexNumber = seasonNumber,
                SeriesId = series.InternalId,
                IndexNumber = episodeNumber,
                IndexNumberEnd = endingEpisodeNumber,
                ProviderIds = episodeSearch.ProviderIds,
                Name = episodeSearch.Name,
            };

            return episode;
        }

        /// <summary>
        /// Gets the season folder path.
        /// </summary>
        /// <param name="series">The series.</param>
        /// <param name="seasonNumber">The season number.</param>
        /// <param name="options">The options.</param>
        /// <returns>System.String.</returns>
        private string GetSeasonFolderPath(Series series, int seasonNumber, EpisodeFileOrganizationOptions options)
        {
            var path = series.Path;

            if (ContainsEpisodesWithoutSeasonFolders(series))
            {
                return path;
            }

            if (seasonNumber == 0)
            {
                return Path.Combine(path, _fileSystem.GetValidFilename(options.SeasonZeroFolderName));
            }

            var seasonFolderName = options.SeasonFolderPattern
                .Replace("%s", seasonNumber.ToString(_usCulture))
                .Replace("%0s", seasonNumber.ToString("00", _usCulture))
                .Replace("%00s", seasonNumber.ToString("000", _usCulture));

            return Path.Combine(path, _fileSystem.GetValidFilename(seasonFolderName));
        }

        private bool ContainsEpisodesWithoutSeasonFolders(Series series)
        {
            if (IsNewSeries(series))
            {
                return false;
            }

            var children = series.GetChildren(new InternalItemsQuery());
            foreach (var child in children)
            {
                if (child is Video)
                {
                    return true;
                }
            }
            return false;
        }

        private string SetEpisodeFileName(string sourcePath, string seriesName, Season season, Episode episode, EpisodeFileOrganizationOptions options)
        {
            seriesName = _fileSystem.GetValidFilename(seriesName).Trim();

            var episodeTitle = _fileSystem.GetValidFilename(episode.Name).Trim();

            if (!episode.IndexNumber.HasValue || !season.IndexNumber.HasValue)
            {
                throw new OrganizationException("GetEpisodeFileName: Mandatory param as missing!");
            }

            var endingEpisodeNumber = episode.IndexNumberEnd;
            var episodeNumber = episode.IndexNumber.Value;
            var seasonNumber = season.IndexNumber.Value;

            var sourceExtension = (Path.GetExtension(sourcePath) ?? string.Empty).TrimStart('.');

            var pattern = endingEpisodeNumber.HasValue ? options.MultiEpisodeNamePattern : options.EpisodeNamePattern;

            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new OrganizationException("GetEpisodeFileName: Configured episode name pattern is empty!");
            }

            var result = pattern.Replace("%sn", seriesName)
                .Replace("%s.n", seriesName.Replace(" ", "."))
                .Replace("%s_n", seriesName.Replace(" ", "_"))
                .Replace("%s", seasonNumber.ToString(_usCulture))
                .Replace("%0s", seasonNumber.ToString("00", _usCulture))
                .Replace("%00s", seasonNumber.ToString("000", _usCulture))
                .Replace("%ext", sourceExtension)
                .Replace("%en", "%#1")
                .Replace("%e.n", "%#2")
                .Replace("%e_n", "%#3")
                .Replace("%fn", Path.GetFileNameWithoutExtension(sourcePath));

            if (endingEpisodeNumber.HasValue)
            {
                result = result.Replace("%ed", endingEpisodeNumber.Value.ToString(_usCulture))
                .Replace("%0ed", endingEpisodeNumber.Value.ToString("00", _usCulture))
                .Replace("%00ed", endingEpisodeNumber.Value.ToString("000", _usCulture));
            }

            result = result.Replace("%e", episodeNumber.ToString(_usCulture))
                .Replace("%0e", episodeNumber.ToString("00", _usCulture))
                .Replace("%00e", episodeNumber.ToString("000", _usCulture));

            if (result.Contains("%#"))
            {
                result = result.Replace("%#1", episodeTitle)
                    .Replace("%#2", episodeTitle.Replace(" ", "."))
                    .Replace("%#3", episodeTitle.Replace(" ", "_"));
            }

            // Finally, call GetValidFilename again in case user customized the episode expression with any invalid filename characters
            return Path.Combine(season.Path, _fileSystem.GetValidFilename(result).Trim());
            

        }

        private bool IsSameEpisode(string sourcePath, string newPath)
        {
            try
            {
                var sourceFileInfo = _fileSystem.GetFileInfo(sourcePath);
                var destinationFileInfo = _fileSystem.GetFileInfo(newPath);

                if (sourceFileInfo.Length == destinationFileInfo.Length)
                {
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }

            return false;
        }

        public static bool IsCopying(string source, FileOrganizationResult dbResult, IFileSystem fileSystem)
        {
            try
            {
                var sourceFile = fileSystem.GetFileInfo(source);
                if(dbResult.FileSize == sourceFile.Length) return false;

            } 
            catch (Exception)
            {
                return true;
            }
            return true;
        }
        public static string GetStreamResolutionFromFileName(string movieName)
        {
            var namingOptions = new NamingOptions();
            
            foreach(var resolution in namingOptions.VideoResolutionFlags)
            {
                if(movieName.Contains(resolution))
                {
                    return resolution;

                }
            }
            return string.Empty;
            
        }
    }
}
