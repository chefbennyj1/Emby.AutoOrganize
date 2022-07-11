using Emby.AutoOrganize.Model;
using MediaBrowser.Common.Events;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.FileMetadata;
using Emby.AutoOrganize.Model.Organization;
using Emby.AutoOrganize.Naming;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Providers;

namespace Emby.AutoOrganize.Core.FileOrganization
{
    public class MovieOrganizer : BaseFileOrganizer<MovieFileOrganizationRequest>
    {
        private ILibraryMonitor LibraryMonitor               { get; }
        private ILibraryManager LibraryManager               { get; }
        private ILogger Log                                  { get; }
        private IFileSystem FileSystem                       { get; }
        private IFileOrganizationService OrganizationService { get; }
        private IProviderManager ProviderManager             { get; }

        //private readonly CultureInfo _usCulture = new CultureInfo("en-US");
        public static event EventHandler<GenericEventArgs<FileOrganizationResult>> ItemUpdated;

        //public static MovieOrganizer Instance { get; private set; }
        public MovieOrganizer(IFileOrganizationService organizationService, IFileSystem fileSystem, ILogger log, ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, IProviderManager providerManager) 
            : base(organizationService, fileSystem, log, libraryManager, libraryMonitor, providerManager)
        {
            OrganizationService = organizationService;
            FileSystem          = fileSystem;
            Log                 = log;
            LibraryManager      = libraryManager;
            LibraryMonitor      = libraryMonitor;
            ProviderManager     = providerManager;
            
            //Instance            = this;
        }

       

