using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Naming.Common;
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
    public class MovieOrganizer
    {
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly IFileOrganizationService _organizationService;
        private readonly IProviderManager _providerManager;

        //private readonly CultureInfo _usCulture = new CultureInfo("en-US");
        public static event EventHandler<GenericEventArgs<FileOrganizationResult>> ItemUpdated;

        public MovieOrganizer(IFileOrganizationService organizationService, IFileSystem fileSystem, ILogger logger, ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, IProviderManager providerManager)
        {
            _organizationService = organizationService;
            _fileSystem          = fileSystem;
            _logger              = logger;
            _libraryManager      = libraryManager;
            _libraryMonitor      = libraryMonitor;
            _providerManager     = providerManager;
        }

        private FileOrganizerType CurrentFileOrganizerType => FileOrganizerType.Movie;

        public async Task<FileOrganizationResult> OrganizeMovieFile(bool? requestToMoveFile, string path, MovieFileOrganizationOptions options, CancellationToken cancellationToken)
        {                
            _logger.Info("Sorting file {0}", path);

            var result = new FileOrganizationResult
            {
                Date = DateTime.UtcNow,
                OriginalPath = path,
                OriginalFileName = Path.GetFileName(path),
                ExtractedResolution = GetStreamResolutionFromFileName(Path.GetFileName(path)),
                ExtractedEdition = GetReleaseEditionFromFileName(Path.GetFileName(path)),
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
                //Stop the movie sort here if the item has been identified as a TV Episode.
                //If the item was found to be an episode and the result was not a failure then return that Episode data instead of attempting movie matches.
                if(dbResult.Type == FileOrganizerType.Episode && dbResult.Status != FileSortingStatus.Failure)
                {
                    return dbResult;
                }                

                result = dbResult;
            }
            
           

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
                _logger.Info("Auto-organize Path is locked. Please try again later.");
                return result;
            }

            try
            {       
                

                var movieInfo = _libraryManager.IsVideoFile(path.AsSpan()) ? _libraryManager.ParseName(Path.GetFileName(path).AsSpan()) : new ItemLookupInfo();

                var movieName = movieInfo.Name;
                
                if (!string.IsNullOrEmpty(movieName))
                {
                    var movieYear = movieInfo.Year;

                    _logger.Debug("Extracted information from {0}. Movie {1}, Year {2}", path, movieName, movieYear);
                                       
                    
                    await OrganizeMovie(requestToMoveFile, path, movieName, movieYear, options, result, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var msg = $"Unable to determine movie name from {path}";
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
            catch (IOException ex)
            {
                if (ex.Message.Contains("being used by another process"))
                {
                    var errorMsg =
                        $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = errorMsg;
                    _logger.ErrorException(errorMsg, ex);
                    
                }
                else
                {
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = ex.Message;
                    _logger.ErrorException("Error organizing file", ex);
                }
            }
            catch (OrganizationException ex)
            {
                
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

        private Movie CreateNewMovie(MovieFileOrganizationRequest request, BaseItem targetFolder, FileOrganizationResult result, MovieFileOrganizationOptions options, CancellationToken cancellationToken)
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

                movie.Path = Path.Combine(request.TargetFolder, newPath);
            }

            return movie;
        }

        public FileOrganizationResult OrganizeWithCorrection(bool? requestToMoveFile, MovieFileOrganizationRequest request, MovieFileOrganizationOptions options, CancellationToken cancellationToken)
        {
            
            var result = _organizationService.GetResult(request.ResultId);

            try
            {
                Movie movie = null;

                if (request.NewMovieProviderIds.Count > 0)
                {
                    BaseItem targetFolder = null;

                    if (!string.IsNullOrEmpty(options.DefaultMovieLibraryPath))
                    {
                        targetFolder = _libraryManager.FindByPath(options.DefaultMovieLibraryPath, true);
                    }

                    // To avoid movie duplicate by mistake (Missing SmartMatch and wrong selection in UI)
                    movie = CreateNewMovie(request, targetFolder, result, options, cancellationToken);
                }

                if (movie == null)
                {
                    // Existing movie
                    movie = (Movie)_libraryManager.GetItemById(request.MovieId);
                    var fileName = GetMovieFileName(result.OriginalPath, movie, options);
                    //var newPath = GetMovieFolder(result.OriginalPath, movie, options);
                    var targetFolder = _fileSystem.GetDirectoryName(movie.Path);
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

                    _logger.Info("Organize with corrections: " + movie.Path);
                }

                // We manually set the media as Movie 
                result.Type = CurrentFileOrganizerType;

                OrganizeMovie(requestToMoveFile, result.OriginalPath, movie, options, null, result, cancellationToken);

               //organizationService.SaveResult(result, CancellationToken.None);

            }
            catch (IOException ex)
            {
                if (ex.Message.Contains("being used by another process"))
                {
                    var errorMsg = $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.InUse;
                    result.StatusMessage = errorMsg;
                    _logger.ErrorException(errorMsg, ex);
                    
                }
                else
                {
                    result.Status = FileSortingStatus.Failure;
                    result.StatusMessage = ex.Message;
                    _logger.ErrorException("Error organizing file", ex);
                }
            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
            }

            _organizationService.SaveResult(result, CancellationToken.None);

            return result;
        }


        private async Task OrganizeMovie(bool? requestToMoveFile, string sourcePath, string movieName, int? movieYear, MovieFileOrganizationOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
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
                    result.Type = CurrentFileOrganizerType; 
                    result.StatusMessage = msg;
                    _organizationService.RemoveFromInprogressList(result);
                    //_organizationService.SaveResult(result, cancellationToken);
                                       
                    throw new OrganizationException(msg);
                }
            }

            // We detected an Movie (either auto-detect or in library)
            // We have all the chance that the media type is an Movie
            result.Type = CurrentFileOrganizerType;

            OrganizeMovie(requestToMoveFile, sourcePath, movie, options, searchResult, result, cancellationToken);

        }

        private void OrganizeMovie(bool? requestToMoveFile,string sourcePath, Movie movie, MovieFileOrganizationOptions options, RemoteSearchResult remoteResult, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            OrganizeMovie(requestToMoveFile,sourcePath, movie, options,result, cancellationToken);
        }

        private void OrganizeMovie(bool? requestToMoveFile, string sourcePath, Movie movie, MovieFileOrganizationOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            //Handle user request from UI
            if (requestToMoveFile.HasValue)
            {                
                if (requestToMoveFile.Value && !string.IsNullOrWhiteSpace(result.TargetPath))
                {
                    //The request came in from the client. The file needs to be moved into the target folder dispite the status.
                    _logger.Info("Auto organize request to move file");
                    _logger.Info("Processing " + result.TargetPath);
                    result.Status = FileSortingStatus.Processing;
                    _logger.Info($"Auto organize adding {result.TargetPath} to inprogress list");
                    _organizationService.AddToInProgressList(result, true);
                    _organizationService.SaveResult(result, cancellationToken);
                    PerformFileSorting(requestToMoveFile, options, result, cancellationToken);
                    _logger.Info($"Auto organize {result.TargetPath} success.");
                    _logger.Info($"Auto organize removing {result.TargetPath} from inprogress list");
                    
                    _organizationService.RemoveFromInprogressList(result);
                    _organizationService.SaveResult(result, cancellationToken);

                    //We can't rely on the Removal from inprogress Items to update the UI. We'll try again here.
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
                    return;
                }
            }

            bool isNew = string.IsNullOrWhiteSpace(result.Id);

            if (isNew)
            {               
                _organizationService.SaveResult(result, cancellationToken);
            }

            if (!_organizationService.AddToInProgressList(result, isNew))
            {
                //result.Status = FileSortingStatus.Waiting;
                //_organizationService.SaveResult(result, cancellationToken);
                throw new OrganizationException("File is currently processed otherwise. Please try again later.");
            }

            
            result.ExtractedResolution = GetStreamResolutionFromFileName(sourcePath);
            result.ExtractedEdition = GetReleaseEditionFromFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(result.TargetPath))
            {
                result.TargetPath = Path.Combine(_fileSystem.GetDirectoryName(movie.Path), GetMovieFileName(sourcePath, movie, options));
            }

            try
            {
                // Proceed to sort the file
               
               _logger.Info("Sorting file {0} into movie {1}", sourcePath, result.TargetPath);
               _logger.Info("AUTO ORGANIZE RESULT TARGET PATH: " + result.TargetPath);

               var fileExists = _fileSystem.FileExists(result.TargetPath); //|| _fileSystem.DirectoryExists(_fileSystem.GetDirectoryName(result.TargetPath));

                //if (!options.OverwriteExistingFiles)
                if (!options.AutoDetectMovie)
                {
                    if (options.CopyOriginalFile && fileExists && IsSameMovie(sourcePath,  movie.Path))
                    {
                        var msg =
                            $"File '{sourcePath}' already exists in target path '{movie.Path}', stopping organization";
                        _logger.Info(msg);
                        result.Status = FileSortingStatus.SkippedExisting;
                        result.StatusMessage = msg;
                        _organizationService.RemoveFromInprogressList(result);
                        _organizationService.SaveResult(result, cancellationToken);
                        return;
                    }
                    

                    if (fileExists)
                    {
                       
                        var msg = string.Empty;
                        
                        //The resolution of the current source movie, and the current library item are the same - mark as existing
                        if (!IsNewStreamResolution(movie, result.ExtractedResolution))
                        {
                            msg = string.Format("File '{0}' already exists as '{1}', stopping organization", sourcePath, movie.Path);
                            _logger.Info(msg);
                            result.Status = FileSortingStatus.SkippedExisting;
                            result.StatusMessage = msg;
                            result.TargetPath = movie.Path;                           
                            _organizationService.RemoveFromInprogressList(result);
                            _organizationService.SaveResult(result, cancellationToken);
                            return;
                        }
                        else //The movie exists in the library, but the new source version has a different resolution
                        { 
                            result.TargetPath = result.TargetPath ?? Path.Combine(_libraryManager.GetVirtualFolders()
                                .FirstOrDefault(i => string.Equals(i.CollectionType, CollectionType.Movies.ToString(), StringComparison.OrdinalIgnoreCase))
                                .Locations
                                .FirstOrDefault(i => movie.Path.Contains(i)), GetMoviePath(result.OriginalPath, movie, options));

                            msg = $"The library currently contains the movie {movie.Name}, but it may have a different resolution than the current source file.";

                            _logger.Info(msg);
                            result.Status = FileSortingStatus.NewResolution;
                            result.StatusMessage = msg;
                            _organizationService.RemoveFromInprogressList(result);
                            _organizationService.SaveResult(result, cancellationToken);
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
                    _logger.ErrorException(errorMsg, ex);
                    EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger); //Update the UI
                }
            }
            catch (Exception ex)
            {
                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = ex.Message;
                _logger.Warn(ex.Message);
            }

            finally
            {
                _organizationService.RemoveFromInprogressList(result);
                _organizationService.SaveResult(result, cancellationToken);
            }
        }

        private void PerformFileSorting(bool? requestToMoveFile, MovieFileOrganizationOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {

            // We should probably handle this earlier so that we never even make it this far
            if (string.Equals(result.OriginalPath, result.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
                       

            _libraryMonitor.ReportFileSystemChangeBeginning(result.TargetPath);
            
            //Check to see if the library already has this entry
            var targetAlreadyExists = _fileSystem.FileExists(result.TargetPath) || _fileSystem.DirectoryExists(Path.GetDirectoryName(result.TargetPath));

            if (requestToMoveFile.HasValue)
            {
                if (targetAlreadyExists && options.OverwriteExistingFiles && requestToMoveFile.Value)
                {
                    RemoveExistingLibraryItem(result);
                }
            }

            if (!_fileSystem.DirectoryExists(_fileSystem.GetDirectoryName(result.TargetPath)))
            {
                _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(result.TargetPath));
            }

            try
            {
                
                if (targetAlreadyExists || options.CopyOriginalFile)
                {
                    try
                    {
                         _logger.Info("Auto organize copying file");                    
                        _fileSystem.CopyFile(result.OriginalPath, result.TargetPath, true);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("disk space"))
                        {
                            _logger.Warn(ex.Message);
                            result.Status = FileSortingStatus.NotEnoughDiskSpace;
                            result.StatusMessage = "There is not enough disk space on the drive to move this file";
                          
                        } 
                        else if (ex.Message.Contains("used by another process"))
                        {
                            _logger.Warn(ex.Message);
                            result.Status = FileSortingStatus.InUse;
                            result.StatusMessage = "The file is being streamed to a emby device. Please try again later.";                         
                        }
                        _organizationService.RemoveFromInprogressList(result);
                        _organizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger); //Update the UI
                        return;
                    }                
                }
                else
                {
                    
                    try 
                    {
                        _logger.Info("Auto organize moving file");
                        _fileSystem.MoveFile(result.OriginalPath, result.TargetPath);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("disk space"))
                        {
                            _logger.Warn(ex.Message);
                            result.Status = FileSortingStatus.NotEnoughDiskSpace;
                            result.StatusMessage = "There is not enough disk space on the drive to move this file";                          
                        } 
                        else if (ex.Message.Contains("used by another process"))
                        {
                            _logger.Warn(ex.Message);
                            result.Status = FileSortingStatus.InUse;
                            result.StatusMessage = "The file is being streamed to a emby device. Please try again later.";                           
                        }
                        _organizationService.RemoveFromInprogressList(result);
                        _organizationService.SaveResult(result, cancellationToken);
                        EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger); //Update the UI
                        return;
                    }
                }

                result.Status = FileSortingStatus.Success;
                result.StatusMessage = string.Empty;                
                _organizationService.RemoveFromInprogressList(result);
                _organizationService.SaveResult(result, cancellationToken);
            }
            catch (IOException ex)
            {
                if(ex.Message.Contains("being used by another process") && !result.IsInProgress)
                {                    
                    var errorMsg = $"Waiting to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";
                    result.Status = FileSortingStatus.Waiting;
                    result.StatusMessage = errorMsg;
                    _logger.ErrorException(errorMsg, ex);                    
                    _organizationService.RemoveFromInprogressList(result);
                    _organizationService.SaveResult(result, cancellationToken);
                    _libraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
                    return;
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to move file from {result.OriginalPath} to {result.TargetPath}: {ex.Message}";

                result.Status = FileSortingStatus.Failure;
                result.StatusMessage = errorMsg;
                _logger.ErrorException(errorMsg, ex);               
                _organizationService.RemoveFromInprogressList(result);
                _organizationService.SaveResult(result, cancellationToken);
                _libraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);
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

        private async Task<Tuple<Movie, RemoteSearchResult>> AutoDetectMovie(string movieName, int? movieYear, FileOrganizationResult result, MovieFileOrganizationOptions options, CancellationToken cancellationToken)
        {
            if (options.AutoDetectMovie)
            {
                var parsedName = _libraryManager.ParseName(movieName.AsSpan());

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
                    targetFolder = _libraryManager.FindByPath(options.DefaultMovieLibraryPath, true);
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

                var searchResultsTask = await _providerManager.GetRemoteSearchResults<Movie, MovieInfo>(new RemoteSearchQuery<MovieInfo>
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
            var existingItems = _libraryManager.GetItemList(new InternalItemsQuery
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
                if(_fileSystem.GetFileNameWithoutExtension(item.Path) == _fileSystem.GetFileNameWithoutExtension(result.TargetPath))
                {
                    itemToRemove = item.Path;
                }
            }
            
            try
            {
                _fileSystem.DeleteFile(itemToRemove);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error deleting {0}", ex, itemToRemove);
            }
        }
        private Movie GetMatchingMovie(string movieName, int? movieYear, BaseItem targetFolder, FileOrganizationResult result, MovieFileOrganizationOptions options)
        {
            var parsedName = _libraryManager.ParseName(movieName.AsSpan());
            
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
            

            var movie = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] {nameof(Movie)},
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
        private string GetMoviePath(string sourcePath, Movie movie, MovieFileOrganizationOptions options)
        {
            var movieFileName = "";

            if (options.CreateMovieInFolder)
            {
                movieFileName = Path.Combine(movieFileName, GetMovieFolder(sourcePath, movie, options));
            }

            movieFileName = Path.Combine(movieFileName, GetMovieFileName(sourcePath, movie, options));

            if (string.IsNullOrEmpty(movieFileName))
            {
                // cause failure
                return string.Empty;
            }

            return movieFileName;
        }

        private string GetMovieFileName(string sourcePath, BaseItem movie, MovieFileOrganizationOptions options)
        {
            return GetMovieNameInternal(sourcePath, movie, options.MoviePattern);
        }

        private string GetMovieFolder(string sourcePath, BaseItem movie, MovieFileOrganizationOptions options)
        {
            return GetMovieNameInternal(sourcePath, movie, options.MovieFolderPattern);
        }

        private string GetMovieNameInternal(string sourcePath, BaseItem movie, string pattern)
        {
            var movieName = _fileSystem.GetValidFilename(movie.Name).Trim();
            var productionYear = movie.ProductionYear;

            var sourceExtension = (Path.GetExtension(sourcePath) ?? string.Empty).TrimStart('.');

            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new OrganizationException("GetMovieFolder: Configured movie name pattern is empty!");
            }

            var result = pattern.Replace("%mn", movieName)
                .Replace("%m.n", movieName.Replace(" ", "."))
                .Replace("%m_n", movieName.Replace(" ", "_"))
                .Replace("%my", productionYear.ToString())
                .Replace("%res", GetStreamResolutionFromFileName(Path.GetFileName(sourcePath)))
                .Replace("%ext", sourceExtension)
                .Replace("%e", GetReleaseEditionFromFileName(Path.GetFileName(sourcePath)))
                .Replace("%fn", Path.GetFileNameWithoutExtension(sourcePath));

            // Finally, call GetValidFilename again in case user customized the movie expression with any invalid filename characters
            return _fileSystem.GetValidFilename(result).Trim();
        }

        private bool IsSameMovie(string sourcePath, string newPath)
        {
            try
            {
                var sourceFileInfo = _fileSystem.GetFileInfo(sourcePath);
                var destinationFileInfo = _fileSystem.GetFileInfo(newPath);
                   
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
