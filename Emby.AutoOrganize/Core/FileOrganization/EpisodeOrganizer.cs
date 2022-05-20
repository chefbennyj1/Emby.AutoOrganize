using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Naming;
using Emby.AutoOrganize.Naming.Common;
using Emby.Naming.TV;
using MediaBrowser.Common.Events;
using MediaBrowser.Common.Extensions;
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
using MediaBrowser.Model.Querying;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;

namespace Emby.AutoOrganize.Core.FileOrganization
{
    public class EpisodeOrganizer : BaseFileOrganizer<EpisodeFileOrganizationRequest>
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
            : base(organizationService, fileSystem, log, libraryManager, libraryMonitor, providerManager)
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

        

        public async Task<FileOrganizationResult> OrganizeFile(bool requestToMoveFile, string path, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            FileOrganizationResult result = null;

            try
            {
                //If a result already exists in the db from the last scan, and it is not a failure return it.
                var dbResult = OrganizationService.GetResultBySourcePath(path);
                if (dbResult != null)
                {
                    //If the file was "in-use" the last time the task was ran, then the file size was at a point when it wasn't completely copied into the monitored folder.
                    //Update the file size data, and update the result to show the true/current file size.
                    dbResult.FileSize = FileSystem.GetFileInfo(path).Length;
                    OrganizationService.SaveResult(dbResult, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this,
                        new GenericEventArgs<FileOrganizationResult>(dbResult), Log); //Update the UI

                    //We are processing, return the result
                    if (dbResult.IsInProgress) return dbResult;

                    result = dbResult;
                }
            }
            catch { }

            if(result == null)
            {
                result = new FileOrganizationResult //Create the result object
                {
                    Date                = DateTime.UtcNow,
                    OriginalPath        = path,
                    OriginalFileName    = Path.GetFileName(path),
                    ExtractedResolution = new Resolution(),
                    ExtractedEdition    = string.Empty,
                    Type                = FileOrganizerType.Episode,
                    FileSize            = FileSystem.GetFileInfo(path).Length,
                    SourceQuality       = RegexExtensions.GetSourceQuality(Path.GetFileName(path)),
                };

            }
                        

            //Check to see if we can access the file path, or if the file path is being used.
            if (LibraryMonitor.IsPathLocked(path.AsSpan()) && result.Status != FileSortingStatus.Processing || IsCopying(path, FileSystem))
            {
                result.Status = FileSortingStatus.InUse;
                result.StatusMessage = "Path is locked by other processes. Please try again later.";
                Log.Info("Auto-organize Path is locked by other processes. Please try again later.");
                
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                return result;
            }
            
            if (!result.AudioStreamCodecs.Any() || !result.VideoStreamCodecs.Any() || !result.Subtitles.Any())
            {
                FileInternalMetadata metadata = null;
                try
                {
                    metadata = await Ffprobe.Ffprobe.Instance.GetFileInternalMetadata(path, Log);
                } 
                catch (Exception)
                {
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = "Path is locked by other processes. Please try again later.";
                    Log.Info("Auto-organize Path is locked by other processes. Please try again later.");
                
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                    return result;
                }
                
                
                if (metadata != null)
                {
                    foreach (var stream in metadata.streams)
                    {
                        switch (stream.codec_type)
                        {
                            case "audio":
                                if (!string.IsNullOrEmpty(stream.codec_name) && !result.AudioStreamCodecs.Any())
                                {
                                    result.AudioStreamCodecs.Add(stream.codec_name);
                                }

                                break;
                            case "video":
                            {
                                if (!string.IsNullOrEmpty(stream.codec_long_name) && !result.VideoStreamCodecs.Any())
                                {
                                    var codecs = stream.codec_long_name.Split('/');
                                    foreach (var codec in codecs.Take(3))
                                    {
                                        if (codec.Trim().Split(' ').Length > 1)
                                        {
                                            result.VideoStreamCodecs.Add(codec.Trim().Split(' ')[0]);
                                            continue;
                                        }

                                        result.VideoStreamCodecs.Add(codec.Trim());
                                    }
                                }

                                if (string.IsNullOrEmpty(result.ExtractedResolution.Name))
                                {
                                    if (stream.width != 0 && stream.height != 0)
                                    {
                                        result.ExtractedResolution = new Resolution()
                                        {
                                            Name = GetResolutionFromMetadata(stream.width, stream.height),
                                            Width = stream.width,
                                            Height = stream.height
                                        };

                                    }
                                }
                                break;
                            }
                            case "subtitle":
                                
                                if (stream.tags != null && !result.Subtitles.Any())
                                {
                                    var language = stream.tags.title ?? stream.tags.language;
                                    if (!string.IsNullOrEmpty(language))
                                    {
                                        result.Subtitles.Add(language);
                                    }
                                }

                                break;
                        }
                    }
                }

                Log.Info($"Metadata extraction successful for {path}");
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                
            }

            if (LibraryMonitor.IsPathLocked(path.AsSpan()) && result.Status == FileSortingStatus.Processing)
            {
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return result;
            }

            //If a target folder has been calculated and the user has multiple locations for the series (Status: Waiting), just return the result. The user will have to organize with corrections.
            if (!string.IsNullOrEmpty(result.TargetPath) && result.Status == FileSortingStatus.Waiting)
            {
                return result;
            }

            //Looks lke we can access the file path.

            //If we have the proper data to sort the file, and the user has requested it. Sort it!
            if (!string.IsNullOrEmpty(result.TargetPath) && requestToMoveFile)
            {
                PerformFileSorting(options, result, cancellationToken);
                return result;
            }

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

                    seriesName = RegexExtensions.NormalizeMediaItemName(seriesName);

                    var seasonNumber = episodeInfo.SeasonNumber;

                    result.ExtractedSeasonNumber = seasonNumber;

                    var episodeNumber = episodeInfo.EpisodeNumber;

                    result.ExtractedEpisodeNumber = episodeNumber;

                    var premiereDate = episodeInfo.IsByDate ? new DateTime(episodeInfo.Year.Value, episodeInfo.Month.Value, episodeInfo.Day.Value) : (DateTime?)null;

                    if (episodeInfo.IsByDate || (seasonNumber.HasValue && episodeNumber.HasValue))
                    {
                        Log.Info($"Extracted information from {path}. Series name {seriesName}, {(seasonNumber.HasValue ? $": Season { seasonNumber.Value }," : " Can not determine season number,")} {(episodeNumber.HasValue ? $" Episode {episodeNumber.Value}." : " Can not determine episode number.")}");

                        var endingEpisodeNumber = episodeInfo.EndingEpisodeNumber;

                        result.ExtractedEndingEpisodeNumber = endingEpisodeNumber;

                        result.ExtractedName = seriesName;
                        result.ExtractedYear = seriesYear;

                        OrganizationService.SaveResult(result, cancellationToken);

                        Series series = null;

                        try
                        {
                            series = GetMatchingSeries(result.ExtractedName, seriesYear, result, cancellationToken);
                        }
                        catch (Exception)
                        {

                        }

                        if (series == null)
                        {
                            try
                            {
                                series = await GetSeriesRemoteProviderData(seriesName, seriesYear, options, cancellationToken);
                            }
                            catch(Exception)
                            {
                                Log.Warn("Provider Data Error...");
                            }
                        }

                        if (series == null) 
                        {
                            var msg = $"Unable to determine series from {path}";
                            result.Status = FileSortingStatus.Failure;
                            result.StatusMessage = msg;
                            Log.Warn(msg);
                            OrganizationService.SaveResult(result, cancellationToken);
                            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                            return result;
                        }

                        if (result.Status == FileSortingStatus.Waiting) //Series spans multiple folders. 
                        {
                            //We've set the message already when we tried to find Matching series, and found multiple matches.
                            //Just return the result
                            return result;
                        }


                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI

                        
                        Log.Info(requestToMoveFile ?  "User request to overwrite file" : "No overwrite file.");
                        OrganizeEpisode(requestToMoveFile, series, seasonNumber, episodeNumber, endingEpisodeNumber, premiereDate, options, false, result, cancellationToken);

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
            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI

            return result;
        }
        
      

        private Series CreateNewSeries(EpisodeFileOrganizationRequest request, BaseItem targetRootFolder, RemoteSearchResult result, AutoOrganizeOptions options)
        {
            var seriesFolderName = GetSeriesFolderName(request.Name, request.Year, options);

            var seriesName = request.Name ?? result.Name;
            var seriesPath = Path.Combine(targetRootFolder.Path, seriesFolderName);
            

            return new Series
            {
                Name = seriesName,
                Path = seriesPath,
                ProviderIds = request.ProviderIds ?? new ProviderIdDictionary(),
                ProductionYear = request.Year,
                InternalId = 0
                //TODO: DO we have to create a series ID here???
            };
        }

        public async void OrganizeWithCorrection(EpisodeFileOrganizationRequest request, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            var result = OrganizationService.GetResult(request.ResultId);

            //We have to check if the user select the same destination we already calculated for them... that could happen apparently.
            if(result.TargetPath.Substring(0, request.TargetFolder.Length) == request.TargetFolder)
            {
                PerformFileSorting(options, result, cancellationToken);
                if (!request.RememberCorrection) return;
                Log.Info($"Adding {result.ExtractedName} to Smart List");
                SaveSmartMatchString(FileSystem.GetFileNameWithoutExtension(result.OriginalPath), request.Name, result, cancellationToken);
                return;
            }



            //Update the result with possible missing information from the request.
            if (!result.ExtractedEpisodeNumber.HasValue && request.EpisodeNumber.HasValue)
            {
                result.ExtractedEpisodeNumber = request.EpisodeNumber.Value;
            }

            if (!result.ExtractedSeasonNumber.HasValue && request.SeasonNumber.HasValue)
            {
                result.ExtractedSeasonNumber = request.SeasonNumber.Value;
            }

            
            //Update the name it should be correct at this point
            result.ExtractedName = request.Name;


            
           
            if (request.CreateNewDestination || 
                string.IsNullOrEmpty(result.TargetPath) || !string.IsNullOrEmpty(result.TargetPath) &&  
                !string.IsNullOrEmpty(request.TargetFolder) && result.TargetPath.Substring(0, request.TargetFolder.Length) != request.TargetFolder)
            {
                Log.Info("Sorting request to create a new series Destination.");

                var seriesFolderName = GetSeriesFolderName(request.Name, request.Year, options);
                var seasonFolderName = GetSeasonFolderName(result.ExtractedSeasonNumber, options);

                if (string.IsNullOrEmpty(seasonFolderName) || string.IsNullOrEmpty(seriesFolderName))
                {
                   var msg = ($"Unable to determine series and season folder name from file name: {result.OriginalPath}");
                   result.StatusMessage = msg;
                   Log.Warn(msg);
                   result.Status = FileSortingStatus.Failure;
                   OrganizationService.SaveResult(result, cancellationToken);
                   EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                   return;

                }


                var episodeFileName = string.Empty;
                Series series = null;

                if (string.IsNullOrEmpty(result.TargetPath))
                {
                    series = GetMatchingSeries(request.Name, request.Year, result, cancellationToken);
                    
                    if (series is null)
                    {
                        series = await GetSeriesRemoteProviderData(request.Name, request.Year, options, cancellationToken);
                    }

                    if (series is null)
                    {
                        var msg = ("Unable to parse series info while organizing with corrections. Stopping Organization...");
                        result.Status = FileSortingStatus.Failure;
                        result.StatusMessage = msg;
                        Log.Warn(msg);
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }

                    Episode episode = null;

                    try
                    {
                        episode = await GetEpisodeRemoteProviderData(series, request.SeasonNumber, request.EpisodeNumber, request.EndingEpisodeNumber, null, cancellationToken);
                        
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Provider Data Error", ex);
                    }


                    if (episode is null)
                    {
                        var msg = $"Unable to determine episode from {result.OriginalPath}";
                        result.Status = FileSortingStatus.Failure;
                        result.StatusMessage = msg;
                        Log.Warn(msg);
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }

                    result.ExtractedEpisodeName = episode.Name;
                    episodeFileName = GetEpisodeFileName(result, request.Name, request.SeasonNumber, request.EpisodeNumber, request.EndingEpisodeNumber, episode.Name, options);
                    
                } 
                else
                {
                    episodeFileName = Path.GetFileName(result.TargetPath);
                }

                if (string.IsNullOrEmpty(episodeFileName))
                {
                    var msg = $"Unable to determine episode from {result.OriginalPath}";
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = msg;
                    Log.Warn(msg);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }
                
                var path = Path.Combine(request.TargetFolder, Path.Combine(seriesFolderName, seasonFolderName, episodeFileName));

                
                result.TargetPath = path;
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                Log.Info($"{result.Type} -  {result.ExtractedName} Target path: {result.TargetPath}");
                

                PerformFileSorting(options, result, cancellationToken);

                if (!request.RememberCorrection) return;

                //var seriesQuery = LibraryManager.GetItemsResult(new InternalItemsQuery()
                //{
                //    IncludeItemTypes = new []{ nameof(Series) },
                //    Recursive = true
                //});

                //var series = seriesQuery.Items.Cast<Series>().FirstOrDefault(s => RegexExtensions.NormalizeString(s.Name).ContainsIgnoreCase(RegexExtensions.NormalizeString(request.Name)));

                if (series != null)
                {
                    Log.Info($"Adding {result.ExtractedName} to Smart List");
                    SaveSmartMatchString(FileSystem.GetFileNameWithoutExtension(result.OriginalPath), series.Name, result, cancellationToken);
                }
                else
                {
                    Log.Info($"Series does not yet exist in the library. Can not add {result.ExtractedName} to Smart List. Try again later.");
                    
                }


                return;
            }
            

            try
            {
                Series series = null;

                if (!string.IsNullOrEmpty(request.SeriesId))
                {
                    series = (Series)LibraryManager.GetItemById(request.SeriesId);
                }

                if (series is null) //Try to match the series
                {
                    
                    try
                    {
                        series = GetMatchingSeries(result.ExtractedName, result.ExtractedYear, result, cancellationToken);
                    }
                    catch (Exception)
                    {

                    }
                }

                if (series is null) //No matching Series?...Create a new series here.
                {
                    BaseItem targetFolder = null;
                    if (!string.IsNullOrEmpty(options.DefaultSeriesLibraryPath))
                    {
                        targetFolder = LibraryManager.FindByPath(options.DefaultSeriesLibraryPath, true);
                    }
                    else
                    {
                        result.Status = FileSortingStatus.Failure;
                        const string msg = "Auto Organize settings: default library not set for TV Shows.";
                        result.StatusMessage = msg;
                        Log.Warn(msg);
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }

                    series = CreateNewSeries(request, targetFolder, null, options);
                }
                
                
                //if (request.ProviderIds.Count > 0)
                //{
                //    //This is a new series to be created using the default library path from the settings
                //    if (!string.IsNullOrEmpty(options.DefaultSeriesLibraryPath))
                //    {
                //        targetFolder = LibraryManager.FindByPath(options.DefaultSeriesLibraryPath, true);
                //    }
                //    else
                //    {
                //        result.Status = FileSortingStatus.Failure;
                //        var msg = "Auto Organize settings: default library not set for TV Shows.";
                //        result.StatusMessage = msg;
                //        Log.Warn(msg);
                //        OrganizationService.SaveResult(result, cancellationToken);
                //        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                //        return;
                //    }

                //    series =  GetMatchingSeries(request.Name, request.Year, null, cancellationToken) ??
                //              CreateNewSeries(request, targetFolder, null, options, cancellationToken);
                //}

                //if (series == null)
                //{
                //    // Existing Series
                //    series = (Series)LibraryManager.GetItemById(request.SeriesId);
                //}

                //if (request.RememberCorrection)
                //{
                //    Log.Info($"Adding {result.ExtractedName} to Smart List");
                //    SaveSmartMatchString(FileSystem.GetFileNameWithoutExtension(result.OriginalPath), series, result, cancellationToken);

                //}

                //This will always be true because the user has chosen the proper provider data from the fileorganizer. We'll force the sort.
                const bool overwriteFile = true; 

                OrganizeEpisode(overwriteFile, series, request.SeasonNumber,
                    request.EpisodeNumber, request.EndingEpisodeNumber, null, options, request.RememberCorrection, result, cancellationToken);

                OrganizationService.SaveResult(result, CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (result.Status == FileSortingStatus.Waiting)
                {
                    return;
                }
                
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
            }

            //return;
        }
    
        private async void OrganizeEpisode(bool overwriteFile, Series series, int? seasonNumber, int? episodeNumber, int? endingEpisodeNumber, DateTime? premiereDate, AutoOrganizeOptions options, bool rememberCorrection, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            var sourcePath = result.OriginalPath;

            var episode = GetMatchingEpisode(series, seasonNumber, episodeNumber, endingEpisodeNumber, result, premiereDate, cancellationToken);

            if (episode is null) 
            {
                try
                {
                    episode = await GetEpisodeRemoteProviderData(series, seasonNumber, episodeNumber, endingEpisodeNumber, premiereDate, cancellationToken);
                }
                catch(Exception)
                {
                    Log.Warn("Exceeded Provider limits. Try again later...");

                }
            }

            if (episode is null)
            {
                var msg = $"Unable to determine episode from {sourcePath}";
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = msg;
                Log.Warn(msg);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return;
            }
            
            var season = episode.Season ?? GetMatchingSeason(series, result, options);
            
            
            var episodeFileName = GetEpisodeFileName(result, series.Name, season.IndexNumber, episode.IndexNumber, episode.IndexNumberEnd, episode.Name, options);
            episode.Path = Path.Combine(season.Path, episodeFileName);

           
            if (string.IsNullOrEmpty(episode.Path))
            {
                var msg = $"Unable to sort {sourcePath} because target path could not be determined.";
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = msg;
                Log.Warn(msg);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return;
            }

            result.TargetPath = episode.Path;
            result.ExtractedEpisodeName = episode.Name;
            //OrganizationService.SaveResult(result, cancellationToken);

            Log.Info($"Episode Target Path : {result.TargetPath}");

            
            Log.Info("Beginning Episode Organization");
            Log.Info("Sorting file {0} into series {1}", sourcePath, series.Name);
            

            bool isNew = string.IsNullOrWhiteSpace(result.Id);

            if (isNew)
            {
                OrganizationService.SaveResult(result, cancellationToken);
            }




            try
            {
                // Proceed to sort the file

                var fileExists = FileSystem.FileExists(result.TargetPath);
                
                var existingEpisodeFilesButWithDifferentPath = GetExistingEpisodeFilesButWithDifferentPath(result.TargetPath, series, episode);
                result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;

                //The source path might be in use. The file could still be copying from it's origin location into watched folder. Status maybe "InUse"
                if(IsCopying(sourcePath, FileSystem) && !result.IsInProgress && result.Status != FileSortingStatus.Processing)
                {
                    var msg = $"File '{sourcePath}' is currently in use, stopping organization";
                    Log.Info(msg);
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = msg;
                    OrganizationService.SaveResult(result, cancellationToken);
                    //OrganizationService.RemoveFromInprogressList(result);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }

                
                //User is forcing sorting from the UI - Sort it!
                if (overwriteFile) 
                {
                    Log.Info("Request To Overwrite Episode: " + overwriteFile);

                    RemoveExistingLibraryFiles(existingEpisodeFilesButWithDifferentPath, result);

                    PerformFileSorting(options, result, cancellationToken);

                    if (rememberCorrection)
                    {
                        SaveSmartMatchString(FileSystem.GetFileNameWithoutExtension(sourcePath), series.Name, result, cancellationToken);
                    }
                    return;
                }

                
                //Five phases:
                //1. Overwrite existing files option is unchecked - The key words input is empty - the file already exists in the library - no files will be overwritten - mark as existing - stop organization.
                //2. Overwrite existing files option is checked - Doesn't matter about key words - the file already exists in the library - any file will overwrite the library item.
                //3. Overwrite existing files option is unchecked - Key words inputs have values - the file already exists in the library - only items with key words in the file name will overwrite the library item.
                //4. a)  The file doesn't exist in the library - is a new episode for an existing series - auto sorting is turned on - Sort episodes for existing series only is turned on - sort it!
                //   b)  The file doesn't exist in the library - is a new episode for a new series - auto sorting is turned on - Sort episodes for existing series only is turned off - Mark the file as NewMedia
                //5. The file doesn't exist in the library - is new - auto sorting is turned off - Mark the file as NewMedia

                //1.
                if (!options.OverwriteExistingEpisodeFiles && !options.OverwriteExistingEpisodeFilesKeyWords.Any() && fileExists) 
                {
                    var msg = $"File '{sourcePath}' already already exists at '{result.TargetPath}', stopping organization";
                    Log.Info(msg);
                    result.Status = FileSortingStatus.SkippedExisting;
                    result.StatusMessage = msg;
                    result.ExistingInternalId = LibraryManager.FindIdByPath(result.TargetPath, false);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                   
                }
                
                //2.
                if (options.OverwriteExistingEpisodeFiles && fileExists && options.AutoDetectSeries)
                {
                    RemoveExistingLibraryFiles(existingEpisodeFilesButWithDifferentPath, result);

                    PerformFileSorting(options, result, cancellationToken);

                    if (rememberCorrection)
                    {
                        SaveSmartMatchString(FileSystem.GetFileNameWithoutExtension(sourcePath), series.Name, result, cancellationToken);
                    }
                    return;
                }

                //3.
                if (!options.OverwriteExistingEpisodeFiles && options.OverwriteExistingEpisodeFilesKeyWords.Any() && fileExists && options.AutoDetectSeries)
                {
                    if (options.OverwriteExistingEpisodeFilesKeyWords.Any(word => result.OriginalFileName.ContainsIgnoreCase(word)))
                    {
                        RemoveExistingLibraryFiles(existingEpisodeFilesButWithDifferentPath, result);

                        PerformFileSorting(options, result, cancellationToken);

                        if (rememberCorrection)
                        {
                            SaveSmartMatchString(FileSystem.GetFileNameWithoutExtension(sourcePath), series.Name, result, cancellationToken);
                        }
                        return;
                    }

                    var msg = $"File '{sourcePath}' already exists as these:'{string.Join("', '", existingEpisodeFilesButWithDifferentPath)}'. Stopping organization";
                    Log.Info(msg);
                    result.Status = FileSortingStatus.SkippedExisting;
                    result.StatusMessage = msg;
                    result.ExistingInternalId = LibraryManager.FindIdByPath(result.TargetPath, false);
                    result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;
                    OrganizationService.SaveResult(result, cancellationToken);
                    //OrganizationService.RemoveFromInprogressList(result);
                   
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }

                //4.
                if (!fileExists && options.AutoDetectSeries)
                {
                    if (IsNewSeries(series) && options.SortExistingSeriesOnly)
                    {
                        var msg = $"Auto detect disabled for new series. File '{sourcePath}' will wait for user interaction. Stopping organization";
                        Log.Info(msg);
                        result.Status = FileSortingStatus.NewMedia;
                        result.StatusMessage = msg;
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }
                     
                    PerformFileSorting(options, result, cancellationToken);

                    if (rememberCorrection)
                    {
                        SaveSmartMatchString(FileSystem.GetFileNameWithoutExtension(sourcePath), series.Name, result, cancellationToken);
                    }

                    return;
                }

                //5.
                if (!options.AutoDetectSeries && !fileExists)
                {
                    var msg = $"Series Auto detect disabled. File '{sourcePath}' will wait for user interaction. Stopping organization";
                    Log.Info(msg);
                    result.Status = FileSortingStatus.NewMedia;
                    result.StatusMessage = msg;
                    OrganizationService.SaveResult(result, cancellationToken);
                    //OrganizationService.RemoveFromInprogressList(result);
                   
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }


            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                Log.Warn(ex.Message);
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.RemoveFromInprogressList(result);
                //TODO: Replace this return... maybe
                //return;
            }


        }

        private void RemoveExistingLibraryFiles(List<string> existingEpisodeFilesButWithDifferentPath, FileOrganizationResult result)
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
        }

        private void SaveSmartMatchString(string matchString, string seriesName, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(matchString) || matchString.Length < 3)
            {
                Log.Info($"Match string: {matchString} is invalid.");
                return;
            }

            var smartInfo = OrganizationService.GetSmartMatchInfos();
            
            var info = smartInfo.Items.FirstOrDefault(i => string.Equals(i.Name, seriesName, StringComparison.OrdinalIgnoreCase));

            if (info == null)
            {
                info = new SmartMatchResult
                {
                    Name = seriesName,
                    OrganizerType = FileOrganizerType.Episode,
                    Id = seriesName.GetMD5().ToString("N")
                };
            }

            if (!info.MatchStrings.Contains(matchString, StringComparer.OrdinalIgnoreCase))
            {
                info.MatchStrings.Add(matchString);
                OrganizationService.SaveResult(info, cancellationToken);
            }

            Log.Info($"Match string saved {info.Name} - {matchString}");
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
            Log.Info("Processing " + result.TargetPath);
            result.Status = FileSortingStatus.Processing;
            result.StatusMessage = "";
            result.FileSize = FileSystem.GetFileInfo(result.OriginalPath).Length; //Update the file size so it will show the actual size of the file here. It may have been copying before.
            Log.Info($"Auto organize adding {result.TargetPath} to inprogress list");
            OrganizationService.SaveResult(result, cancellationToken);
            OrganizationService.AddToInProgressList(result, true);
            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
            
            // We should probably handle this earlier so that we never even make it this far
            // Yup, because if OverwriteExisting files is turned on, and these two paths are the same, it will have deleted the source file.
            if (string.Equals(result.OriginalPath, result.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                const string warnMsg = "Source path and target path can not be the same";
                Log.Warn(warnMsg);
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = warnMsg;
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.RemoveFromInprogressList(result);
               
                //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
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
                    Log.Info(targetAlreadyExists ? "Overwriting Existing Destination File" : "Copying File");
                    
                    try
                    {
                        //Remove the existing library file(s) and make room for this new one
                        if (targetAlreadyExists)
                        {
                            RemoveExistingLibraryFiles(result.DuplicatePaths, result);
                            try
                            {
                                FileSystem.DeleteFile(result.TargetPath);
                            }
                            catch
                            {

                            }
                        }

                        try
                        {
                            FileSystem.CopyFile(result.OriginalPath, result.TargetPath, true);
                        }
                        catch (Exception ex)
                        {
                            if (ex.Message.Contains("disk space"))
                            {
                                Log.Warn(ex.Message);
                                result.Status = FileSortingStatus.NotEnoughDiskSpace;
                                result.StatusMessage = "There is not enough disk space on the drive to move this file";
                                OrganizationService.SaveResult(result, cancellationToken);
                                OrganizationService.RemoveFromInprogressList(result);
                                
                                //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                                return;
                            }
                        }   

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
                        OrganizationService.SaveResult(result, cancellationToken);
                        OrganizationService.RemoveFromInprogressList(result);
                        
                        //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }
                   
                }
                else
                {
                    Log.Info("Moving File");
                    try
                    {
                        //Remove the existing library file(s) and make room for this new one
                        if (targetAlreadyExists)
                        {
                            RemoveExistingLibraryFiles(result.DuplicatePaths, result);
                            try
                            {
                                FileSystem.DeleteFile(result.TargetPath);
                            }
                            catch
                            {

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
                       OrganizationService.SaveResult(result, cancellationToken);
                       OrganizationService.RemoveFromInprogressList(result);
                       
                       //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                       LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
                       return;
                    } 
                   
                }

                result.Status = FileSortingStatus.Success;
                result.StatusMessage = string.Empty;
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.RemoveFromInprogressList(result);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);

            }
            catch (IOException ex)
            {
                if(ex.Message.Contains("being used by another process"))
                {                    
                    var errorMsg = $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse; //We're waiting for the file to become available.
                    result.StatusMessage = errorMsg;
                    Log.ErrorException(errorMsg, ex);
                    OrganizationService.SaveResult(result, cancellationToken);
                    OrganizationService.RemoveFromInprogressList(result);
                   
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
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
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.RemoveFromInprogressList(result);
               
                //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
                return;
            }
            
            
            LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
            

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
                        .FirstOrDefault(e => e.IndexNumber == result.ExtractedSeasonNumber);
                    //&& e.LocationType == LocationType.FileSystem);
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
                    season.Path = Path.Combine(series.Path, GetSeasonFolderName(result.ExtractedSeasonNumber ?? season.IndexNumber, options));
                }
                
                //season.Path = GetSeasonFolderPath(series, episode.ParentIndexNumber.Value, options);
            }

            return season;
        }
        
        private Series GetMatchingSeries(string seriesName, int? seriesYear, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            
            Log.Info($"Parsing Library for matching Series: {seriesName}");
            var resultSeries = new List<BaseItem>();
            
            var series = LibraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Series) },
                Recursive = true,
                DtoOptions = new DtoOptions(true),
                Years = seriesYear.HasValue ? new[] { seriesYear.Value } : Array.Empty<int>()

            });

            if (series.Any())
            {
                resultSeries = series.Where(s => RegexExtensions.NormalizeSearchStringComparison(s.Name) == (RegexExtensions.NormalizeSearchStringComparison(seriesName))).ToList();
                
                if (!resultSeries.Any())
                {
                    resultSeries = series.Where(s => RegexExtensions.NormalizeSearchStringComparison(s.Name).ContainsIgnoreCase(RegexExtensions.NormalizeSearchStringComparison(seriesName))).ToList();
                }
            }
            
            
            //We can't read this series name try the smart list
            if (!resultSeries.Any())
            {
                Log.Info($"Parsing Smart List Info to find series name: {seriesName}");
                
                var smartMatch = new QueryResult<SmartMatchResult>();

                try
                {
                    smartMatch = OrganizationService.GetSmartMatchInfos();
                }
                catch(Exception)
                {

                }

                if (smartMatch.TotalRecordCount == 0) return null;

                var nameToCompare = FileSystem.GetFileNameWithoutExtension(result.OriginalPath);
                
                var info = smartMatch.Items.FirstOrDefault(smartMatchInfo => smartMatchInfo.MatchStrings.Any(match => RegexExtensions.NormalizeSearchStringComparison(match).Contains(RegexExtensions.NormalizeSearchStringComparison(nameToCompare))));

                if (info == null)
                {
                    return null;
                }

                Log.Info($"Smart List entry found for {result.ExtractedName}: {(info.Name)}");

                series = LibraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Series) },
                    Recursive = true,
                    Name = info.Name,
                    DtoOptions = new DtoOptions(true)
                });

                if (series.Any())
                {
                    resultSeries = series.ToList();
                }
            }

            if (resultSeries.Count == 1)
            {
                result.ExtractedName = resultSeries[0].Name;
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return resultSeries.Cast<Series>().FirstOrDefault();
            }

            if (resultSeries.Count > 1)
            {
                //Mark the result as having more then one location in the library. Needs user input, set it to "Waiting"
                result.Status = FileSortingStatus.Waiting;
                const string msg = "Item has more then one possible destination folders. Waiting to sort file...";
                result.StatusMessage = msg;
                Log.Warn(msg);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return resultSeries.Cast<Series>().FirstOrDefault();;
            }

            Log.Info("New Series Detected...");

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
                SeriesProviderIds = series.ProviderIds ?? new ProviderIdDictionary(),
            };

            if (premiereDate.HasValue) episodeInfo.PremiereDate = premiereDate;


            IEnumerable<RemoteSearchResult> searchResults;

            try
            {
                searchResults = await ProviderManager.GetRemoteSearchResults<Episode, EpisodeInfo>(new RemoteSearchQuery<EpisodeInfo>
                {
                    SearchInfo = episodeInfo,
                    IncludeDisabledProviders = true

                }, series, cancellationToken);
            }
            catch (Exception)
            {
                throw new Exception(); //We'll catch this later
            }

            RemoteSearchResult episodeResults = null;
            if (searchResults != null)
            {
                episodeResults = searchResults.FirstOrDefault();
            }

            if (episodeResults == null)
            {
                var msg = $"No provider metadata found for {series.Name} season {seasonNumber} episode {episodeNumber}";
                Log.Warn(msg);
                return null;
            }

            seasonNumber = seasonNumber ?? episodeResults.ParentIndexNumber;
            episodeNumber = episodeNumber ?? episodeResults.IndexNumber;
            endingEpisodeNumber = endingEpisodeNumber ?? episodeResults.IndexNumberEnd;

            var episode = new Episode
            {
                ParentIndexNumber = seasonNumber,
                IndexNumber = episodeNumber,
                IndexNumberEnd = endingEpisodeNumber,
                ProviderIds = episodeResults.ProviderIds,
                Name = episodeResults.Name,
            };

            if(!IsNewSeries(series)) episode.SeriesId = series.InternalId; //New series will have an internalId of 0 - ignore this

            return episode;
        }
        private async Task<Series> GetSeriesRemoteProviderData(string seriesName, int? seriesYear, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            
            
            string metadataLanguage = null;
            string metadataCountryCode = null;
            BaseItem targetRootFolder = null;

            if (!string.IsNullOrEmpty(options.DefaultSeriesLibraryPath))
            {
                targetRootFolder = LibraryManager.FindByPath(options.DefaultSeriesLibraryPath, true);
            } 
            else
            {
                const string msg = "Auto Organize settings: default library not set for TV Shows.";
                Log.Warn(msg);
                return null;
            }

            if (targetRootFolder != null)
            {
                metadataLanguage = targetRootFolder.GetPreferredMetadataLanguage();
                metadataCountryCode = targetRootFolder.GetPreferredMetadataCountryCode();
            }

            var seriesInfo = new SeriesInfo
            {
                Name = seriesName,
                MetadataCountryCode = metadataCountryCode ?? "",
                MetadataLanguage = metadataLanguage ?? ""
            };

            if (seriesYear.HasValue) seriesInfo.Year = seriesYear.Value;


            IEnumerable<RemoteSearchResult> searchResults = null;
            try
            {
                searchResults = await ProviderManager.GetRemoteSearchResults<Series, SeriesInfo>(
                    new RemoteSearchQuery<SeriesInfo>
                    {
                        SearchInfo = seriesInfo

                    }, targetRootFolder, cancellationToken);

            }
            catch (Exception)
            {
                Log.Warn("Provider limits reached.");
            }

            RemoteSearchResult finalResult = null;

            if (searchResults != null)
            {
                finalResult = searchResults.FirstOrDefault(result => RegexExtensions.NormalizeSearchStringComparison(result.Name).Contains(RegexExtensions.NormalizeSearchStringComparison(seriesName)));
            }
            
            if (finalResult != null)
            {
                // We are in the good position, we can create the item
                var organizationRequest = new EpisodeFileOrganizationRequest
                {
                    Name = finalResult.Name,
                    ProviderIds = finalResult.ProviderIds,
                    Year = finalResult.ProductionYear,
                    TargetFolder = options.DefaultSeriesLibraryPath
                };

                 
                var series =  GetMatchingSeries(organizationRequest.Name, organizationRequest.Year, null, cancellationToken);
            
                return series ?? CreateNewSeries(organizationRequest, targetRootFolder, finalResult, options);
            }

            return null;
        }


        private string GetSeriesFolderName(string seriesName, int? seriesYear, AutoOrganizeOptions options)
        {
            var seriesFullName = seriesName;

            if (seriesYear.HasValue)
            {
                if (!seriesFullName.Contains($"({seriesYear})")) //Don't add the year to the name if it already has the year in it. Yup, it can happen.
                {
                    seriesFullName = $"{seriesFullName} ({seriesYear})";
                }
               
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
        private string GetSeasonFolderName(int? seasonNumber, AutoOrganizeOptions options)
        {
            if (!seasonNumber.HasValue)
            {
                return null;
            }
            if (seasonNumber == 0)
            {
                //return Path.Combine(path, FileSystem.GetValidFilename(options.SeasonZeroFolderName));
                return FileSystem.GetValidFilename(options.SeasonZeroFolderName);
            }

            var seasonFolderName = options.SeasonFolderPattern
                .Replace("%s", seasonNumber.Value.ToString())
                .Replace("%0s", seasonNumber.Value.ToString("00", new CultureInfo("en-US")))
                .Replace("%00s", seasonNumber.Value.ToString("000", new CultureInfo("en-US")));

            //return Path.Combine(path, FileSystem.GetValidFilename(seasonFolderName));
            return FileSystem.GetValidFilename(seasonFolderName);
        }
        private string GetEpisodeFileName(FileOrganizationResult result, string seriesName, int? seasonIndexNumber, int? episodeIndexNumber, int? episodeIndexNumberEnd, string episodeName, AutoOrganizeOptions options)
        {
            seriesName = FileSystem.GetValidFilename(seriesName).Trim();

            var episodeTitle = FileSystem.GetValidFilename(episodeName).Trim();

            if (!episodeIndexNumber.HasValue || !seasonIndexNumber.HasValue)
            {
                
                var msg = "GetEpisodeFileName: Mandatory param as missing!";
                Log.Error(msg);
                return null;

            }

            var endingEpisodeNumber = episodeIndexNumberEnd;
            var episodeNumber = episodeIndexNumber.Value;
            var seasonNumber = seasonIndexNumber.Value;

            var sourceExtension = (Path.GetExtension(result.OriginalPath) ?? string.Empty).TrimStart('.');

            var pattern = endingEpisodeNumber.HasValue ? options.MultiEpisodeNamePattern : options.EpisodeNamePattern;

            if (string.IsNullOrWhiteSpace(pattern))
            {
                var msg = "Configured episode name pattern is empty!";
                Log.Warn(msg);
                return null;
            }

            var filename = pattern.Replace("%sn", seriesName)
                .Replace("%s.n", seriesName.Replace(" ", "."))
                .Replace("%s_n", seriesName.Replace(" ", "_"))
                .Replace("%s", seasonNumber.ToString(_usCulture))
                .Replace("%0s", seasonNumber.ToString("00", _usCulture))
                .Replace("%00s", seasonNumber.ToString("000", _usCulture))
                .Replace("%ext", sourceExtension)
                .Replace("%en", "%#1")
                .Replace("%res", result.ExtractedResolution.Name ?? "")
                .Replace("%e.n", "%#2")
                .Replace("%e_n", "%#3")
                .Replace("%fn", Path.GetFileNameWithoutExtension(result.OriginalPath));

            if (endingEpisodeNumber.HasValue)
            {
                filename = filename.Replace("%ed", endingEpisodeNumber.Value.ToString(_usCulture))
                .Replace("%0ed", endingEpisodeNumber.Value.ToString("00", _usCulture))
                .Replace("%00ed", endingEpisodeNumber.Value.ToString("000", _usCulture));
            }

            filename = filename.Replace("%e", episodeNumber.ToString(_usCulture))
                .Replace("%0e", episodeNumber.ToString("00", _usCulture))
                .Replace("%00e", episodeNumber.ToString("000", _usCulture));

            if (filename.Contains("%#"))
            {
                filename = filename.Replace("%#1", episodeTitle)
                    .Replace("%#2", episodeTitle.Replace(" ", "."))
                    .Replace("%#3", episodeTitle.Replace(" ", "_"));
            }

            // Finally, call GetValidFilename again in case user customized the episode expression with any invalid filename characters
            //return Path.Combine(season.Path, FileSystem.GetValidFilename(result).Trim());
            return FileSystem.GetValidFilename(filename).Trim();

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

        
        private string GetResolutionFromMetadata(int width, int height)
        {
            var diagonal = Math.Round(Math.Sqrt(Math.Pow(width, 2) + Math.Pow(height,2)), 2);

            if (diagonal <= 800) return "480p"; //4:3
            if (diagonal > 800 && diagonal <= 1468.6) return "720p"; //16:9
            if (diagonal > 1468.6 && diagonal <=  2315.32) return "1080p"; //16:9 or 1:1.77
            if (diagonal >  2315.32 && diagonal <= 2937.21) return "1440p"; //16:9
            if (diagonal >  2937.21 && diagonal <= 4405.81) return "2160p"; //1:1.9 - 4K
            if (diagonal > 4405.81 && diagonal <= 8811.63) return "4320p"; //16∶9 - 8K

            return "Unknown";
        }
        //private string NormalizeString(string value)
        //{
        //    return Regex.Replace(value, @"(\s+|@|&|'|:|\(|\)|<|>|#|-|\.|\band\b|,)", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
        //}


    }
}