        public async Task<FileOrganizationResult> OrganizeFile(bool requestToMoveFile, string path, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {                
            Log.Info("Sorting file {0}", path);

            FileOrganizationResult result = null;

            try
            {
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
                result = new FileOrganizationResult()//Create the result
                {
                    Date = DateTime.UtcNow,
                    OriginalPath = path,
                    OriginalFileName = Path.GetFileName(path),
                    ExtractedEdition = RegexExtensions.GetReleaseEditionFromFileName(Path.GetFileName(path)),
                    Type = FileOrganizerType.Movie,
                    FileSize = FileSystem.GetFileInfo(path).Length,
                    SourceQuality = RegexExtensions.GetSourceQuality(Path.GetFileName(path)),
                    ExtractedResolution = new Resolution()
                };

                Log.Info("Auto organize checking for movie: " + result.TargetPath);
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
                Log.Info("Auto-organize Path is locked by other processes. Please try again later.");
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
                //Log.Warn(result.ExtractedResolution);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                
            }

            if (LibraryMonitor.IsPathLocked(path.AsSpan()) && result.Status == FileSortingStatus.Processing)
            {
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                return result;
            }

            //If we have the proper data to sort the file, and the user has requested it. Sort it!
            if (!string.IsNullOrEmpty(result.TargetPath) && requestToMoveFile)
            {
                PerformFileSorting(options, result, cancellationToken);
                return result;
            }


            try
            {
                
                var movieName = string.Empty;
                int? movieYear = null;

                //Parse the file name for naming info
                var movieInfoFromFile = LibraryManager.ParseName(FileSystem.GetFileNameWithoutExtension(path).AsSpan());
               
                movieName = movieInfoFromFile.Name;
                movieYear = movieInfoFromFile.Year;
                
                //Movie name could be null or empty
                //Or it could contain a dash. ex: "exq-MovieName.2022.1080p.etc.ext"
                //If the movie name contains a dash (this is a typical naming convention from online resources), it may not be possible to parse an actual name for the file.
                //Try the parent folder for proper naming so emby will understand.
                if (movieName.Substring(0, movieName.Length / 2).Contains("-") || string.IsNullOrEmpty(movieName))
                {
                    Log.Info("Checking parent folder for movie naming...");

                    //Split the file path by the Separator
                    var paths = path.Split(FileSystem.DirectorySeparatorChar);

                    //our parent folder
                    var parentFolderName = paths[paths.Count() - 2];

                    //Parse the Parent folder for some kind of proper naming
                    var movieInfoFromParentFolder = LibraryManager.ParseName(parentFolderName.AsSpan());


                    //Check those values...
                    //Both attempts to read a movie name from the file and parent folder has no results
                    //User will have to sort with corrections.
                    if (string.IsNullOrEmpty(movieInfoFromFile.Name) &&
                        string.IsNullOrEmpty(movieInfoFromParentFolder.Name))
                    {
                        var msg = $"Unable to determine movie name from {path}";
                        result.Status = FileSortingStatus.Failure;
                        result.StatusMessage = msg;
                        Log.Warn(msg);
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return result;
                    }

                    //We found everything we need.
                    if (!string.IsNullOrEmpty(movieInfoFromParentFolder.Name) && movieInfoFromParentFolder.Year.HasValue)
                    {
                        Log.Info("Parsed movie name from parent folder successful...");
                        movieName = movieInfoFromParentFolder.Name;
                        movieYear = movieInfoFromParentFolder.Year;

                        //We'll update the Edition Information here again, because the parent folder most likely contains the proper data we need to identify the movie.
                        result.ExtractedEdition = RegexExtensions.GetReleaseEditionFromFileName(parentFolderName);
                        result.SourceQuality = RegexExtensions.GetSourceQuality(parentFolderName);

                    }
                    //We'll reset, and try again with the file name
                    else
                    {
                        movieName = string.Empty;
                    }

                }

                //We tried the parent folder for naming, but got nothing...
                //Use the file name which may contains a dash. ex: exq-Movie.Name.2022.1080p.etc.ext
                //Use Regex to select everything after the first dash.
                //This isn't great, but we'll also keep an eye out for movies that have strings which contain "-Man" or "-man".
                //It may seem specific, however modern/contemporary movies (Hero movies) may contain a dash, and need to be excluded from this check. 
                //So, we're taking that into consideration. 
                if (string.IsNullOrEmpty(movieName))
                {
                    var regexName = new Regex(@"(?<=[a-zA-Z0-9{0,5}]-(\b(?![Mm]an)\b))(?:.*)");
                    var namingMatch = regexName.Match(FileSystem.GetFileNameWithoutExtension(path));

                    if (namingMatch.Success)
                    {
                        movieName = namingMatch.Value;
                    }
                }

                if (!movieYear.HasValue)
                {
                    var regexDate = new Regex(@"(19|20|21)\d{2}");
                    var yearMatch = regexDate.Match(FileSystem.GetFileNameWithoutExtension(path));

                    if (int.TryParse(yearMatch.Value, out var year))
                    {
                        movieYear = year;
                    }
                }



                //Some movie naming places the Edition information before the year. ex: "MovieName.UNCUT.2022.1080p.etc.ext"
                //Emby must look at the Year to decide the name, when using LibraryManager.ParseName() method.
                //This results in the edition information gets parsed with the name.
                //
                //result => "MovieName UNCUT"
                //
                //We'll strip it here,  We only want the "MovieName".
                movieName = movieName.Replace(result.ExtractedEdition, string.Empty).Trim();
                

                //...And sometimes Emby LibraryManager.ParseName() method will Parse the Year in the name as well.
                //
                //result =>  "MovieName (2022)"
                //
                //We'll have to strip the year next. We only want the "MovieName".
                if (movieYear.HasValue)
                {
                    movieName = movieName.Replace($"({movieYear.Value})", string.Empty).Trim();
                }

                //Clean up the movie name that the Library Manager parsed, it will still contain unwanted character, and it may still contain the Resolution.
                movieName = Regex.Replace(movieName, @"[^A-Za-z0-9\s+]|[0-9]{3,4}[Pp]", " ", RegexOptions.IgnoreCase).Replace("  ", " ").Trim();
                movieName = new CultureInfo("en-US", false).TextInfo.ToTitleCase(movieName.Trim());


                Log.Info($"Extracted information from {path}. Movie {movieName}, Year {(movieYear.HasValue ? movieYear.Value.ToString() : " Can not parse year")}");

                result.ExtractedName = movieName;
                result.ExtractedYear = movieYear;
               

                OrganizationService.SaveResult(result, cancellationToken);


                if (requestToMoveFile)
                {
                    Log.Info($"User Requests to sort {result.OriginalFileName}.");
                }
               
                
                //If we have both a year and a name, that is all we really need to name the file for the user
                if (!string.IsNullOrEmpty(result.ExtractedName) && result.ExtractedYear.HasValue)
                {
                    var targetFolder = "";
                    //if (!string.IsNullOrEmpty(options.DefaultMovieLibraryPath))
                    //{
                    //    targetFolder = options.DefaultMovieLibraryPath;
                    //} 
                    //else
                    //{
                    //    //The user didn't filling the settings - warn the log, return failure - that is all
                    //    var msg = $"Auto sorting for {movieName} is not possible. Please choose a default library path in settings";
                    //    result.Status = FileSortingStatus.Failure;
                    //    result.StatusMessage = msg;
                    //    Log.Warn(msg);
                    //    OrganizationService.SaveResult(result, cancellationToken);
                    //    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    //    return result;
                    //}

                    try
                    {
                        targetFolder = ResolveTargetFolder(result, options, cancellationToken);
                    }
                    catch (InvalidTargetFolderException ex)
                    {
                        //The user didn't filling the settings - warn the log, return failure - that is all
                        result.Status = FileSortingStatus.Failure;
                        result.StatusMessage = ex.Message;
                        Log.Warn(ex.Message);
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return result;
                    }

                    //Name the file path with the options the user filled out, and the meta from the source file name.
                    var movieFolderName = GetMovieFolderName(result, options);
                    var movieFileName   = GetMovieFileName(result.OriginalPath, result, options);
                    result.TargetPath   = Path.Combine(targetFolder, movieFolderName, movieFileName);

                    Log.Info($"Movie: {result.ExtractedName} - Target path has been calculated as: {result.TargetPath}");
                    //organize the the file
                    OrganizeMovie(requestToMoveFile, path, options, result, cancellationToken);

                    return result;

                }

                    
                //We don't have a name or a year... most likely a year is missing. We'll need that to sort the file - it's an option the user has for naming.
                
                //Maybe the movie already part of the library... ...
                var movie = GetMatchingMovie(result.ExtractedName, movieYear, result);
            
                    
                if (movie == null)
                {
                    //Not part of the library, but we really need that year... 
                    //Ask the metadata providers for it...
                    try
                    {
                        movie = await GetMovieRemoteProviderData(result, options, cancellationToken);//.ConfigureAwait(false);
                    }
                    catch(Exception)
                    {

                    }
                }

                //Did any of that work... ?

                if (movie is null)
                {
                    //Nope none of it did. Fail the movie sorting. The user will have to sort with corrections.
                    var msg = $"Unable to determine movie name from {path}";
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = msg;
                    Log.Warn(msg);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return result;
                }
                    
                //We have a movie either from the library or the providers data. 
                //At this point one of the methods gave us a path for the movie.
                //TODO: I don't like that one of those methods handed us a path... It would be best to build the path here using the data returned from those methods. Fix that!

                result.TargetPath = movie.Path;
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                 
                
                //Guess the naming is good to go! Organize it.
                OrganizeMovie(requestToMoveFile, path, options, result, cancellationToken);
            
                

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
            catch (IOException ex)
            {
                if (ex.Message.Contains("being used by another process"))
                {
                    var errorMsg = $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = errorMsg;
                    Log.ErrorException(errorMsg, ex);
                    
                }
                else
                {
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = ex.Message;
                    Log.ErrorException("Error organizing file", ex);
                }
            }
            catch (OrganizationException)
            {
                
            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                Log.ErrorException("Error organizing file", ex);
            }

            OrganizationService.SaveResult(result, cancellationToken);
            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI

            return result;
        }
        

        private Movie CreateNewMovie(MovieFileOrganizationRequest request, string targetRootFolder, FileOrganizationResult result, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            // To avoid Movie duplicate by mistake (Missing SmartMatch and wrong selection in UI)
            var movie = GetMatchingMovie(request.Name, request.Year, result);

            if (movie == null)
            {
                movie = new Movie
                {
                    Name = request.Name,
                    ProductionYear = request.Year,
                    IsInMixedFolder = !options.CreateMovieInFolder,
                    ProviderIds = request.ProviderIds ?? new ProviderIdDictionary(),
                };

                var movieFolderPath = "";

                if (options.CreateMovieInFolder)
                {
                    movieFolderPath = Path.Combine(movieFolderPath, GetMovieFolderName(result, options));
                }

                movieFolderPath = Path.Combine(movieFolderPath, GetMovieFileName(result.OriginalPath, result, options));
                
                if (string.IsNullOrEmpty(movieFolderPath))
                {
                    var msg = $"Unable to sort {result.OriginalPath} because target path could not be determined.";
                    Log.Warn(msg);
                    return null;
                }

                movie.Path = Path.Combine(request.TargetFolder ?? targetRootFolder, movieFolderPath);

            }

            return movie;
        }

        public void OrganizeWithCorrection(MovieFileOrganizationRequest request, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            
            var result = OrganizationService.GetResult(request.ResultId);
            var overwriteFile = request.RequestToMoveFile ?? false;

            try
            {
                Movie movie = null;

                
                string targetRootFolder = null;

                if (!string.IsNullOrEmpty(request.TargetFolder))
                {
                    //User wants the file to go in this root folder.
                    targetRootFolder = request.TargetFolder;
                }
                else
                {
                    //User didn't specify a root folder, so we'll use default 
                    if (!string.IsNullOrEmpty(options.DefaultMovieLibraryPath))
                    {
                        targetRootFolder = options.DefaultMovieLibraryPath;
                    }
                    else
                    {
                        //User didn't fill out the settings - warn the log, and fail organization.
                        result.Status = FileSortingStatus.Failure;
                        result.StatusMessage = "Auto Organize settings: default library not set for Movies. Stopping Organization";
                        Log.Warn(result.StatusMessage);
                        //OrganizationService.RemoveFromInProgressList(result);
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        //return result;

                    }
                }

                if (!string.IsNullOrEmpty(request.Name) && request.Year.HasValue)
                {
                    result.ExtractedName = request.Name;
                    result.ExtractedYear = request.Year;

                    OrganizationService.SaveResult(result, cancellationToken);

                    //Name the file path with the options the user filled out, and the request.
                    var movieFolderName = GetMovieFolderName(result, options);
                    var movieFileName   = GetMovieFileName(result.OriginalPath, result, options);
                    result.TargetPath   = Path.Combine(targetRootFolder, movieFolderName, movieFileName);

                    
                    //organize the the file
                    OrganizeMovie(overwriteFile, result.OriginalPath, options, result, cancellationToken);

                    //We won't use this old version of the result in the UI. It will have been updated
                    //return result;
                    return;
                }

                // To avoid movie duplicate by mistake (Missing SmartMatch and wrong selection in UI)
                movie = CreateNewMovie(request, targetRootFolder, result, options, cancellationToken);
                

                if (movie is null)
                {
                    var msg = "Error organizing file with corrections";
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = msg;
                    Log.Warn(msg);
                    //OrganizationService.RemoveFromInProgressList(result);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }

                result.TargetPath = movie.Path;
                 
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);

                Log.Info("Organize with corrections: " + movie.Path);
                
                OrganizeMovie(overwriteFile, result.OriginalPath, options, result, cancellationToken);

               

            }
            catch (IOException ex)
            {
                if (ex.Message.Contains("being used by another process"))
                {
                    var errorMsg = $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = errorMsg;
                    Log.Warn(errorMsg, ex);
                    
                }
                else
                {
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = ex.Message;
                    Log.Warn("Error organizing file", ex);
                }
            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
            }

            OrganizationService.SaveResult(result, CancellationToken.None);
            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
            //return result;
        }
     

        private void OrganizeMovie(bool overwriteFile, string sourcePath, AutoOrganizeOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            

            bool isNew = string.IsNullOrWhiteSpace(result.Id);

            if (isNew)
            {               
                OrganizationService.SaveResult(result, cancellationToken);
            }

            //if (!OrganizationService.AddToInProgressList(result, isNew))
            //{
            //    Log.Info("File is currently processed otherwise. Please try again later.");
            //    return;
            //}
            

            try
            {
                // Proceed to sort the file

                //The actual file, or the movie folder it lives in.
                var fileExists = FileSystem.FileExists(result.TargetPath) || FileSystem.DirectoryExists(FileSystem.GetDirectoryName(result.TargetPath));
                
                //The source path might be in use. The file could still be copying from it's origin location into watched folder. Status maybe "InUse"
                if (IsCopying(sourcePath, FileSystem) && !result.IsInProgress && result.Status != FileSortingStatus.Processing)
                {
                    var msg = $"File '{sourcePath}' is currently in use, stopping organization";
                    Log.Info(msg);
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = msg;
                    //OrganizationService.RemoveFromInProgressList(result);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }

                //User is forcing sorting from the UI - Sort it!
                if (overwriteFile)
                {
                    Log.Info("Sorting file {0} into movie {1}", sourcePath, result.TargetPath);
                    //The request came in from the client. The file needs to be moved into the target folder despite the status.
                    PerformFileSorting(options, result, cancellationToken);
                    return;
                }

                //Five phases:
                //1. Check for new resolution when no auto sorting is enabled
                //1. Overwrite existing files option is unchecked - The key words input is empty - no files will be overwritten.
                //2. Overwrite existing files option is checked - Doesn't matter about key words - any file will overwrite the library item.
                //3. Overwrite existing files option is unchecked - Key words inputs have values - only items with key words in the file name will overwrite the library item.
                //4. If the file doesn't exist and is new sort it!
                //5. The file is new (doesn't exist in the library), but auto sorting is turned off - Mark the file as NewMedia


                //1.
                if (fileExists && !options.AutoDetectMovie)
                {
                    var msg = string.Empty;
                    Log.Info($"Checking Existing Resolution: {result.ExtractedName}");
                    var moviesResult = LibraryManager.GetItemsResult(new InternalItemsQuery()
                    {
                        IncludeItemTypes = new[] {nameof(Movie)},
                        Recursive = true,
                        DtoOptions = new DtoOptions(true),
                    });

                    var movies = moviesResult.Items.Where(m => RegexExtensions.NormalizeString(m.Name) == RegexExtensions.NormalizeString(result.ExtractedName)).ToList();

                    if (!movies.Any()) //hail mary comparison for movie name
                    {
                        movies = moviesResult.Items.Where(m => RegexExtensions.NormalizeString(m.Name).ContainsIgnoreCase(RegexExtensions.NormalizeString(result.ExtractedName))).ToList();
                    }

                    if (movies.Any()) //<-- Found the movie, and possibly several versions/resolutions of it.
                    {
                        var message = $"Possible existing movies found for {result.ExtractedName} but with different resolutions:";
                        foreach (var movie in movies)
                        {
                            message += $"\n {movie.Name}";
                            
                            movie.GetMediaStreams().ForEach(s => message += " " + s.DisplayTitle);
                        }

                        Log.Info(message);

                        if(movies.All(m => !ResolutionExists(m, result.ExtractedResolution))) 
                        {
                            msg = $"File {sourcePath} is a new resolution, stopping organization";
                            Log.Info(msg);
                            result.Status = FileSortingStatus.NewResolution;
                            result.StatusMessage = msg;
                            result.ExistingInternalId = movies[0].InternalId;
                            OrganizationService.SaveResult(result, cancellationToken);
                            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                            return;
                        }

                        Log.Info($"Checking Existing Edition/Release: {result.ExtractedName}");
                        var fileSpecific = movies.Any(movie => movie.Path == result.TargetPath);
                        if (fileSpecific)
                        {
                            msg = $"File '{sourcePath}' already exists: '{result.TargetPath}', stopping organization";
                            Log.Info(msg);
                            result.Status = FileSortingStatus.SkippedExisting;
                            result.ExistingInternalId = LibraryManager.GetItemsResult(new InternalItemsQuery() { Path = result.TargetPath }).Items[0].InternalId;
                            result.StatusMessage = msg;
                            OrganizationService.SaveResult(result, cancellationToken);
                            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                            return;
                        }

                        msg = $"Movie Auto detect disabled. File '{sourcePath}' is a new edition, will wait for user interaction. Stopping organization";
                        Log.Info(msg);
                        result.Status = FileSortingStatus.NewEdition;
                        result.StatusMessage = msg;
                        result.ExistingInternalId = movies[0].InternalId;
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;

                    }


                }

                //2.
                if (!options.OverwriteExistingMovieFiles && !options.OverwriteExistingMovieFilesKeyWords.Any() && fileExists)
                {
                    if (FileSystem.FileExists(result.TargetPath)) //The actual file with the same name, not just the movie library folder
                    {
                        var msg = $"File '{sourcePath}' already exists: '{result.TargetPath}', stopping organization";
                        Log.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        result.ExistingInternalId = LibraryManager.GetItemsResult(new InternalItemsQuery() { Path = result.TargetPath }).Items[0].InternalId;
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }

                    if (!options.AutoDetectMovie)
                    {
                        var msg = $"Movie Auto detect disabled. File '{sourcePath}' is a new edition, will wait for user interaction. Stopping organization";
                        Log.Info(msg);
                        result.Status = FileSortingStatus.NewEdition;
                        result.StatusMessage = msg;
                        result.ExistingInternalId = LibraryManager.GetItemsResult(new InternalItemsQuery() { Path = result.TargetPath }).Items[0].InternalId;
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                        return;
                    }

                    //Just sort the damn thing!
                    PerformFileSorting(options, result, cancellationToken);
                    return;


                }

                //3.
                if (options.OverwriteExistingMovieFiles && fileExists && options.AutoDetectMovie)
                {
                    PerformFileSorting(options, result, cancellationToken);
                    return;
                }

                //4.
                if (!options.OverwriteExistingMovieFiles && options.OverwriteExistingMovieFilesKeyWords.Any() && fileExists && options.AutoDetectMovie)
                {
                    if (options.OverwriteExistingMovieFilesKeyWords.Any(word => result.OriginalFileName.ContainsIgnoreCase(word)))
                    {
                        PerformFileSorting(options, result, cancellationToken);
                        return;
                    }

                    var msg = $"File '{sourcePath}' already exists at path '{result.TargetPath}', stopping organization";
                    Log.Info(msg);
                    result.Status = FileSortingStatus.SkippedExisting;
                    result.StatusMessage = msg;
                    result.ExistingInternalId = LibraryManager.GetItemsResult(new InternalItemsQuery() { Path = result.TargetPath }).Items[0].InternalId;
                    OrganizationService.RemoveFromInProgressList(result);
                    OrganizationService.SaveResult(result, cancellationToken);
                    //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }

                //5.
                if (!fileExists && options.AutoDetectMovie)
                {
                    PerformFileSorting(options, result, cancellationToken);
                }

                //6.
                if (!options.AutoDetectMovie && !fileExists)
                {
                    var msg = $"Movie Auto detect disabled. File '{sourcePath}' will wait for user interaction. Stopping organization";
                    Log.Info(msg);
                    result.Status = FileSortingStatus.NewMedia;
                    result.StatusMessage = msg;
                    //OrganizationService.RemoveFromInProgressList(result);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }
                
            }
            catch (OrganizationException ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;               
            }
            catch (IOException ex)
            {
                if(ex.Message.Contains("being used by another process") && !result.IsInProgress)
                {                    
                    var errorMsg = $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = errorMsg;
                    Log.Warn(errorMsg, ex);
                    //OrganizationService.RemoveFromInProgressList(result);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                }
            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                Log.Warn(ex.Message);
                //OrganizationService.RemoveFromInProgressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
            }

            
        }

        private bool ResolutionExists(BaseItem movie, Resolution sourceFileResolution)
        {
            var videoStream = movie.GetMediaStreams().FirstOrDefault(s => s.Type == MediaStreamType.Video);
            
            return videoStream?.Width == sourceFileResolution.Width || videoStream.DisplayTitle.ContainsIgnoreCase(sourceFileResolution.Name);
        }

        
        public void PerformFileSorting(AutoOrganizeOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            Log.Info("Processing " + result.TargetPath);
            result.Status = FileSortingStatus.Processing;
            result.StatusMessage = "";
            result.FileSize = FileSystem.GetFileInfo(result.OriginalPath).Length; //Update the file size so it will show the actual size of the file here. It may have been copying before.
            Log.Info($"Auto organize adding {result.TargetPath} to in progress list");
            OrganizationService.SaveResult(result, cancellationToken);
            OrganizationService.AddToInProgressList(result, true);
            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI


            // We should probably handle this earlier so that we never even make it this far
            if (string.Equals(result.OriginalPath, result.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = FileSortingStatus.Failure;
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.RemoveFromInProgressList(result);
                
                //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return;
            }

            LibraryMonitor.ReportFileSystemChangeBeginning(result.TargetPath);
            
            //Check to see if the library already has this entry
            var targetAlreadyExists = FileSystem.FileExists(result.TargetPath) || FileSystem.DirectoryExists(Path.GetDirectoryName(result.TargetPath));


            if (FileSystem.FileExists(result.TargetPath)) //(targetAlreadyExists && options.OverwriteExistingFiles && requestToMoveFile.Value)
            {
                RemoveExistingLibraryItem(result);
            }


            if (!FileSystem.DirectoryExists(FileSystem.GetDirectoryName(result.TargetPath)))
            {
                FileSystem.CreateDirectory(FileSystem.GetDirectoryName(result.TargetPath));
            }

            try
            {
                
                if (options.CopyOriginalFile)
                {
                    try
                    {
                        Log.Info(targetAlreadyExists ? "Overwriting Existing Destination File" : "Copying File");
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
                else
                {
                    Log.Info("Moving File");
                    try 
                    {
                        FileSystem.MoveFile(result.OriginalPath, result.TargetPath);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("disk space"))
                        {
                            Log.Warn(ex.Message);
                            result.Status = FileSortingStatus.NotEnoughDiskSpace;
                            result.StatusMessage = "There is not enough disk space on the drive to move this file";                          
                        } 
                        else if (ex.Message.Contains("used by another process"))
                        {
                            Log.Warn(ex.Message);
                            result.Status = FileSortingStatus.InUse;
                            result.StatusMessage = "The file is being streamed to a emby device. Please try again later.";                           
                        }
                        OrganizationService.SaveResult(result, cancellationToken);
                        OrganizationService.RemoveFromInProgressList(result);
                        
                        //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                        return;
                    }
                }

                result.Status = FileSortingStatus.Success;
                result.StatusMessage = $"{result.OriginalPath} has successfully been placed in the target destination: {result.TargetPath}";
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.RemoveFromInProgressList(result);
               
                //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
            }
            catch (IOException ex)
            {
                if(ex.Message.Contains("being used by another process") && !result.IsInProgress)
                {                    
                    var errorMsg = $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = errorMsg;
                    Log.ErrorException(errorMsg, ex);
                    OrganizationService.SaveResult(result, cancellationToken);
                    OrganizationService.RemoveFromInProgressList(result);
                    
                    //EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
                    return;
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";

                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = errorMsg;
                Log.ErrorException(errorMsg, ex);
                OrganizationService.SaveResult(result, cancellationToken);
                OrganizationService.RemoveFromInProgressList(result);
               
                LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
                return;
            }
            finally
            {
                LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
            }

            if (options.CopyOriginalFile) return;

            try
            {
                FileSystem.DeleteFile(result.OriginalPath);
            }
            catch (Exception ex)
            {
                Log.Warn("Error deleting {0}", ex, result.OriginalPath);
            }



        }

        private async Task<Movie> GetMovieRemoteProviderData(FileOrganizationResult result, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            

            string metadataLanguage = null;
            string metadataCountryCode = null;
            BaseItem targetFolder = null;

            if (!string.IsNullOrEmpty(options.DefaultMovieLibraryPath))
            {
                targetFolder = LibraryManager.FindByPath(options.DefaultMovieLibraryPath, true);
            } 
            
            if (targetFolder != null)
            {
                metadataLanguage = targetFolder.GetPreferredMetadataLanguage();
                metadataCountryCode = targetFolder.GetPreferredMetadataCountryCode();
            }
            else
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = "Auto Organize settings: default library not set for Movies.";
                OrganizationService.RemoveFromInProgressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                return null;
            }

            var movieInfo = new MovieInfo
            {
                Name = result.ExtractedName,
                MetadataCountryCode = metadataCountryCode,
                MetadataLanguage = metadataLanguage
            };

            if (result.ExtractedYear.HasValue)
            {
                movieInfo.Year = result.ExtractedYear.Value;
            }

            IEnumerable<RemoteSearchResult> searchResults = null;
            try
            {
                searchResults = await ProviderManager.GetRemoteSearchResults<Movie, MovieInfo>(
                    new RemoteSearchQuery<MovieInfo>
                    {
                        SearchInfo = movieInfo,
                        IncludeDisabledProviders = true

                    }, targetFolder, cancellationToken);
            }
            catch (Exception)
            {
                Log.Warn("Provider limits reached.");
            }

            RemoteSearchResult finalResult = null;

            if (searchResults != null)
            {
                var remoteSearchResults = searchResults.ToList();
                Log.Info($"Movie Provider Results found: {remoteSearchResults.Count()} results for {result.ExtractedName}.");
                Log.Info($"Compare Result {result.ExtractedName} and {remoteSearchResults.ToList()[0].Name}");
                finalResult =
                    remoteSearchResults
                        .FirstOrDefault(m => RegexExtensions.NormalizeString(m.Name) == RegexExtensions.NormalizeString(result.ExtractedName));
            }//need to sort the results based on name to increase likelyhood of best match

            if (finalResult == null) return null;

            // We are in the good position, we can create the item
            var organizationRequest = new MovieFileOrganizationRequest
            {
                Name = finalResult.Name, //RegexExtensions.NormalizeString(finalResult.Name) != RegexExtensions.NormalizeString(result.ExtractedName) ? result.ExtractedName : finalResult.Name, //<== This should keep the name in different languages
                ProviderIds  = finalResult.ProviderIds,
                Year = finalResult.ProductionYear,
                TargetFolder = options.DefaultMovieLibraryPath,
                        
            };

            //Got the year we needed! Update the result in the DB.
            result.ExtractedYear = finalResult.ProductionYear;
            OrganizationService.SaveResult(result, cancellationToken);


            return CreateNewMovie(organizationRequest, targetFolder.Path, result, options, cancellationToken);

        }
        
        private void RemoveExistingLibraryItem(FileOrganizationResult result)
        {
            var existingItems = LibraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Movie) },
                Recursive = true,
                DtoOptions = new DtoOptions(true),
                SearchTerm = result.ExtractedName,
                Years = result.ExtractedYear.HasValue ? new[] {result.ExtractedYear.Value} : Array.Empty<int>()
            });

            var itemToRemove = string.Empty;
            foreach(var item in existingItems)
            {
                if(FileSystem.GetFileNameWithoutExtension(item.Path) == FileSystem.GetFileNameWithoutExtension(result.TargetPath))
                {
                    itemToRemove = item.Path;
                }
            }
            
            try
            {
                if (!string.IsNullOrEmpty(itemToRemove))
                {
                    FileSystem.DeleteFile(itemToRemove);
                }
               
            }
            catch (Exception ex)
            {
                Log.Warn("Error deleting {0}", ex, itemToRemove);
            }
        }
        private Movie GetMatchingMovie(string movieName, int? movieYear, FileOrganizationResult result)
        {
            var parsedName = LibraryManager.ParseName(movieName.AsSpan());
            
            var yearInName = parsedName.Year;
            var nameWithoutYear = parsedName.Name;

            if (string.IsNullOrWhiteSpace(nameWithoutYear))
            {
                nameWithoutYear = movieName;
            }

            if (!yearInName.HasValue)
            {
                yearInName = movieYear;
            }

            var movieResult = LibraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Movie) },
                Recursive = true,
                DtoOptions = new DtoOptions(true),
                Years = yearInName.HasValue ? new[] { yearInName.Value } : Array.Empty<int>(),
                

            }).Cast<Movie>();

            var movieItems = movieResult.ToList();
            
            var movies = movieItems.Where(m => RegexExtensions.NormalizeString(m.Name) == RegexExtensions.NormalizeString(nameWithoutYear)).ToList();

            if (!movies.Any())
            {
                movies = movieItems.Where(m => RegexExtensions.NormalizeString(m.Name).ContainsIgnoreCase(RegexExtensions.NormalizeString(nameWithoutYear))).ToList();
            }

            if (!movies.Any())
            {
                return null;
            }

            //var resolution = movies.FirstOrDefault(m =>  !ResolutionExists(m, result.ExtractedResolution));

            //if (resolution != null)
            //{
            //    return resolution;
            //}

            return null;
        }
        

