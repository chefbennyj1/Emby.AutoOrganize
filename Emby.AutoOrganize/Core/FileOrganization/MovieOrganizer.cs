using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Naming.Common;
using MediaBrowser.Common.Configuration;
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
using MediaBrowser.Model.Providers;

namespace Emby.AutoOrganize.Core.FileOrganization
{
    public class MovieOrganizer : IFileOrganizer
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
        {
            OrganizationService = organizationService;
            FileSystem          = fileSystem;
            Log                 = log;
            LibraryManager      = libraryManager;
            LibraryMonitor      = libraryMonitor;
            ProviderManager     = providerManager;
            
            //Instance            = this;
        }

        //private FileOrganizerType CurrentFileOrganizerType => FileOrganizerType.Movie;

        public async Task<FileOrganizationResult> OrganizeFile(bool? requestToMoveFile, string path, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {                
            Log.Info("Sorting file {0}", path);

            var result = new FileOrganizationResult
            {
                Date = DateTime.UtcNow,
                OriginalPath = path,
                OriginalFileName = Path.GetFileName(path),
                ExtractedResolution = GetStreamResolutionFromFileName(Path.GetFileName(path)),
                ExtractedEdition = GetReleaseEditionFromFileName(Path.GetFileName(path)),
                Type = FileOrganizerType.Movie,
                FileSize = FileSystem.GetFileInfo(path).Length
            };
            
           
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
                

                ////If the User has choose to monitor movies and episodes in the same folder.
                ////Stop the movie sort here if the item has been identified as a TV Episode.
                ////If the item was found to be an episode and the result was not a failure then return that Episode data instead of attempting movie matches.
                //if(dbResult.Type == FileOrganizerType.Episode && dbResult.Status != FileSortingStatus.Failure)
                //{
                //    return dbResult;
                //}                

                result = dbResult;
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
            

            if (LibraryMonitor.IsPathLocked(path.AsSpan()) && result.Status == FileSortingStatus.Processing)
            {
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                return result;
            }

            try
            {      
                
                var serverConfiguration = OrganizationService.GetServerConfiguration();
                var movieInfo = LibraryManager.IsVideoFile(path.AsSpan()) ? 
                    LibraryManager.ParseName(Path.GetFileName(path).AsSpan()) : 
                    new ItemLookupInfo()
                    {
                        MetadataCountryCode = serverConfiguration.MetadataCountryCode,
                        MetadataLanguage = serverConfiguration.PreferredMetadataLanguage
                    };

                var movieName = movieInfo.Name;
                
                if (!string.IsNullOrEmpty(movieName))
                {
                    var movieYear = movieInfo.Year;

                    Log.Debug("Extracted information from {0}. Movie {1}, Year {2}", path, movieName, movieYear);
                                       
                    
                    await OrganizeMovie(requestToMoveFile, path, movieName, movieYear, options, result, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var msg = $"Unable to determine movie name from {path}";
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
            catch (IOException ex)
            {
                if (ex.Message.Contains("being used by another process"))
                {
                    var errorMsg =
                        $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
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
            catch (OrganizationException ex)
            {
                
            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                Log.ErrorException("Error organizing file", ex);
            }
            
            OrganizationService.SaveResult(result, CancellationToken.None);

            return result;
        }
        

        private Movie CreateNewMovie(MovieFileOrganizationRequest request, BaseItem targetFolder, FileOrganizationResult result, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            // To avoid Movie duplicate by mistake (Missing SmartMatch and wrong selection in UI)
            var movie = GetMatchingMovie(request.NewMovieName, request.NewMovieYear, targetFolder, result, options);

            if (movie == null)
            {
                // We're having a new movie here
                movie = new Movie
                {
                    Name = request.NewMovieName,
                    ProductionYear = request.NewMovieYear,
                    IsInMixedFolder = !options.CreateMovieInFolder,
                    ProviderIds = request.NewMovieProviderIds,
                };

                var newPath = GetMoviePath(result.OriginalPath, movie, options);

                if (string.IsNullOrEmpty(newPath))
                {
                    var msg = $"Unable to sort {result.OriginalPath} because target path could not be determined.";
                    throw new OrganizationException(msg);
                }

                movie.Path = Path.Combine(request.TargetFolder ?? targetFolder.Path, newPath);
            }

            return movie;
        }

        public FileOrganizationResult OrganizeWithCorrection(MovieFileOrganizationRequest request, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            
            var result = OrganizationService.GetResult(request.ResultId);

            try
            {
                Movie movie = null;

                if (request.NewMovieProviderIds.Count > 0)
                {
                    BaseItem targetFolder = null;

                    if (!string.IsNullOrEmpty(options.DefaultMovieLibraryPath))
                    {
                        targetFolder = LibraryManager.FindByPath(options.DefaultMovieLibraryPath, true);
                    }

                    // To avoid movie duplicate by mistake (Missing SmartMatch and wrong selection in UI)
                    movie = CreateNewMovie(request, targetFolder, result, options, cancellationToken);
                }

                if (movie == null)
                {
                    // Existing movie
                    movie = (Movie)LibraryManager.GetItemById(request.MovieId);
                    var fileName = GetMovieFileName(result.OriginalPath, movie, options);
                    //var newPath = GetMovieFolder(result.OriginalPath, movie, options);
                    var targetFolder = FileSystem.GetDirectoryName(movie.Path);
                    //var targetFolder = _libraryManager
                    //.GetVirtualFolders()
                    //.Where(i => string.Equals(i.CollectionType, CollectionType.Movies.ToString(), StringComparison.OrdinalIgnoreCase))
                    //.FirstOrDefault()
                    //.Locations
                    //.Where(i => movie.Path.Contains(i))
                    //.FirstOrDefault();
                   
                    movie.Path = Path.Combine(targetFolder, fileName);
                    result.TargetPath = movie.Path;
                    
                    //request.RequestToMoveFile = true;

                    Log.Info("Organize with corrections: " + movie.Path);
                }

                // We manually set the media as Movie 
                //result.Type = CurrentFileOrganizerType;

                OrganizeMovie(request.RequestToMoveFile, result.OriginalPath, movie, options, null, result, cancellationToken);

               //organizationService.SaveResult(result, CancellationToken.None);

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
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
            }

            OrganizationService.SaveResult(result, CancellationToken.None);

            return result;
        }


        private async Task OrganizeMovie(bool? requestToMoveFile, string sourcePath, string movieName, int? movieYear, AutoOrganizeOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            var movie = GetMatchingMovie(movieName, movieYear, null, result, options);
            RemoteSearchResult searchResult = null;

            if (movie == null)
            {
                var autoResult = await AutoDetectMovie(movieName, movieYear, result, options, cancellationToken).ConfigureAwait(false);

                movie = autoResult?.Item1;
                searchResult = autoResult?.Item2;

                if (movie == null)
                {
                    var msg = string.Format("Unable to find movie in library matching name {0}", movieName);
                    result.Status = FileSortingStatus.Failure;
                    result.Type = FileOrganizerType.Movie; 
                    result.StatusMessage = msg;
                    OrganizationService.RemoveFromInprogressList(result);
                    //_organizationService.SaveResult(result, cancellationToken);
                                       
                    throw new OrganizationException(msg);
                }
            }

            // We detected an Movie (either auto-detect or in library)
            // We have all the chance that the media type is an Movie
            //result.Type = CurrentFileOrganizerType;

            OrganizeMovie(requestToMoveFile, sourcePath, movie, options, searchResult, result, cancellationToken);

        }

        private void OrganizeMovie(bool? requestToMoveFile,string sourcePath, Movie movie, AutoOrganizeOptions options, RemoteSearchResult remoteResult, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            OrganizeMovie(requestToMoveFile,sourcePath, movie, options,result, cancellationToken);
        }

        private void OrganizeMovie(bool? requestToMoveFile, string sourcePath, Movie movie, AutoOrganizeOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            //Handle user request from UI
            if (requestToMoveFile.HasValue)
            {                
                if (requestToMoveFile.Value && !string.IsNullOrWhiteSpace(result.TargetPath))
                {
                    //The request came in from the client. The file needs to be moved into the target folder dispite the status.
                    Log.Info("Auto organize request to move file");
                    Log.Info("Processing " + result.TargetPath);
                    result.Status = FileSortingStatus.Processing;
                    result.FileSize = FileSystem.GetFileInfo(result.OriginalPath).Length; //Update the file size so it will show the actual size of the file here. It may have been copying before.
                    Log.Info($"Auto organize adding {result.TargetPath} to inprogress list");
                    OrganizationService.AddToInProgressList(result, true);
                    OrganizationService.SaveResult(result, cancellationToken);
                    PerformFileSorting(requestToMoveFile, options, result, cancellationToken);
                    Log.Info($"Auto organize {result.TargetPath} success.");
                    Log.Info($"Auto organize removing {result.TargetPath} from inprogress list");
                    
                    OrganizationService.RemoveFromInprogressList(result);
                    OrganizationService.SaveResult(result, cancellationToken);

                    //We can't rely on the Removal from inprogress Items to update the UI. We'll try again here.
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
                    return;
                }
            }

            bool isNew = string.IsNullOrWhiteSpace(result.Id);

            if (isNew)
            {               
                OrganizationService.SaveResult(result, cancellationToken);
            }

            if (!OrganizationService.AddToInProgressList(result, isNew))
            {
                
                //_organizationService.SaveResult(result, cancellationToken);
                throw new OrganizationException("File is currently processed otherwise. Please try again later.");
            }

            
            result.ExtractedResolution = GetStreamResolutionFromFileName(sourcePath);
            result.ExtractedEdition = GetReleaseEditionFromFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(result.TargetPath))
            {
                result.TargetPath = Path.Combine(FileSystem.GetDirectoryName(movie.Path), GetMovieFileName(sourcePath, movie, options));
            }

            try
            {
                // Proceed to sort the file
               
               Log.Info("Sorting file {0} into movie {1}", sourcePath, result.TargetPath);
               Log.Info("AUTO ORGANIZE RESULT TARGET PATH: " + result.TargetPath);

               var fileExists = FileSystem.FileExists(result.TargetPath); //|| _fileSystem.DirectoryExists(_fileSystem.GetDirectoryName(result.TargetPath));

                //if (!options.OverwriteExistingFiles)
                if (!options.AutoDetectMovie)
                {
                    if (options.CopyOriginalFile && fileExists && IsSameMovie(sourcePath,  movie.Path))
                    {
                        var msg =
                            $"File '{sourcePath}' already exists in target path '{movie.Path}', stopping organization";
                        Log.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        OrganizationService.RemoveFromInprogressList(result);
                        OrganizationService.SaveResult(result, cancellationToken);
                        return;
                    }
                    

                    if (fileExists)
                    {
                       
                        var msg = string.Empty;
                        
                        //The resolution of the current source movie, and the current library item are the same - mark as existing
                        if (!IsNewStreamResolution(movie, result.ExtractedResolution))
                        {
                            msg = string.Format("File '{0}' already exists as '{1}', stopping organization", sourcePath, movie.Path);
                            Log.Info(msg);
                            result.Status = FileSortingStatus.SkippedExisting;
                            result.StatusMessage = msg;
                            result.TargetPath = movie.Path;                           
                            OrganizationService.RemoveFromInprogressList(result);
                            OrganizationService.SaveResult(result, cancellationToken);
                            return;
                        }
                        else //The movie exists in the library, but the new source version has a different resolution
                        { 
                            result.TargetPath = result.TargetPath ?? Path.Combine(LibraryManager.GetVirtualFolders()
                                .FirstOrDefault(i => string.Equals(i.CollectionType, CollectionType.Movies.ToString(), StringComparison.OrdinalIgnoreCase))
                                .Locations
                                .FirstOrDefault(i => movie.Path.Contains(i)), GetMoviePath(result.OriginalPath, movie, options));

                            msg = $"The library currently contains the movie {movie.Name}, but it may have a different resolution than the current source file.";

                            Log.Info(msg);
                            result.Status = FileSortingStatus.NewResolution;
                            result.StatusMessage = msg;
                            OrganizationService.RemoveFromInprogressList(result);
                            OrganizationService.SaveResult(result, cancellationToken);
                            return;
                        }

                    }
                }

                PerformFileSorting(requestToMoveFile, options, result, cancellationToken);
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
                    Log.ErrorException(errorMsg, ex);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                }
            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                Log.Warn(ex.Message);
            }

            finally
            {
                OrganizationService.RemoveFromInprogressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
            }
        }

        private void PerformFileSorting(bool? requestToMoveFile, AutoOrganizeOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {

            // We should probably handle this earlier so that we never even make it this far
            if (string.Equals(result.OriginalPath, result.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
             
            

            LibraryMonitor.ReportFileSystemChangeBeginning(result.TargetPath);
            
            //Check to see if the library already has this entry
            var targetAlreadyExists = FileSystem.FileExists(result.TargetPath) || FileSystem.DirectoryExists(Path.GetDirectoryName(result.TargetPath));

            if (requestToMoveFile.HasValue)
            {
                if (targetAlreadyExists && options.OverwriteExistingFiles && requestToMoveFile.Value)
                {
                    RemoveExistingLibraryItem(result);
                }
            }

            if (!FileSystem.DirectoryExists(FileSystem.GetDirectoryName(result.TargetPath)))
            {
                FileSystem.CreateDirectory(FileSystem.GetDirectoryName(result.TargetPath));
            }

            try
            {
                
                if (targetAlreadyExists || options.CopyOriginalFile)
                {
                    try
                    {
                         Log.Info("Auto organize copying file");                    
                        FileSystem.CopyFile(result.OriginalPath, result.TargetPath, true);
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
                        OrganizationService.RemoveFromInprogressList(result);
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                        return;
                    }                
                }
                else
                {
                    
                    try 
                    {
                        Log.Info("Auto organize moving file");
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
                        OrganizationService.RemoveFromInprogressList(result);
                        OrganizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log); //Update the UI
                        return;
                    }
                }

                result.Status = FileSortingStatus.Success;
                result.StatusMessage = string.Empty;                
                OrganizationService.RemoveFromInprogressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
            }
            catch (IOException ex)
            {
                if(ex.Message.Contains("being used by another process") && !result.IsInProgress)
                {                    
                    var errorMsg = $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = errorMsg;
                    Log.ErrorException(errorMsg, ex);                    
                    OrganizationService.RemoveFromInprogressList(result);
                    OrganizationService.SaveResult(result, cancellationToken);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), Log);
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
                OrganizationService.RemoveFromInprogressList(result);
                OrganizationService.SaveResult(result, cancellationToken);
                LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
                return;
            }
            finally
            {
                LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
            }

            if (targetAlreadyExists && !options.CopyOriginalFile)
            {
                try
                {
                    FileSystem.DeleteFile(result.OriginalPath);
                }
                catch (Exception ex)
                {
                    Log.ErrorException("Error deleting {0}", ex, result.OriginalPath);
                }
            }

            
        }

        private async Task<Tuple<Movie, RemoteSearchResult>> AutoDetectMovie(string movieName, int? movieYear, FileOrganizationResult result, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            if (options.AutoDetectMovie)
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

                var movieInfo = new MovieInfo
                {
                    Name = nameWithoutYear,
                    Year = yearInName,
                    MetadataCountryCode = metadataCountryCode,
                    MetadataLanguage = metadataLanguage
                };

                var searchResultsTask = await ProviderManager.GetRemoteSearchResults<Movie, MovieInfo>(new RemoteSearchQuery<MovieInfo>
                {
                    SearchInfo = movieInfo

                }, targetFolder, cancellationToken);

                var finalResult = searchResultsTask.FirstOrDefault();

                if (finalResult != null)
                {
                    // We are in the good position, we can create the item
                    var organizationRequest = new MovieFileOrganizationRequest
                    {
                        NewMovieName = finalResult.Name,
                        NewMovieProviderIds = finalResult.ProviderIds,
                        NewMovieYear = finalResult.ProductionYear,
                        TargetFolder = options.DefaultMovieLibraryPath,
                        
                    };

                    var movie = CreateNewMovie(organizationRequest, targetFolder, result, options, cancellationToken);

                    return new Tuple<Movie, RemoteSearchResult>(movie, finalResult);
                }
            }

            return null;
        }
        
        private void RemoveExistingLibraryItem(FileOrganizationResult result)
        {
            var existingItems = LibraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] {nameof(Movie)},
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
                FileSystem.DeleteFile(itemToRemove);
            }
            catch (Exception ex)
            {
                Log.ErrorException("Error deleting {0}", ex, itemToRemove);
            }
        }
        private Movie GetMatchingMovie(string movieName, int? movieYear, BaseItem targetFolder, FileOrganizationResult result, AutoOrganizeOptions options)
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

            result.ExtractedName = nameWithoutYear;
            result.ExtractedYear = yearInName;
            

            var movie = LibraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Movie) },
                Recursive = true,
                DtoOptions = new DtoOptions(true),
                AncestorIds = targetFolder == null ? Array.Empty<long>() : new[] { targetFolder.InternalId },
                SearchTerm = nameWithoutYear,
                Years = yearInName.HasValue ? new[] { yearInName.Value } : Array.Empty<int>()
            })
                .Cast<Movie>()
                // Check For the right extension (to handle quality upgrade)
                .FirstOrDefault(m => Path.GetExtension(m.Path) == Path.GetExtension(result.OriginalPath));
                //TODO: checking extentions for quailty upgrades is priobabaly not the best. We should check MediaStreams for higher video outputs
            return movie;
        }

        /// <summary>
        /// Gets the new path.
        /// </summary>
        /// <param name="sourcePath">The source path.</param>
        /// <param name="movie">The movie.</param>
        /// <param name="options">The options.</param>
        /// <returns>System.String.</returns>
        private string GetMoviePath(string sourcePath, Movie movie, AutoOrganizeOptions options)
        {
            var movieFolderPath = "";

            if (options.CreateMovieInFolder)
            {
                movieFolderPath = Path.Combine(movieFolderPath, GetMovieFolder(sourcePath, movie, options));
            }

            movieFolderPath = Path.Combine(movieFolderPath, GetMovieFileName(sourcePath, movie, options));

            if (string.IsNullOrEmpty(movieFolderPath))
            {
                // cause failure
                Log.Warn("Unable to produce movie folder path.");
                return string.Empty;
            }

            return movieFolderPath;
        }

        private string GetMovieFileName(string sourcePath, BaseItem movie, AutoOrganizeOptions options)
        {
            return GetMovieNameInternal(sourcePath, movie, options.MoviePattern);
        }

        private string GetMovieFolder(string sourcePath, BaseItem movie, AutoOrganizeOptions options)
        {
            return GetMovieNameInternal(sourcePath, movie, options.MovieFolderPattern);
        }

        private string GetMovieNameInternal(string sourcePath, BaseItem movie, string pattern)
        {
            var movieName = FileSystem.GetValidFilename(movie.Name).Trim();
            var productionYear = movie.ProductionYear.ToString() ?? "";

            var sourceExtension = (Path.GetExtension(sourcePath) ?? string.Empty).TrimStart('.');

            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new OrganizationException("GetMovieFolder: Configured movie name pattern is empty!");
            }

            var result = pattern.Replace("%mn", movieName)
                .Replace("%m.n", movieName.Replace(" ", "."))
                .Replace("%m_n", movieName.Replace(" ", "_"))
                .Replace("%my", productionYear)
                .Replace("%res", GetStreamResolutionFromFileName(Path.GetFileName(sourcePath)))
                .Replace("%ext", sourceExtension)
                .Replace("%e", GetReleaseEditionFromFileName(Path.GetFileName(sourcePath)))
                .Replace("%fn", Path.GetFileNameWithoutExtension(sourcePath));

            // Finally, call GetValidFilename again in case user customized the movie expression with any invalid filename characters
            return FileSystem.GetValidFilename(result).Trim();
        }

        private bool IsSameMovie(string sourcePath, string newPath)
        {
            try
            {
                var sourceFileInfo = FileSystem.GetFileInfo(sourcePath);
                var destinationFileInfo = FileSystem.GetFileInfo(newPath);
                   
                if (sourceFileInfo.Length == destinationFileInfo.Length && sourceFileInfo.Extension == destinationFileInfo.Extension)
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

        private bool IsNewStreamResolution(Movie movie, string extractedResolution)
        {
            //We may have a library entery for this movie, but this particular copy of it may have a different Resolution.
            try
            {   
                return !movie.GetMediaStreams().Any(s => s.DisplayTitle.Contains(extractedResolution));
                //if (movie.GetMediaStreams().Any(s => s.DisplayTitle.Contains(extractedResolution)))
                //{
                //    return false;
                //}
                //return true;
            }
            catch (Exception)
            {
                return false;
            }
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

        private string GetReleaseEditionFromFileName(string sourceFileName)
        {
            var namingOptions = new NamingOptions();
            var pattern = $"{string.Join("|", namingOptions.VideoReleaseEditionFlags)}";
            var input = sourceFileName.Replace(".", " ").Replace("_", " ");
            var results = Regex.Matches(input, pattern, RegexOptions.IgnoreCase);
            return results.Count > 0 ? string.Join(" ", from Match match in results select match.Value) : "Theatrical Version";
        }

        private static string GetStreamResolutionFromFileName(string sourceFileName)
        {
            var namingOptions = new NamingOptions();
            
            foreach(var resolution in namingOptions.VideoResolutionFlags)
            {
                if(sourceFileName.Contains(resolution))
                {
                    return resolution;

                }
            }
            return string.Empty;
            
        }

        
    }
}
