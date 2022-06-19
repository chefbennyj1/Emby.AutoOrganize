using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Data;
using Emby.AutoOrganize.FileMetadata;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Model.Organization;
using Emby.AutoOrganize.Model.SmartLists;
using Emby.AutoOrganize.Model.SmartMatch;
using Emby.AutoOrganize.Naming;
using Emby.AutoOrganize.Naming.Common;
using Emby.AutoOrganize.Naming.TV;
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

        private string FormatLogMsg(string msg)
        {
            msg.Replace("<br/>", " ");
            return msg;
        }
        
        private bool IsSameEpisode(string sourcePath, string newPath)
        {
            try
            {
                if(Path.GetExtension(sourcePath) != Path.GetExtension(newPath))
                {
                    return false;
                }

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

                Log.Info("Auto organize checking for tv series: " + result.TargetPath);
                result.Status = FileSortingStatus.Checking;
                result.Date = DateTime.UtcNow; //Update the Date so that it moves to the top of the list in the UI (UI table is sorted by date)
                result.StatusMessage = $"File {result.TargetPath} is currently being analysed";
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.AddToInProgressList(result, true);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI

            }


            //Check to see if we can access the file path, or if the file path is being used.
            if (LibraryMonitor.IsPathLocked(path.AsSpan()) && result.Status != FileSortingStatus.Processing || IsCopying(path, FileSystem))
            {
                result.Status = FileSortingStatus.InUse;
                result.Date = DateTime.UtcNow; //Update the Date so that it moves to the top of the list in the UI (UI table is sorted by date)
                result.StatusMessage = "Path is locked by other processes. Please try again later.";
                Log.Info("Auto organize Path is locked by other processes. Please try again later.");
                
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                return result;
            }
            
            if (!result.AudioStreamCodecs.Any() || !result.VideoStreamCodecs.Any() || !result.Subtitles.Any())
            {

                var mediaInfo = new MediaInfo();
                try
                {
                    mediaInfo = await mediaInfo.GetMediaInfo(result.OriginalPath);
                }
                catch
                {
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = "Path is locked by other processes. Please try again later.";
                    Log.Info("Auto-organize Path is locked by other processes. Please try again later.");
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                    return result; 
                }

                result.AudioStreamCodecs = mediaInfo.AudioStreamCodecs;
                result.VideoStreamCodecs = mediaInfo.VideoStreamCodecs;
                result.Subtitles = mediaInfo.Subtitles;
                result.ExtractedResolution = mediaInfo.Resolution;

                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                
            }

            if (LibraryMonitor.IsPathLocked(path.AsSpan()) && result.Status == FileSortingStatus.Processing)
            {
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return result;
            }

            //If a target folder has been calculated and the user has multiple locations for the series (Status: UserInputRequired), just return the result. The user will have to organize with corrections.
            if (!string.IsNullOrEmpty(result.TargetPath) && result.Status == FileSortingStatus.UserInputRequired)
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

                    seriesName = Regex.Replace(seriesName, @"[^A-Za-z0-9\s+()]|[0-9]{3,4}[Pp]", " ", RegexOptions.IgnoreCase).Replace("  ", " ").Trim();
                    seriesName = new CultureInfo("en-US", false).TextInfo.ToTitleCase(seriesName.Trim());

                    var seasonNumber = episodeInfo.SeasonNumber;

                    result.ExtractedSeasonNumber = seasonNumber;

                    var episodeNumber = episodeInfo.EpisodeNumber;

                    result.ExtractedEpisodeNumber = episodeNumber;

                    var premiereDate = episodeInfo.IsByDate ? new DateTime(episodeInfo.Year.Value, episodeInfo.Month.Value, episodeInfo.Day.Value) : (DateTime?)null;

                    if (episodeInfo.IsByDate || (seasonNumber.HasValue && episodeNumber.HasValue))
                    {
                        string epNumber = episodeInfo.EndingEpisodeNumber > 0 ? string.Concat(episodeInfo.EpisodeNumber, '-', episodeInfo.EndingEpisodeNumber) : $"{episodeInfo.EpisodeNumber}";
                        Log.Info($"Extracted information from {path}. Series name {seriesName}, {(seasonNumber.HasValue ? $": Season { seasonNumber.Value }," : " Can not determine season number,")} {(episodeNumber.HasValue ? $" Episode {epNumber}." : " Can not determine episode number.")}");
                        
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

                        //If finding a matching series in the library  was unsuccessful
                        //If the extracted series name has 3 or less characters, the user should create a smart list match for the item.
                        //ex. SNL = Saturday Night Live.
                        //We should not attempt to sort this item.
                        //We will mark the item as new media.
                        if (series == null && seriesName.Length <= 3)
                        {
                            result.Status = FileSortingStatus.UserInputRequired;
                            var msg = $"Unable to determine Series.<br/>A Smart Match should be created for {seriesName}.<br/><br/>Please Organize with corrections to create the Smart Match entry.";
                            result.StatusMessage = msg;
                            Log.Warn(FormatLogMsg(msg));
                            OrganizationService.SaveResult(result, cancellationToken);
                            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                            return result;
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
                            var msg = $"Unable to determine Series.<br/>{path} extracted as {seriesName}.<br/><br/>Please Organize with corrections to create the Smart Match entry.";
                            result.Status = FileSortingStatus.Failure;
                            result.StatusMessage = msg;
                            Log.Warn(FormatLogMsg(msg));
                            OrganizationService.SaveResult(result, cancellationToken);
                            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                            return result;
                        }

                        if (result.Status == FileSortingStatus.UserInputRequired) //Series spans multiple folders. 
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
                        Log.Warn(FormatLogMsg(msg));

                    }
                }
                else
                {
                    var msg = $"Unable to determine series name from {path}";
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = msg;
                    Log.Warn(FormatLogMsg(msg));
                    
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
                if (result.Status != FileSortingStatus.UserInputRequired) //Waiting when the series spans multiple locations, and we need user input.
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
            //if (result.TargetPath.Substring(0, request.TargetFolder.Length) == request.TargetFolder)
            //{
            //    result.ExtractedName = request.Name;
            //    result.ExtractedYear = request.Year;

            //    //PerformFileSorting(options, result, cancellationToken);
            //    //if (!request.RememberCorrection) return;
            //    //Log.Info($"Adding {result.ExtractedName} to Smart List");
            //    //SaveSmartMatchString(FileSystem.GetFileNameWithoutExtension(result.OriginalPath), request.Name, result, cancellationToken);
            //    //return;
            //}




            //Update the result with possible missing information from the request.
            if (!result.ExtractedEpisodeNumber.HasValue || result.ExtractedEpisodeNumber != request.EpisodeNumber)
            {
                if (request.EpisodeNumber.HasValue) result.ExtractedEpisodeNumber = request.EpisodeNumber.Value;
            }

            if (!result.ExtractedSeasonNumber.HasValue || result.ExtractedSeasonNumber != request.SeasonNumber)
            {
                if (request.SeasonNumber.HasValue) result.ExtractedSeasonNumber = request.SeasonNumber.Value;
            }

            //Update the name it should be correct at this point
            result.ExtractedName = request.Name;
            
            if (request.CreateNewDestination || string.IsNullOrEmpty(result.TargetPath) || !string.IsNullOrEmpty(result.TargetPath) &&  
                !string.IsNullOrEmpty(request.TargetFolder) && result.TargetPath.Substring(0, request.TargetFolder.Length) != request.TargetFolder)
            {
                Log.Info("Sorting request to create a new series Destination.");

                var seriesFolderName = GetSeriesFolderName(request.Name, request.Year, options);
                var seasonFolderName = GetSeasonFolderName(result.ExtractedSeasonNumber, options);

                if (string.IsNullOrEmpty(seasonFolderName) || string.IsNullOrEmpty(seriesFolderName))
                {
                   var msg = ($"Unable to determine series and season folder name from file name: {result.OriginalPath}");
                   result.StatusMessage = msg;
                   Log.Warn(FormatLogMsg(msg));
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
                        Log.Warn(FormatLogMsg(msg));
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }

                    Episode episode = null;

                    try
                    {
                        episode = await GetEpisodeRemoteProviderData(series, request.SeasonNumber, request.EpisodeNumber, request.EndingEpisodeNumber, null, options, cancellationToken);
                        
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Provider Data Error", ex);
                    }


                    if (episode is null)
                    {
                        string epNumber = request.EndingEpisodeNumber > 0 ? string.Concat(request.EpisodeNumber, '-', request.EndingEpisodeNumber) : $"{request.EpisodeNumber}";
                        var msg = $"No provider metadata found for {series.Name} Season {request.SeasonNumber} Episode {epNumber}.<br/>You have either exceeded provider limits or the provider does not have episode information.<br/>Please try again later and/or manually check your providers have the episode available.<br/>(REF:1)";
                        result.Status = FileSortingStatus.Failure;
                        result.StatusMessage = msg;
                        Log.Warn(FormatLogMsg(msg));
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
                    string epNumber = request.EndingEpisodeNumber > 0 ? string.Concat(request.EpisodeNumber, '-', request.EndingEpisodeNumber) : $"{request.EpisodeNumber}";
                    var msg = $"No provider metadata found for {series.Name} Season {request.SeasonNumber} Episode {epNumber}.<br/>You have either exceeded provider limits or the provider does not have episode information.<br/>Please try again later and/or manually check your providers have the episode available.<br/>(REF:2)";
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = msg;
                    Log.Warn(FormatLogMsg(msg));
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }
                
                var path = Path.Combine(request.TargetFolder, Path.Combine(seriesFolderName, seasonFolderName, episodeFileName));

                
                result.TargetPath = path;
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                Log.Info($"{result.Type} -  {result.ExtractedName} Target path: {result.TargetPath}");
                
                
                if (request.RememberCorrection)
                {
                    if (series != null)
                    {
                        Log.Info($"Adding {result.ExtractedName} to Smart List");
                        SaveSmartMatchString(LibraryManager.ParseName(FileSystem.GetFileNameWithoutExtension(result.OriginalPath).AsSpan()).Name, series.Name, cancellationToken);
                    }
                    else
                    {
                        Log.Info($"Can not add {result.ExtractedName} to Smart List. Try again later.");
                    
                    }
                }

                PerformFileSorting(options, result, cancellationToken);

                return;
            }
            

            try
            {
                Series series = null;
                //User choose an existing series in the UI.
                if (!string.IsNullOrEmpty(request.SeriesId))
                {
                    series = (Series)LibraryManager.GetItemById(request.SeriesId);
                }

                if (series is null) //Try to match the series
                {
                    
                    try
                    {
                        series = GetMatchingSeries(request.Name, request.Year, result, cancellationToken);
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
                        Log.Warn(FormatLogMsg(msg));
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }

                    series = CreateNewSeries(request, targetFolder, null, options);
                }
                
               

                //This will always be true because the user has chosen the proper provider data from the fileorganizer. We'll force the sort.
                //const bool overwriteFile = true; 

                OrganizeEpisode(true, series, request.SeasonNumber, request.EpisodeNumber, request.EndingEpisodeNumber, null, options, request.RememberCorrection, result, cancellationToken);

                OrganizationService.SaveResult(result, CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (result.Status == FileSortingStatus.UserInputRequired)
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
                    episode = await GetEpisodeRemoteProviderData(series, seasonNumber, episodeNumber, endingEpisodeNumber, premiereDate, options, cancellationToken);
                }
                catch(Exception)
                {
                    Log.Warn("Exceeded Provider limits. Try again later...");
                }
            }else
            {
                Log.Debug("Episode matched from internal metadata");
            }

            if (episode is null)
            {
                string epNumber = endingEpisodeNumber > 0 ? string.Concat(episodeNumber, '-', endingEpisodeNumber) : $"{episodeNumber}";
                var msg = $"No provider metadata found for {series.Name} Season {seasonNumber} Episode {epNumber}.<br/>You have either exceeded provider limits or the provider does not have episode information.<br/>Please try again later and/or manually check your providers have the episode available.<br/>(REF:3)";
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = msg;
                Log.Warn(FormatLogMsg(msg));
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
                Log.Warn(FormatLogMsg(msg));
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
                var existingEpisodeFilesButWithDifferentPath = GetExistingEpisodeFilesButWithDifferentPath(result.TargetPath, series, episode);
                result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;

                var fileExists = IsSameEpisode(sourcePath, result.TargetPath); //check if exact file sorted
                var episodeExists = fileExists || result.DuplicatePaths.Count > 0; //check for other copies (duplicates)

                //The source path might be in use. The file could still be copying from it's origin location into watched folder. Status maybe "InUse"
                if(IsCopying(sourcePath, FileSystem) && !result.IsInProgress && result.Status != FileSortingStatus.Processing)
                {
                    var msg = $"File '{sourcePath}' is currently in use, stopping organization";
                    Log.Info(FormatLogMsg(msg));
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = msg;
                    OrganizationService.SaveResult(result, cancellationToken);
                    //OrganizationService.RemoveFromInProgressList(result);
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
                        SaveSmartMatchString(LibraryManager.ParseName(FileSystem.GetFileNameWithoutExtension(result.OriginalPath).AsSpan()).Name, series.Name, cancellationToken);
                    }
                    return;
                }


                //Six phases:
                //1. Copy Mode Only - Destination file is the same file as the one we are trying to sort and there are no duplicates - stop organisation and do nothing.
                //2. Overwrite existing files option is unchecked - The key words input is empty - the file already exists in the library - no files will be overwritten - mark as existing - stop organization.
                //3. Overwrite existing files option is checked - Doesn't matter about key words - the file already exists in the library - any file will overwrite the library item.
                //4. Overwrite existing files option is unchecked - Key words inputs have values - the file already exists in the library - only items with key words in the file name will overwrite the library item.
                //5. a)  The file doesn't exist in the library - is a new episode for an existing series - auto sorting is turned on - Sort episodes for existing series only is turned on - sort it!
                //   b)  The file doesn't exist in the library - is a new episode for a new series - auto sorting is turned on - Sort episodes for existing series only is turned off - Mark the file as NewMedia
                //6. The file doesn't exist in the library - is new - auto sorting is turned off - Mark the file as NewMedia

                //1.
                if (options.CopyOriginalFile && fileExists && existingEpisodeFilesButWithDifferentPath.Count == 0)
                {
                    var msg = $"Exact Match.<br/>File '{sourcePath}' is the same as '{result.TargetPath}'.";
                    Log.Info(FormatLogMsg(msg + " Stopping organization"));
                    result.Status = FileSortingStatus.Success;
                    result.StatusMessage = msg;
                    result.ExistingInternalId = LibraryManager.FindIdByPath(result.TargetPath, false);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }

                //2.
                if (!options.OverwriteExistingEpisodeFiles && !options.OverwriteExistingEpisodeFilesKeyWords.Any() && episodeExists)
                {
                    var msg = $"Existing Episode & No Overwrite.<br/>File '{sourcePath}' already exists {(existingEpisodeFilesButWithDifferentPath.Count > 1 ? "as these" : "at")}:<br/>'{string.Join("'<br/>'", existingEpisodeFilesButWithDifferentPath)}'.<br/><br/>Please refer to the actions panel in the Auto Organize log.";
                    Log.Info(FormatLogMsg(msg));
                    result.Status = FileSortingStatus.SkippedExisting;
                    result.StatusMessage = msg;
                    result.ExistingInternalId = LibraryManager.FindIdByPath(result.TargetPath, false);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }
                
                //3.
                if (options.OverwriteExistingEpisodeFiles && episodeExists && options.AutoDetectSeries)
                {
                    RemoveExistingLibraryFiles(existingEpisodeFilesButWithDifferentPath, result);

                    PerformFileSorting(options, result, cancellationToken);

                    if (rememberCorrection)
                    {
                        SaveSmartMatchString(LibraryManager.ParseName(FileSystem.GetFileNameWithoutExtension(result.OriginalPath).AsSpan()).Name, series.Name, cancellationToken);
                    }
                    return;
                }

                //4.
                if (!options.OverwriteExistingEpisodeFiles && options.OverwriteExistingEpisodeFilesKeyWords.Any() && episodeExists && options.AutoDetectSeries)
                {
                    if (options.OverwriteExistingEpisodeFilesKeyWords.Any(word => result.OriginalFileName.ContainsIgnoreCase(word)))
                    {
                        RemoveExistingLibraryFiles(existingEpisodeFilesButWithDifferentPath, result);

                        PerformFileSorting(options, result, cancellationToken);

                        if (rememberCorrection)
                        {
                            SaveSmartMatchString(LibraryManager.ParseName(FileSystem.GetFileNameWithoutExtension(result.OriginalPath).AsSpan()).Name, series.Name, cancellationToken);
                        }
                        return;
                    }

                    var msg = $"Overwrite restricted to key words.<br/>File '{sourcePath}' already exists {(existingEpisodeFilesButWithDifferentPath.Count > 1 ? "as these" : "at")}:<br/>'{string.Join("'<br/>'", existingEpisodeFilesButWithDifferentPath)}'.<br/><br/>Please refer to the actions panel in the Auto Organize log.";
                    Log.Info(FormatLogMsg(msg));
                    result.Status = FileSortingStatus.SkippedExisting;
                    result.StatusMessage = msg;
                    result.ExistingInternalId = LibraryManager.FindIdByPath(result.TargetPath, false);
                    result.DuplicatePaths = existingEpisodeFilesButWithDifferentPath;
                    OrganizationService.SaveResult(result, cancellationToken);

                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }

                //5.
                if (!episodeExists && options.AutoDetectSeries)
                {
                    if (IsNewSeries(series) && options.SortExistingSeriesOnly)
                    {//b
                        var msg = $"Enable new series creation is disabled.<br/>File '{sourcePath}' will require manual sorting.<br/><br/>Please refer to the actions panel in the Auto Organize log.";
                        Log.Info(FormatLogMsg(FormatLogMsg(msg)));
                        result.Status = FileSortingStatus.NewMedia;
                        result.StatusMessage = msg;
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }
                     
                    PerformFileSorting(options, result, cancellationToken);

                    if (rememberCorrection)
                    {
                        SaveSmartMatchString(LibraryManager.ParseName(FileSystem.GetFileNameWithoutExtension(result.OriginalPath).AsSpan()).Name, series.Name, cancellationToken);
                    }

                    return;
                }

                //6.
                if (!options.AutoDetectSeries && !episodeExists)
                {
                    var msg = $"Smart Series Auto detect disabled.<br/>File '{sourcePath}' will require manual sorting.<br/><br/>Please refer to the actions panel in the Auto Organize log.";
                    Log.Info(FormatLogMsg(msg));
                    result.Status = FileSortingStatus.NewMedia;
                    result.StatusMessage = msg;
                    OrganizationService.SaveResult(result, cancellationToken);
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
                OrganizationService.RemoveFromInProgressList(result);
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

        private void SaveSmartMatchString(string matchString, string seriesName, CancellationToken cancellationToken)
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

            //The LibraryManager.ParseName() method could parse season and episode data with the Name
            //Remove Season and Episode info from matchString. 
            matchString = Regex.Replace(matchString,
                @"(?:([Ss](\d{1,2})[Ee](\d{1,2})))|(?:(\d{1,2}x\d{1,2}))|(?:[Ss](\d{1,2}x[Ee]\d{1,2}))|(?:([Ss](\d{1,2})))", string.Empty);

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
            if (!episode.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue)
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
                        i.ParentIndexNumber.HasValue && i.ParentIndexNumber.Value == episode.ParentIndexNumber.Value &&
                        i.IndexNumber.HasValue && i.IndexNumber.Value == episode.IndexNumber.Value)
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
            result.StatusMessage = "Sorting is in progress";
            result.FileSize = FileSystem.GetFileInfo(result.OriginalPath).Length; //Update the file size so it will show the actual size of the file here. It may have been copying before.
            Log.Info($"Auto organize adding {result.TargetPath} to in progress list");
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
                OrganizationService.RemoveFromInProgressList(result);
               
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
                                OrganizationService.RemoveFromInProgressList(result);
                                
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
                        OrganizationService.RemoveFromInProgressList(result);
                        
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
                       OrganizationService.RemoveFromInProgressList(result);
                       
                       //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                       LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
                       return;
                    } 
                   
                }

                result.Status = FileSortingStatus.Success;
                result.StatusMessage = $"{result.OriginalPath} has successfully been placed in the target destination: {result.TargetPath}";
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.RemoveFromInProgressList(result);
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
                    OrganizationService.RemoveFromInProgressList(result);
                   
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
                OrganizationService.RemoveFromInProgressList(result);
               
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
            if(endingEpisodeNumber > 0)
            {   
                //always do a provider check for dual episodes - we need individual names and not concatented for us
                return episode;
            }
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
                IsVirtualItem = false,
                Years = seriesYear.HasValue ? new[] { seriesYear.Value } : Array.Empty<int>()
            });

            if (series.Any())
            {
                resultSeries = series.Where(s => string.Equals(RegexExtensions.NormalizeString(s.Name), RegexExtensions.NormalizeString(seriesName), StringComparison.InvariantCultureIgnoreCase)).ToList();
                
                //if (!resultSeries.Any())
                //{
                //    resultSeries = series.Where(s => RegexExtensions.NormalizeString(s.Name).ContainsIgnoreCase(RegexExtensions.NormalizeString(seriesName))).ToList();
                //}
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

                
                
                var info = smartMatch.Items.FirstOrDefault(smartMatchInfo => smartMatchInfo.MatchStrings.Any(match => LibraryManager.ParseName(match.AsSpan()).Name.ContainsIgnoreCase(seriesName)));

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
                    IsVirtualItem = false,
                    DtoOptions = new DtoOptions(true)
                });

                if (series.Any())
                {
                    resultSeries = series.ToList();
                }
            }

            var distinctPaths = resultSeries.Select(s => new { s.Path }).Distinct().ToList(); //Apparently we could have a result twice with same paths... (virtual item?? - or maybe corrupted DB??)

            if (distinctPaths.Count == 1)
            {
                result.ExtractedName = resultSeries[0].Name;
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return resultSeries.Cast<Series>().FirstOrDefault();
            }

            if (distinctPaths.Count > 1)
            {

                //Mark the result as having more then one location in the library. Needs user input, set it to "Waiting"
                result.Status = FileSortingStatus.UserInputRequired;
                var msg = "Item has more then one possible destination folders. Waiting to sort file...\n";
                foreach (var entry in resultSeries)
                {
                    msg += $"Location: {entry.Path}\n";
                }
                result.StatusMessage = msg;
                Log.Warn(FormatLogMsg(msg));
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return resultSeries.Cast<Series>().FirstOrDefault();;
            }

            Log.Info("New Series Detected...");

            return null;
        }
        

        private async Task<Episode> GetEpisodeRemoteProviderData(Series series, int? seasonNumber, int? episodeNumber, int? endingEpisodeNumber, DateTime? premiereDate, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            Log.Info("Searching providers for matching episode data...");
            var libraryOptions = LibraryManager.GetLibraryOptions(series);
            var episodeInfo = new EpisodeInfo
            {
                IndexNumber = episodeNumber,
                IndexNumberEnd = endingEpisodeNumber,
                MetadataCountryCode = libraryOptions.MetadataCountryCode,
                MetadataLanguage = libraryOptions.PreferredMetadataLanguage,
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

            if (endingEpisodeNumber > 0)
            {
                IEnumerable<RemoteSearchResult> subSearchResults;
                for (int i = (int)(episodeNumber + 1); i <= endingEpisodeNumber; i++)
                {
                    var subEpisodeInfo = episodeInfo;
                    subEpisodeInfo.IndexNumber = i;
                    try
                    {
                        subSearchResults = await ProviderManager.GetRemoteSearchResults<Episode, EpisodeInfo>(new RemoteSearchQuery<EpisodeInfo>
                        {
                            SearchInfo = subEpisodeInfo,
                            IncludeDisabledProviders = true

                        }, series, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"Internal Error subEpisode: {e}");
                        throw new Exception(); //We'll catch this later
                    }

                    if (subSearchResults != null)
                    {
                        RemoteSearchResult subEpisodeResults = subSearchResults.FirstOrDefault();
                        episodeResults.Name = string.Concat(episodeResults.Name, options.MultiEpisodeNameDeliminator, subEpisodeResults.Name);
                    }
                }
            }
            if (episodeResults == null)
            {
                var msg = $"No provider metadata found for {series.Name} season {seasonNumber} episode {episodeNumber}";
                Log.Warn(FormatLogMsg(msg));
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
                Log.Warn("Auto Organize settings: default library not set for TV Shows.");
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
                MetadataLanguage = metadataLanguage ?? "",
                
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
                var remoteSearchResults = searchResults.ToList();
                Log.Info($"Provider results for {seriesName} has {remoteSearchResults.Count()} results");
                finalResult =
                    remoteSearchResults
                        .FirstOrDefault(); //.FirstOrDefault(result => RegexExtensions.NormalizeString(result.Name).ContainsIgnoreCase(RegexExtensions.NormalizeString(seriesName)));
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
                Log.Error("GetEpisodeFileName: Mandatory param as missing!");
                return null;
            }

            var endingEpisodeNumber = episodeIndexNumberEnd;
            var episodeNumber = episodeIndexNumber.Value;
            var seasonNumber = seasonIndexNumber.Value;

            var sourceExtension = (Path.GetExtension(result.OriginalPath) ?? string.Empty).TrimStart('.');

            var pattern = endingEpisodeNumber.HasValue ? options.MultiEpisodeNamePattern : options.EpisodeNamePattern;
            
            if (string.IsNullOrWhiteSpace(pattern))
            {
                Log.Warn("Configured episode name pattern is empty!");
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

        
        
        //private string NormalizeString(string value)
        //{
        //    return Regex.Replace(value, @"(\s+|@|&|'|:|\(|\)|<|>|#|-|\.|\band\b|,)", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
        //}


    }
}