        private string GetMovieFileName(string sourcePath, FileOrganizationResult result, AutoOrganizeOptions options)
        {
            var textInfo        = new CultureInfo("en-US", false).TextInfo;
            var movieName       = textInfo.ToTitleCase(FileSystem.GetValidFilename(result.ExtractedName).Trim()); //Make sure the name is:  "Title Case"
            var productionYear  = result.ExtractedYear.ToString() ?? "";
            var edition         = result.ExtractedEdition ?? "";
            var resolution      = result.ExtractedResolution.Name;
            var sourceExtension = (Path.GetExtension(sourcePath) ?? string.Empty).TrimStart('.');

            var pattern = options.MoviePattern;

            if (string.IsNullOrWhiteSpace(pattern))
            {
                Log.Warn("GetMovieFolder: Configured movie name pattern is empty!");
                return null;
            } 

           
            var patternResult = pattern.Replace("%mn", movieName)
                .Replace("%m.n", movieName.Replace(" ", "."))
                .Replace("%m_n", movieName.Replace(" ", "_"))
                .Replace("%my", productionYear)
                .Replace("%res", resolution)
                .Replace("%ext", sourceExtension)
                .Replace("%e", edition)
                .Replace("%fn", Path.GetFileNameWithoutExtension(sourcePath));

            // Finally, call GetValidFilename again in case user customized the movie expression with any invalid filename characters
            return FileSystem.GetValidFilename(patternResult).Trim();
        }

