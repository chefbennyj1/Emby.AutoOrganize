using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    public class EpisodeOrganizer : IFileOrganizer
    {
        private ILibraryMonitor LibraryMonitor               { get; }
        private ILibraryManager LibraryManager               { get; }
        private ILogger Log                                  { get; }
        private NamingOptions NamingOptions                 { get; set; }
        private IFileSystem FileSystem                       { get; }
        private IFileOrganizationService OrganizationService { get; }
        private IProviderManager ProviderManager             { get; }

        private readonly CultureInfo _usCulture = new CultureInfo("en-US");

        public static event EventHandler<GenericEventArgs<FileOrganizationResult>> ItemUpdated;

     
        //public static EpisodeOrganizer Instance { get; set; }
        public EpisodeOrganizer(IFileOrganizationService organizationService, IFileSystem fileSystem, ILogger log, ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, IProviderManager providerManager)
        {
            OrganizationService = organizationService;
            FileSystem          = fileSystem;
            Log                 = log;
            LibraryManager      = libraryManager;
            LibraryMonitor      = libraryMonitor;
            ProviderManager     = providerManager;
        }

       
        private NamingOptions GetNamingOptionsInternal()
        {
            if (NamingOptions == null)
            {
                var options = new NamingOptions();

                NamingOptions = options;
            }

            return NamingOptions;
        }

        

        public async Task<FileOrganizationResult> OrganizeFile(bool? requestToOverwriteExistingFile, string path, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            var result = new FileOrganizationResult //Default result object
            {
                Date = DateTime.UtcNow,
                OriginalPath = path,
                OriginalFileName = Path.GetFileName(path),
                ExtractedResolution = GetStreamResolutionFromFileName(Path.GetFileName(path)),
                ExtractedEdition = string.Empty,
                Type = FileOrganizerType.Episode,
                FileSize = FileSystem.GetFileInfo(path).Length,
                
            };

            //If a result already exists in the db from the last scan, and it is not a failure return it.
            var dbResult = OrganizationService.GetResultBySourcePath(path);
            if(dbResult != null) 
            {
                //If the file was "in-use" the last time the task was ran, then the file size was at a point when it wasn't completely copied into the monitored folder.
                //Update the file size data, and update the result to show the true/current file size.
                dbResult.FileSize = FileSystem.GetFileInfo(path).Length;
                OrganizationService.SaveResult(dbResult, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(dbResult), Log); //Update the UI

                //We are processing, return the result
                if (dbResult.IsInProgress) return dbResult;

                result = dbResult; 
            }

            //Check to see if we can access the file path, or if the file path is being used.
            if (LibraryMonitor.IsPathLocked(path.AsSpan()) && result.Status != FileSortingStatus.Processing || IsCopying(path, FileSystem))
            {
                result.Status = FileSortingStatus.InUse;
                result.StatusMessage = "Path is locked by other processes. Please try again later.";
                Log.Info("Auto-organize Path is locked by other processes. Please try again later.");
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.RemoveFromInprogressList(result);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                return result;
            }
            
            if (LibraryMonitor.IsPathLocked(path.AsSpan()) && result.Status == FileSortingStatus.Processing)
            {
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return result;
            }
            

            //Looks lke we can access the file path.

            try
            {

                var namingOptions = GetNamingOptionsInternal();
                var resolver = new EpisodeResolver(namingOptions);

                var episodeInfo = resolver.Resolve(path, false) ?? new Naming.TV.EpisodeInfo();

                var seriesName = episodeInfo.SeriesName;
                int? seriesYear = null;

                if (!string.IsNullOrEmpty(seriesName))
                {
                    var seriesParseResult = LibraryManager.ParseName(seriesName.AsSpan());

                    seriesName = seriesParseResult.Name;
                    seriesYear = seriesParseResult.Year;
                }

                if (string.IsNullOrWhiteSpace(seriesName))
                {
                    seriesName = episodeInfo.SeriesName;
                }

                if (!string.IsNullOrEmpty(seriesName))
                {
                    //result.ExtractedSeriesName = seriesName;

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
                            Log.Debug("Extracted information from {0}. Series name {1}, Date {2}", path, seriesName, premiereDate.Value);
                        }
                        else
                        {
                            Log.Debug("Extracted information from {0}. Series name {1}, Season {2}, Episode {3}", path, seriesName, seasonNumber, episodeNumber);
                        }

                        var endingEpisodeNumber = episodeInfo.EndingEpisodeNumber;

                        result.ExtractedEndingEpisodeNumber = endingEpisodeNumber;

                        result.ExtractedName = seriesName;
                        result.ExtractedYear = seriesYear;


                        //await OrganizeEpisode(requestToOverwriteExistingFile, path, seriesName, seriesYear, seasonNumber, episodeNumber, endingEpisodeNumber, premiereDate, options, false, result, cancellationToken).ConfigureAwait(false);
                        var series = GetMatchingSeries(seriesName, seriesYear, null, result, cancellationToken);

                        if (series == null)
                        {
                            series = await GetSeriesRemoteProviderData(seriesName, seriesYear, options, cancellationToken);//.ConfigureAwait(false);

                            if (series == null && result.Status != FileSortingStatus.Waiting) //Series will be null when it spans multiple folders. 
                            {
                                var msg = $"Unable to determine series from {path}";
                                result.Status = FileSortingStatus.Failure;
                                result.StatusMessage = msg;
                                Log.Warn(msg);
                                return result;
                            }

                            if (result.Status == FileSortingStatus.Waiting)
                            {
                                return result;
                            }
                        }

                        await OrganizeEpisode(requestToOverwriteExistingFile, 
                            path,
                            series,
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
                        var msg = $"Unable to determine episode number from {path}";
                        result.Status = FileSortingStatus.Failure;
                        result.StatusMessage = msg;
                        Log.Warn(msg);
                        
                    }
                }
                else
                {
                    var msg = $"Unable to determine series name from {path}";
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = msg;
                    Log.Warn(msg);
                    
                }

                // Handle previous result
                var previousResult = OrganizationService.GetResultBySourcePath(path);

                if ((previousResult != null && result.Type == FileOrganizerType.Unknown) || (previousResult?.Status == result.Status &&
                                                                                             previousResult?.StatusMessage == result.StatusMessage &&
                                                                                             result.Status != FileSortingStatus.Success))
                {
                    // Don't keep saving the same result over and over if nothing has changed
                    return previousResult;
                }

            }
            
            catch (Exception ex)
            {
                if (result.Status != FileSortingStatus.Waiting) //Waiting when the series spans multiple locations, and we need user input.
                {
                    //otherwise fail.
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = ex.Message;
                    Log.Warn("Error organizing file", ex);
                }
                
            }

            OrganizationService.SaveResult(result, CancellationToken.None);

            return result;
        }
        
      

        private Series CreateNewSeries(EpisodeFileOrganizationRequest request, BaseItem targetRootFolder, RemoteSearchResult result, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            var seriesFolderName = GetSeriesFolderName(request.NewSeriesName, request.NewSeriesYear, options);

            var seriesName = request.NewSeriesName ?? result.Name;
            var seriesPath = Path.Combine(targetRootFolder.Path, seriesFolderName);
            

            return new Series
            {
                Name = seriesName,
                Path = seriesPath,
                ProviderIds = request.NewSeriesProviderIds,
                ProductionYear = request.NewSeriesYear
            };
        }

        public async Task<FileOrganizationResult> OrganizeWithCorrection(EpisodeFileOrganizationRequest request, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            var result = OrganizationService.GetResult(request.ResultId);

            if (request.CreateNewDestination)
            {
                Log.Info("Sorting request to create a new series Destination.");

                var seriesFolderName = GetSeriesFolderName(request.NewSeriesName, request.NewSeriesYear, options);
                var seasonFolderName = GetSeasonFolderName(request.SeasonNumber, options);
               

                var paths = result.TargetPath?.Split(Path.DirectorySeparatorChar).ToList();

                //There has to be a better way then this... this seems wrong, but it works.
                var pathResult = Path.Combine(request.TargetFolder, Path.Combine(seriesFolderName, seasonFolderName, paths[paths.Count() -1]));

                result.TargetPath = pathResult;

                OrganizationService.SaveResult(result, cancellationToken);

                PerformFileSorting(options, result, cancellationToken);

                if (!request.RememberCorrection) return result;

                var series = LibraryManager.GetItemById(request.SeriesId);
                SaveSmartMatchString(Path.GetFileNameWithoutExtension(result.OriginalFileName), series.Name, cancellationToken);
                return result;
            }
            

            try
            {
                Series series = null;
                BaseItem targetFolder = null;

                
                //The user has identified the series in the UI, and it has provider data attached to the request.
                //OR the user has chosen a destination root folder for the target file.
                if (request.NewSeriesProviderIds.Count > 0)
                {
                    //This is a new series to be created using the default library path from the settings
                    if (!string.IsNullOrEmpty(options.DefaultSeriesLibraryPath)) //&& request.TargetFolder is null)
                    {
                        targetFolder = LibraryManager.FindByPath(options.DefaultSeriesLibraryPath, true);
                    }

                    series =  GetMatchingSeries(request.NewSeriesName, request.NewSeriesYear, targetFolder, null, cancellationToken) ??
                              CreateNewSeries(request, targetFolder, null, options, cancellationToken);
                }

                if (series == null)
                {
                    // Existing Series
                    series = (Series)LibraryManager.GetItemById(request.SeriesId);
                }

                await OrganizeEpisode(request.RequestToMoveFile, result.OriginalPath, series, request.SeasonNumber, request.EpisodeNumber, request.EndingEpisodeNumber, null,
                   options,
                   request.RememberCorrection,
                   result,
                   cancellationToken).ConfigureAwait(false);

                OrganizationService.SaveResult(result, CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (result.Status == FileSortingStatus.Waiting) return result;
                
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;

            }

            return result;
        }

        //private async Task OrganizeEpisode(bool? requestToMoveFile,
        //    string sourcePath,
        //    string seriesName,
        //    int? seriesYear,
        //    int? seasonNumber,
        //    int? episodeNumber,
        //    int? endingEpisodeNumber,
        //    DateTime? premiereDate,
        //    AutoOrganizeOptions options,
        //    bool rememberCorrection,
        //    FileOrganizationResult result,
        //    CancellationToken cancellationToken)
        //{
        //    var series = GetMatchingSeries(seriesName, seriesYear, null, result, cancellationToken);

        //    if (series == null)
        //    {
        //        series = await GetSeriesRemoteProviderData(seriesName, seriesYear, options, cancellationToken);//.ConfigureAwait(false);

        //        if (series == null && result.Status != FileSortingStatus.Waiting) //Series will be null when it spans multiple folders. 
        //        {
        //            var msg = $"Unable to determine series from {sourcePath}";
        //            result.Status = FileSortingStatus.Failure;
        //            result.StatusMessage = msg;
        //            Log.Warn(msg);
        //            return;
        //        }

        //        if (result.Status == FileSortingStatus.Waiting)
        //        {
        //            return;
        //        }
        //    }

        //    await OrganizeEpisode(requestToMoveFile, 
        //        sourcePath,
        //        series,
        //        seasonNumber,
        //        episodeNumber,
        //        endingEpisodeNumber,
        //        premiereDate,
        //        options,
        //        rememberCorrection,
        //        result,
        //        cancellationToken).ConfigureAwait(false);
        //}

        /// <summary>
        /// Organize part responsible of Season AND Episode recognition
        /// </summary>
        private async Task OrganizeEpisode(bool? requestToMoveFile, string sourcePath, Series series, int? seasonNumber, int? episodeNumber, int? endingEpisodeNumber, DateTime? premiereDate, AutoOrganizeOptions options, bool rememberCorrection, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            var episode = GetMatchingEpisode(series, seasonNumber, episodeNumber, endingEpisodeNumber, result, premiereDate, cancellationToken);

            if (episode is null)
            {
                try
                {
                    episode = await GetEpisodeRemoteProviderData(series, seasonNumber, episodeNumber, endingEpisodeNumber, premiereDate, cancellationToken);
                }
                catch(Exception ex)
                {
                    Log.Warn("Provider Data Error", ex);
                }
            }

            if(episode is null)
            {
                var msg = $"Unable to determine episode from {sourcePath}";
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = msg;
                Log.Warn(msg);
                return;
            }
            
            var season = episode.Season ?? GetMatchingSeason(series, result, options);
            

            var episodeFileName = GetEpisodeFileName(sourcePath, series.Name, season.IndexNumber, episode.IndexNumber, episode.IndexNumberEnd, episode.Name, options);
            episode.Path = Path.Combine(season.Path, episodeFileName);

           
            if (string.IsNullOrEmpty(episode.Path))
            {
                var msg = $"Unable to sort {sourcePath} because target path could not be determined.";
                throw new OrganizationException(msg);
            }

            result.TargetPath = episode.Path;
            Log.Info($"Episode Target Path : {result.TargetPath}");

            //OrganizeEpisode(requestToMoveFile, sourcePath, series, episode, options, rememberCorrection, result, cancellationToken);
            Log.Info("Beginning Episode Organization");
            Log.Info("Sorting file {0} into series {1}", sourcePath, series.Name);

            
            var originalExtractedSeriesString = result.ExtractedName;

            bool isNew = string.IsNullOrWhiteSpace(result.Id);

            if (isNew)
            {
                OrganizationService.SaveResult(result, cancellationToken);
            }

            if (!OrganizationService.AddToInProgressList(result, isNew))
            {
                throw new OrganizationException("File is currently processed otherwise, Please try again later.");
            }

            try
            {
                // Proceed to sort the file

                var fileExists = FileSystem.FileExists(result.TargetPath);
                
                var existingEpisodeFilesButWithDifferentPath = GetExistingEpisodeFilesButWithDifferentPath(result.TargetPath, series, episode);
                result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;

                //The source path might be in use. The file could still be copying from it's origin location into watched folder. Status maybe "Waiting"
                if(IsCopying(sourcePath, FileSystem) && !result.IsInProgress && result.Status != FileSortingStatus.Processing)
                {
                    var msg = $"File '{sourcePath}' is currently in use, stopping organization";
                    Log.Info(msg);
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = msg;
                    OrganizationService.RemoveFromInprogressList(result);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }

                

                //Four phases:
                //1. Overwrite existing files option is unchecked - The key words input is empty - no files will be overwritten.
                //2. Overwrite existing files option is checked - Doesn't matter about key words - any file will overwrite the library item.
                //3. Overwrite existing files option is unchecked - Key words inputs have values - only items with key words in the file name will overwrite the library item.
                //4. If the file doesn't exist and is new sort it!

                //1.
                if (!options.OverwriteExistingFiles && !options.OverwriteExistingFilesKeyWords.Any()) //NO you have to check the key words like this. there will be empty strings in the config.
                {
                    Log.Info("Plugin options: no overwrite episode");
                    if (requestToMoveFile != null)
                    {                         
                        if (requestToMoveFile == true) //User is forcing sorting from the UI
                        {
                            Log.Info("request to overwrite episode: " + requestToMoveFile);
                            PerformFileSorting(options, result, cancellationToken);
                            return;
                        }
                    }


                    if (options.CopyOriginalFile && fileExists && IsSameEpisode(sourcePath, result.TargetPath))
                    {
                        var msg = $"File '{sourcePath}' already copied to new path '{result.TargetPath}', stopping organization";
                        Log.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        OrganizationService.SaveResult(result, cancellationToken);
                        //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        OrganizationService.RemoveFromInprogressList(result);
                        return;
                    }
                    
                    if (fileExists)
                    {
                        var msg = $"File '{sourcePath}' already exists as '{result.TargetPath}', stopping organization";
                        Log.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        OrganizationService.SaveResult(result, cancellationToken);
                        //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        OrganizationService.RemoveFromInprogressList(result);
                        return;
                    }

                    if (existingEpisodeFilesButWithDifferentPath.Count > 0)
                    {
                        var msg = $"File '{sourcePath}' already exists as these:'{string.Join("', '", existingEpisodeFilesButWithDifferentPath)}'. Stopping organization";
                        Log.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;
                        OrganizationService.SaveResult(result, cancellationToken);
                        //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        OrganizationService.RemoveFromInprogressList(result);
                        return;
                    } 
                   
                    PerformFileSorting(options, result, cancellationToken);
                    
                }
                
                //2.
                if (options.OverwriteExistingFiles)
                {
                    var hasRenamedFiles = false;

                    foreach (var path in existingEpisodeFilesButWithDifferentPath)
                    {
                        Log.Debug("Removing episode(s) from file system {0}", path);

                        LibraryMonitor.ReportFileSystemChangeBeginning(path);

                        var renameRelatedFiles = !hasRenamedFiles &&
                            string.Equals(FileSystem.GetDirectoryName(path), FileSystem.GetDirectoryName(result.TargetPath), StringComparison.OrdinalIgnoreCase);

                        if (renameRelatedFiles)
                        {
                            hasRenamedFiles = true;
                        }

                        try
                        {
                            DeleteLibraryFile(path, renameRelatedFiles, result.TargetPath);
                        }
                        catch (Exception ex)
                        {
                            Log.ErrorException("Error removing episode(s)", ex, path);
                        }
                        finally
                        {
                            LibraryMonitor.ReportFileSystemChangeComplete(path, true);
                        }
                    }

                    PerformFileSorting(options, result, cancellationToken);
                    
                }

                //3.
                if (!options.OverwriteExistingFiles && options.OverwriteExistingFilesKeyWords.Any() && fileExists)
                {
                    if (options.OverwriteExistingFilesKeyWords.Any(word => result.OriginalFileName.ContainsIgnoreCase(word)))
                    {
                        var hasRenamedFiles = false;

                        foreach (var path in existingEpisodeFilesButWithDifferentPath)
                        {
                            Log.Debug("Removing episode(s) from file system {0}", path);

                            LibraryMonitor.ReportFileSystemChangeBeginning(path);

                            var renameRelatedFiles = !hasRenamedFiles &&
                                                     string.Equals(FileSystem.GetDirectoryName(path), FileSystem.GetDirectoryName(result.TargetPath), StringComparison.OrdinalIgnoreCase);

                            if (renameRelatedFiles)
                            {
                                hasRenamedFiles = true;
                            }

                            try
                            {
                                DeleteLibraryFile(path, renameRelatedFiles, result.TargetPath);
                            }
                            catch (Exception ex)
                            {
                                Log.ErrorException("Error removing episode(s)", ex, path);
                            }
                            finally
                            {
                                LibraryMonitor.ReportFileSystemChangeComplete(path, true);
                            }
                        }
                        PerformFileSorting(options, result, cancellationToken);
                    }
                    else
                    {
                        var msg = $"File '{sourcePath}' already exists as these:'{string.Join("', '", existingEpisodeFilesButWithDifferentPath)}'. Stopping organization";
                        Log.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;
                        OrganizationService.SaveResult(result, cancellationToken);
                        //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        OrganizationService.RemoveFromInprogressList(result);
                        return;
                    }
                }

                if (!fileExists)
                {
                    PerformFileSorting(options, result, cancellationToken);
                }

            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                Log.Warn(ex.Message);
                //TODO: Replace this return... maybe
                //return;
            }
            finally
            {
                OrganizationService.RemoveFromInprogressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
            }

            if (rememberCorrection)
            {
                SaveSmartMatchString(originalExtractedSeriesString, series.Name, cancellationToken);
            }

        }

        //private void OrganizeEpisode(
        //    bool? requestToMoveFile, 
        //    string sourcePath,
        //    Series series,
        //    Episode episode,
        //    AutoOrganizeOptions options,
        //    bool rememberCorrection,
        //    FileOrganizationResult result,
        //    CancellationToken cancellationToken)
        //{
        //    Log.Info("Beginning Episode Organization");
        //    Log.Info("Sorting file {0} into series {1}", sourcePath, series.Path);

            
        //    var originalExtractedSeriesString = result.ExtractedName;

        //    bool isNew = string.IsNullOrWhiteSpace(result.Id);

        //    if (isNew)
        //    {
        //        OrganizationService.SaveResult(result, cancellationToken);
        //    }

        //    if (!OrganizationService.AddToInProgressList(result, isNew))
        //    {
        //        throw new OrganizationException("File is currently processed otherwise, Please try again later.");
        //    }

        //    try
        //    {
        //        // Proceed to sort the file
        //        var resultTargetPath = episode.Path;

        //        if (string.IsNullOrEmpty(resultTargetPath))
        //        {
        //            var msg = $"Unable to sort {sourcePath} because target path could not be determined.";
        //            throw new OrganizationException(msg);
        //        }

        //        Log.Info("Sorting file {0} to new path {1}", sourcePath, resultTargetPath);
        //        result.TargetPath = resultTargetPath;
               
        //        var fileExists = FileSystem.FileExists(result.TargetPath);
                
        //        var existingEpisodeFilesButWithDifferentPath = GetExistingEpisodeFilesButWithDifferentPath(result.TargetPath, series, episode);
        //        result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;

        //        //The source path might be in use. The file could still be copying from it's origin location into watched folder. Status maybe "Waiting"
        //        if(IsCopying(sourcePath, FileSystem) && !result.IsInProgress && result.Status != FileSortingStatus.Processing)
        //        {
        //            var msg = $"File '{sourcePath}' is currently in use, stopping organization";
        //            Log.Info(msg);
        //            result.Status = FileSortingStatus.InUse;
        //            result.StatusMessage = msg;
        //            result.TargetPath = resultTargetPath;
                   
        //            OrganizationService.SaveResult(result, cancellationToken);
        //            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
        //            return;
        //        }

                

        //        //Four phases:
        //        //1. Overwrite existing files option is unchecked - The key words input is empty - no files will be overwritten.
        //        //2. Overwrite existing files option is checked - Doesn't matter about key words - any file will overwrite the library item.
        //        //3. Overwrite existing files option is unchecked - Key words inputs have values - only items with key words in the file name will overwrite the library item.
        //        //4. If the file doesn't exist and is new sort it!

        //        //1.
        //        if (!options.OverwriteExistingFiles && !options.OverwriteExistingFilesKeyWords.Any()) //NO you have to check the key words like this. there will be empty strings in the config.
        //        {
        //            Log.Info("Plugin options: no overwrite episode");
        //            if (requestToMoveFile != null)
        //            {                         
        //                if (requestToMoveFile == true) //User is forcing sorting from the UI
        //                {
        //                    Log.Info("request to overwrite episode: " + requestToMoveFile);
        //                    PerformFileSorting(options, result, cancellationToken);
        //                    return;
        //                }
        //            }


        //            if (options.CopyOriginalFile && fileExists && IsSameEpisode(sourcePath, resultTargetPath))
        //            {
        //                var msg = $"File '{sourcePath}' already copied to new path '{resultTargetPath}', stopping organization";
        //                Log.Info(msg);
        //                result.Status = FileSortingStatus.SkippedExisting;
        //                result.StatusMessage = msg;
        //                OrganizationService.SaveResult(result, cancellationToken);
        //                //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
        //                OrganizationService.RemoveFromInprogressList(result);
        //                return;
        //            }
                    
        //            if (fileExists)
        //            {
        //                var msg = $"File '{sourcePath}' already exists as '{resultTargetPath}', stopping organization";
        //                Log.Info(msg);
        //                result.Status = FileSortingStatus.SkippedExisting;
        //                result.StatusMessage = msg;
        //                result.TargetPath = resultTargetPath;
        //                OrganizationService.SaveResult(result, cancellationToken);
        //                //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
        //                OrganizationService.RemoveFromInprogressList(result);
        //                return;
        //            }

        //            if (existingEpisodeFilesButWithDifferentPath.Count > 0)
        //            {
        //                var msg = $"File '{sourcePath}' already exists as these:'{string.Join("', '", existingEpisodeFilesButWithDifferentPath)}'. Stopping organization";
        //                Log.Info(msg);
        //                result.Status = FileSortingStatus.SkippedExisting;
        //                result.StatusMessage = msg;
        //                result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;
        //                OrganizationService.SaveResult(result, cancellationToken);
        //                //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
        //                OrganizationService.RemoveFromInprogressList(result);
        //                return;
        //            } 
                   
        //            PerformFileSorting(options, result, cancellationToken);
                    
        //        }
                
        //        //2.
        //        if (options.OverwriteExistingFiles)
        //        {
        //            var hasRenamedFiles = false;

        //            foreach (var path in existingEpisodeFilesButWithDifferentPath)
        //            {
        //                Log.Debug("Removing episode(s) from file system {0}", path);

        //                LibraryMonitor.ReportFileSystemChangeBeginning(path);

        //                var renameRelatedFiles = !hasRenamedFiles &&
        //                    string.Equals(FileSystem.GetDirectoryName(path), FileSystem.GetDirectoryName(result.TargetPath), StringComparison.OrdinalIgnoreCase);

        //                if (renameRelatedFiles)
        //                {
        //                    hasRenamedFiles = true;
        //                }

        //                try
        //                {
        //                    DeleteLibraryFile(path, renameRelatedFiles, result.TargetPath);
        //                }
        //                catch (Exception ex)
        //                {
        //                    Log.ErrorException("Error removing episode(s)", ex, path);
        //                }
        //                finally
        //                {
        //                    LibraryMonitor.ReportFileSystemChangeComplete(path, true);
        //                }
        //            }

        //            PerformFileSorting(options, result, cancellationToken);
                    
        //        }

        //        //3.
        //        if (!options.OverwriteExistingFiles && options.OverwriteExistingFilesKeyWords.Any() && fileExists)
        //        {
        //            if (options.OverwriteExistingFilesKeyWords.Any(word => result.OriginalFileName.ContainsIgnoreCase(word)))
        //            {
        //                var hasRenamedFiles = false;

        //                foreach (var path in existingEpisodeFilesButWithDifferentPath)
        //                {
        //                    Log.Debug("Removing episode(s) from file system {0}", path);

        //                    LibraryMonitor.ReportFileSystemChangeBeginning(path);

        //                    var renameRelatedFiles = !hasRenamedFiles &&
        //                                             string.Equals(FileSystem.GetDirectoryName(path), FileSystem.GetDirectoryName(result.TargetPath), StringComparison.OrdinalIgnoreCase);

        //                    if (renameRelatedFiles)
        //                    {
        //                        hasRenamedFiles = true;
        //                    }

        //                    try
        //                    {
        //                        DeleteLibraryFile(path, renameRelatedFiles, result.TargetPath);
        //                    }
        //                    catch (Exception ex)
        //                    {
        //                        Log.ErrorException("Error removing episode(s)", ex, path);
        //                    }
        //                    finally
        //                    {
        //                        LibraryMonitor.ReportFileSystemChangeComplete(path, true);
        //                    }
        //                }
        //                PerformFileSorting(options, result, cancellationToken);
        //            }
        //        }

        //        if (!fileExists)
        //        {
        //            PerformFileSorting(options, result, cancellationToken);
        //        }

        //    }
        //    catch (Exception ex)
        //    {
        //        result.Status = FileSortingStatus.Failure;
        //        result.StatusMessage = ex.Message;
        //        Log.Warn(ex.Message);
        //        //TODO: Replace this return... maybe
        //        //return;
        //    }
        //    finally
        //    {
        //        OrganizationService.RemoveFromInprogressList(result);
        //        OrganizationService.SaveResult(result, cancellationToken);
        //    }

        //    if (rememberCorrection)
        //    {
        //        SaveSmartMatchString(originalExtractedSeriesString, series.Name, cancellationToken);
        //    }
        //}

        private void SaveSmartMatchString(string matchString, string seriesName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(matchString) || matchString.Length < 3)
            {
                return;
            }

            var info = OrganizationService.GetSmartMatchInfos().Items.FirstOrDefault(i => string.Equals(i.ItemName, seriesName, StringComparison.OrdinalIgnoreCase));

            if (info == null)
            {
                info = new SmartMatchResult
                {
                    ItemName = seriesName,
                    OrganizerType = FileOrganizerType.Episode,
                    DisplayName = seriesName,
                    
                };
            }

            if (!info.MatchStrings.Contains(matchString, StringComparer.OrdinalIgnoreCase))
            {
                info.MatchStrings.Add(matchString);
                OrganizationService.SaveResult(info, cancellationToken);
            }
        }

        private void DeleteLibraryFile(string path, bool renameRelatedFiles, string targetPath)
        {
            FileSystem.DeleteFile(path);

            if (!renameRelatedFiles)
            {
                return;
            }

            // Now find other metadata files
            var originalFilenameWithoutExtension = Path.GetFileNameWithoutExtension(path);
            var directory = FileSystem.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(originalFilenameWithoutExtension) && !string.IsNullOrWhiteSpace(directory))
            {
                // Get all related files, e.g. metadata, images, etc
                var files = FileSystem.GetFilePaths(directory)
                    .Where(i => (Path.GetFileNameWithoutExtension(i) ?? string.Empty).StartsWith(originalFilenameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var targetFilenameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);

                foreach (var file in files)
                {
                    directory = FileSystem.GetDirectoryName(file);
                    var filename = Path.GetFileName(file);

                    filename = filename.Replace(originalFilenameWithoutExtension, targetFilenameWithoutExtension, StringComparison.OrdinalIgnoreCase);

                    var destination = Path.Combine(directory, filename);

                    FileSystem.MoveFile(file, destination); //Pretty much renaming these files.
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

            var folder = FileSystem.GetDirectoryName(targetPath);
            var targetFileNameWithoutExtension = FileSystem.GetFileNameWithoutExtension(targetPath);

            try
            {
                var filesOfOtherExtensions = FileSystem.GetFilePaths(folder)
                    .Where(i => LibraryManager.IsVideoFile(i.AsSpan()) && string.Equals(FileSystem.GetFileNameWithoutExtension(i), targetFileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));

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

        private void PerformFileSorting(AutoOrganizeOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            Log.Info("Perform Sorting");
            result.Status = FileSortingStatus.Processing;
            var msg = ($"Auto organize added {result.TargetPath} to inprogress list");
            result.StatusMessage = msg;
            Log.Info(msg);
            OrganizationService.AddToInProgressList(result, true);
            OrganizationService.SaveResult(result, cancellationToken);
            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
            
            // We should probably handle this earlier so that we never even make it this far
            //Yup, because if OverwriteExisting files is turned on, and these to paths are the same, it will have deleted the source file.
            if (string.Equals(result.OriginalPath, result.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = FileSortingStatus.Failure;
                OrganizationService.RemoveFromInprogressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return;
            }

            LibraryMonitor.ReportFileSystemChangeBeginning(result.TargetPath);

            try 
            {
                FileSystem.CreateDirectory(FileSystem.GetDirectoryName(result.TargetPath));
            } 
            catch {} //It is possible we are overwriting a file, and therefore can not create this directory.

            var targetAlreadyExists = FileSystem.FileExists(result.TargetPath) || result.DuplicatePaths.Count > 0;

            try
            {
                if (options.CopyOriginalFile)
                {
                    Log.Info($"{ (targetAlreadyExists ? "Overwriting Existing Destination File" : "Copying File") }");
                    
                    try
                    {
                        if (targetAlreadyExists)
                        {
                            try
                            {
                                FileSystem.DeleteFile(result.TargetPath);
                                Log.Info($"Removing library item: {result.TargetPath}");
                            }
                            catch (Exception ex)
                            {
                                Log.ErrorException("Error deleting {0}", ex, result.TargetPath);
                            }
                        }

                        FileSystem.CopyFile(result.OriginalPath, result.TargetPath, true);
                        Log.Info($"{result.OriginalPath} has successfully been placed in the target destination: {result.TargetPath}");
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

                        Log.Warn(ex.Message);
                        OrganizationService.RemoveFromInprogressList(result);
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }
                   
                }
                else
                {
                    Log.Info("Moving File");
                    try
                    {
                        if (targetAlreadyExists)
                        {
                            try
                            {
                                FileSystem.DeleteFile(result.TargetPath);
                                Log.Info($"Removing library item: {result.TargetPath}");
                            }
                            catch (Exception ex)
                            {
                                Log.ErrorException("Error deleting {0}", ex, result.TargetPath);
                            }
                        }
                        FileSystem.MoveFile(result.OriginalPath, result.TargetPath);
                        Log.Info($"{result.OriginalPath} has successfully been moved to {result.TargetPath}");
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
                       Log.Warn(ex.Message);
                       OrganizationService.RemoveFromInprogressList(result);
                       OrganizationService.SaveResult(result, cancellationToken);
                       EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                       return;
                    } 
                   
                }

                result.Status = FileSortingStatus.Success;
                result.StatusMessage = string.Empty;               
                OrganizationService.RemoveFromInprogressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
            }
            catch (IOException ex)
            {
                if(ex.Message.Contains("being used by another process"))
                {                    
                    var errorMsg =
                        $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse; //We're waiting for the file to become available.
                    result.StatusMessage = errorMsg;
                    Log.ErrorException(errorMsg, ex);
                    OrganizationService.RemoveFromInprogressList(result);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("We have encountered an error during Processing. Most likely copying the file!");
                var errorMsg = $"Failed to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = errorMsg;
                Log.ErrorException(errorMsg, ex);
                OrganizationService.RemoveFromInprogressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return;
            }
            finally
            {
                LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
            }

            if (options.CopyOriginalFile) return;

            try
            {
                Log.Info($"Removing Source file from watched folder: {result.OriginalPath}");
                FileSystem.DeleteFile(result.OriginalPath);
            }
            catch (Exception ex)
            {
                Log.ErrorException("Error deleting {0}", ex, result.OriginalPath);
            }

        }

        private bool IsNewSeries(Series series)
        {
            return series.InternalId.Equals(0);
        }


        private Episode GetMatchingEpisode(Series series, int? seasonNumber, int? episodeNumber, int? endingEpisodeNumber, FileOrganizationResult result, DateTime? premiereDate, CancellationToken cancellationToken)
        {
            Episode episode = null;

            if (!IsNewSeries(series))
            {
                episode = series
                   .GetRecursiveChildren().OfType<Episode>()
                   .FirstOrDefault(e => e.ParentIndexNumber == seasonNumber
                           && e.IndexNumber == episodeNumber
                           && e.IndexNumberEnd == endingEpisodeNumber
                           && e.LocationType == LocationType.FileSystem
                           && Path.GetExtension(e.Path) == Path.GetExtension(result.OriginalPath));
            }

            
            return episode;
        }

        private Season GetMatchingSeason(Series series, FileOrganizationResult result, AutoOrganizeOptions options)
        {
            Season season = null;

            if (!IsNewSeries(series))
            {
                try
                {
                    season = series
                        .GetRecursiveChildren().OfType<Season>()
                        .FirstOrDefault(e => e.IndexNumber == result.ExtractedSeasonNumber
                                             && e.LocationType == LocationType.FileSystem);
                }
                catch {}
            }

            if (season == null)
            {
                season = new Season
                {
                    Id = Guid.NewGuid(),
                    SeriesId = series.InternalId,
                    IndexNumber = result.ExtractedSeasonNumber,
                };
            }

            if (string.IsNullOrEmpty(season.Path))
            {
                if (ContainsEpisodesWithoutSeasonFolders(series))
                {
                    season.Path = series.Path;
                }
                else
                {
                    season.Path = Path.Combine(series.Path, GetSeasonFolderName(season.IndexNumber.Value, options));
                }
                
                //season.Path = GetSeasonFolderPath(series, episode.ParentIndexNumber.Value, options);
            }

            return season;
        }
        
        private Series GetMatchingSeries(string seriesName, int? seriesYear, BaseItem targetFolder, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            List<BaseItem> resultSeries = null;

            var series = LibraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Series) },
                Recursive = true,
                DtoOptions = new DtoOptions(true),
                Years = seriesYear.HasValue ? new[] { seriesYear.Value } : Array.Empty<int>()

            });
                
            resultSeries = series.Where(s => NormalizeString(s.Name) == (NormalizeString(seriesName))).ToList();

            if (!resultSeries.Any())
            {
                resultSeries = series.Where(s => NormalizeString(s.Name).ContainsIgnoreCase(NormalizeString(seriesName))).ToList();
            }
            
            //We  can't read this series name try the smart list
            if (!resultSeries.Any())
            {
                var info = OrganizationService.GetSmartMatchInfos().Items.FirstOrDefault(e => e.MatchStrings.Contains(seriesName, StringComparer.OrdinalIgnoreCase));

                if (info == null) return null;

                series = LibraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Series) },
                    Recursive = true,
                    AncestorIds = targetFolder == null ? Array.Empty<long>() : new[] { targetFolder.InternalId },
                    Name = info.ItemName,
                    DtoOptions = new DtoOptions(true)
                });

                if (series.Any())
                {
                    resultSeries = series.ToList();
                }
            }

            if (resultSeries.Count == 1)
            {
                return resultSeries.Cast<Series>().FirstOrDefault();
            }

            if (resultSeries.Count > 1)
            {
                //Mark the result as having more then one location in the library. Needs user input, set it to "Waiting"
                result.Status = FileSortingStatus.Waiting;
                const string msg = "Item has more then one possible destination folders. Waiting to sort file...";
                result.StatusMessage = msg;
                result.TargetPath = string.Empty;
                Log.Warn(msg);
                OrganizationService.RemoveFromInprogressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                throw new OrganizationException(msg);
            }

            return null;
        }
        

        private async Task<Episode> GetEpisodeRemoteProviderData(Series series, int? seasonNumber, int? episodeNumber, int? endingEpisodeNumber, DateTime? premiereDate, CancellationToken cancellationToken)
        {
            Log.Info("Searching providers for matching episode data...");
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

            var searchResults = await ProviderManager.GetRemoteSearchResults<Episode, EpisodeInfo>(new RemoteSearchQuery<EpisodeInfo>
            {
                SearchInfo = episodeInfo,
                IncludeDisabledProviders = true

            }, series, cancellationToken);//.ConfigureAwait(false);

            var episodeSearch = searchResults.FirstOrDefault();

            if (episodeSearch == null)
            {
                var msg = $"No provider metadata found for {series.Name} season {seasonNumber} episode {episodeNumber}";
                Log.Warn(msg);
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
        private async Task<Series> GetSeriesRemoteProviderData(string seriesName, int? seriesYear, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            if (options.AutoDetectSeries)
            {
                string metadataLanguage = null;
                string metadataCountryCode = null;
                BaseItem targetRootFolder = null;

                if (!string.IsNullOrEmpty(options.DefaultSeriesLibraryPath))
                {
                    targetRootFolder = LibraryManager.FindByPath(options.DefaultSeriesLibraryPath, true);
                }

                if (targetRootFolder != null)
                {
                    metadataLanguage = targetRootFolder.GetPreferredMetadataLanguage();
                    metadataCountryCode = targetRootFolder.GetPreferredMetadataCountryCode();
                }

                var seriesInfo = new SeriesInfo
                {
                    Name = seriesName,
                    Year = seriesYear,
                    MetadataCountryCode = metadataCountryCode ?? "",
                    MetadataLanguage = metadataLanguage ?? ""
                };

                var searchResultsTask = await ProviderManager.GetRemoteSearchResults<Series, SeriesInfo>(new RemoteSearchQuery<SeriesInfo>
                {
                    SearchInfo = seriesInfo

                }, targetRootFolder, cancellationToken);

                var finalResult = searchResultsTask.FirstOrDefault(result => NormalizeString(result.Name) == NormalizeString(seriesName));

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

                    var series =  GetMatchingSeries(organizationRequest.NewSeriesName, organizationRequest.NewSeriesYear, targetRootFolder, null, cancellationToken);
            
                    return series ?? CreateNewSeries(organizationRequest, targetRootFolder, finalResult, options, cancellationToken);
                }
            }

            return null;
        }


        private string GetSeriesFolderName(string seriesName, int? seriesYear, AutoOrganizeOptions options)
        {
            var seriesFullName = seriesName;

            if (seriesYear.HasValue)
            {
                seriesFullName = string.Format("{0} ({1})", seriesFullName, seriesYear);
            }

            var seriesFolderName = options.SeriesFolderPattern
                .Replace("%sn", seriesName)
                .Replace("%s.n", seriesName.Replace(" ", "."))
                .Replace("%s_n", seriesName.Replace(" ", "_"))
                .Replace("%fn", seriesFullName);

            if (seriesYear.HasValue)
            {
                seriesFolderName = seriesFolderName.Replace("%sy", seriesYear.Value.ToString());
            }
            else
            {
                seriesFolderName = seriesFolderName.Replace("%sy", string.Empty);
            }

            // Don't try to create a series folder ending in a period
            // https://emby.media/community/index.php?/topic/77680-auto-organize-shows-with-periods-qnap-and-cache
            return FileSystem.GetValidFilename(seriesFolderName).TrimEnd(new[] { '.', ' ' });
        }
        private string GetSeasonFolderName(int seasonNumber, AutoOrganizeOptions options)
        {

            if (seasonNumber == 0)
            {
                //return Path.Combine(path, FileSystem.GetValidFilename(options.SeasonZeroFolderName));
                return FileSystem.GetValidFilename(options.SeasonZeroFolderName);
            }

            var seasonFolderName = options.SeasonFolderPattern
                .Replace("%s", seasonNumber.ToString())
                .Replace("%0s", seasonNumber.ToString("00", new CultureInfo("en-US")))
                .Replace("%00s", seasonNumber.ToString("000", new CultureInfo("en-US")));

            //return Path.Combine(path, FileSystem.GetValidFilename(seasonFolderName));
            return FileSystem.GetValidFilename(seasonFolderName);
        }
        private string GetEpisodeFileName(string sourcePath, string seriesName, int? seasonIndexNumber, int? episodeIndexNumber, int? episodeIndexNumberEnd, string episodeName, AutoOrganizeOptions options)
        {
            seriesName = FileSystem.GetValidFilename(seriesName).Trim();

            var episodeTitle = FileSystem.GetValidFilename(episodeName).Trim();

            if (!episodeIndexNumber.HasValue || !seasonIndexNumber.HasValue)
            {
                throw new OrganizationException("GetEpisodeFileName: Mandatory param as missing!");
            }

            var endingEpisodeNumber = episodeIndexNumberEnd;
            var episodeNumber = episodeIndexNumber.Value;
            var seasonNumber = seasonIndexNumber.Value;

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
            //return Path.Combine(season.Path, FileSystem.GetValidFilename(result).Trim());
            return FileSystem.GetValidFilename(result).Trim();

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
        
        private bool IsSameEpisode(string sourcePath, string newPath)
        {
            try
            {
                var sourceFileInfo = FileSystem.GetFileInfo(sourcePath);
                var destinationFileInfo = FileSystem.GetFileInfo(newPath);

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
        
        private static bool IsCopying(string source, IFileSystem fileSystem)
        {

            try
            {
                var file = new FileInfo(source);
                using(var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }

            //file is not locked
            return false;
            
        }

        private static string GetStreamResolutionFromFileName(string movieName)
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

        private string NormalizeString(string value)
        {
            return Regex.Replace(value, @"(\s+|@|&|'|:|\(|\)|<|>|#|\.)", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
        }
       
    }
}