        private string GetMovieFolderName(FileOrganizationResult result, AutoOrganizeOptions options)
        {
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;

            var movieName       = textInfo.ToTitleCase(FileSystem.GetValidFilename(result.ExtractedName).Trim()); //Make sure the name is:  "Title Case"
            var productionYear  = result.ExtractedYear.ToString() ?? "";
            var pattern         = options.MovieFolderPattern;

            if (string.IsNullOrWhiteSpace(pattern))
            {
                Log.Warn("GetMovieFolder: Configured movie name pattern is empty!");
                return null;
            }

            var patternResult = pattern.Replace("%mn", movieName)
                .Replace("%m.n", movieName.Replace(" ", "."))
                .Replace("%m_n", movieName.Replace(" ", "_"))
                .Replace("%my", productionYear)
                .Replace("%res", result.ExtractedResolution.Name)
                .Replace("%e", RegexExtensions.GetReleaseEditionFromFileName(Path.GetFileName(result.OriginalPath)))
                .Replace("%fn", Path.GetFileNameWithoutExtension(result.OriginalPath));

            // Finally, call GetValidFilename again in case user customized the movie expression with any invalid filename characters
            return FileSystem.GetValidFilename(patternResult).Trim();
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

        
        private string ResolveTargetFolder(FileOrganizationResult result, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            var entityMatchingScore = 0;
            var targetFolderResult = "";
            //Check smart matches for target folder rules
            var smartMatches = OrganizationService.GetSmartMatchInfos();
            var customSmartMatches = smartMatches.Items.Where(s => s.IsCustomUserDefinedEntry).ToList();
            
            //Only attempt comparison if the user has added custom smart matches.
            if (customSmartMatches.Any())
            {
                //Replace any non-alpha/non-numeric characters from the file name and replace with space, then split it by spaces.
                var fileNameEntities = Regex.Replace(result.OriginalPath, @"[^A-Za-z0-9\s+]", " ", RegexOptions.IgnoreCase).Replace("  ", " ").Trim().Split(' ');

                foreach (var match in customSmartMatches.Where(m => m.OrganizerType == FileOrganizerType.Movie))
                {
                    var intersectingEntities = match.MatchStrings.Intersect(fileNameEntities).ToList();
                    if (intersectingEntities.Count() <= entityMatchingScore) continue;
                    entityMatchingScore = intersectingEntities.Count();
                    targetFolderResult = match.TargetFolder;
                }

                if (!string.IsNullOrEmpty(targetFolderResult))
                {
                    return targetFolderResult;
                }
            }

            var targetFolder = "";
            if (!string.IsNullOrEmpty(options.DefaultMovieLibraryPath))
            {
                targetFolder = options.DefaultMovieLibraryPath;
            } 
            else
            {
                //The user didn't filling the settings - warn the log, return failure - that is all
                throw new InvalidTargetFolderException($"Auto sorting for {result.ExtractedName} is not possible. Please choose a default library path in settings");
            }

            return targetFolder;
        }
    }
}
