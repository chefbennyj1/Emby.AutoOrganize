using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Core.FileOrganization;
using Emby.AutoOrganize.Data;
using Emby.AutoOrganize.Model;
using MediaBrowser.Common.Events;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;

namespace Emby.AutoOrganize.Core
{
    public class InternalFileOrganizationService : IFileOrganizationService
    {
        private readonly ITaskManager _taskManager;
        private readonly IFileOrganizationRepository _repo;
        private readonly ILogger _logger;
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILibraryManager _libraryManager;
        private readonly IServerConfigurationManager _config;
        private readonly IFileSystem _fileSystem;
        private readonly IProviderManager _providerManager;
        private readonly ConcurrentDictionary<string, bool> _inProgressItemIds = new ConcurrentDictionary<string, bool>();

        public event EventHandler<GenericEventArgs<FileOrganizationResult>> ItemAdded;
        public event EventHandler<GenericEventArgs<FileOrganizationResult>> ItemUpdated;
        public event EventHandler<GenericEventArgs<FileOrganizationResult>> ItemRemoved;
        public event EventHandler LogReset;

        public InternalFileOrganizationService(ITaskManager taskManager, IFileOrganizationRepository repo, ILogger logger, ILibraryMonitor libraryMonitor, ILibraryManager libraryManager, IServerConfigurationManager config, IFileSystem fileSystem, IProviderManager providerManager)
        {
            _taskManager = taskManager;
            _repo = repo;
            _logger = logger;
            _libraryMonitor = libraryMonitor;
            _libraryManager = libraryManager;
            _config = config;
            _fileSystem = fileSystem;
            _providerManager = providerManager;
            
        }

        public void BeginProcessNewFiles()
        {
            _taskManager.CancelIfRunningAndQueue<OrganizerScheduledTask>();
        }

        public void SaveResult(FileOrganizationResult result, CancellationToken cancellationToken)
        {
            if (result == null || string.IsNullOrEmpty(result.OriginalPath))
            {
                throw new ArgumentNullException("result");
            }

            result.Id = result.OriginalPath.GetMD5().ToString("N");

            _repo.SaveResult(result, cancellationToken);
        }

        public void SaveResult(SmartMatchResult result, CancellationToken cancellationToken)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            _repo.SaveResult(result, cancellationToken);
        }

        public QueryResult<FileOrganizationResult> GetResults(FileOrganizationResultQuery query)
        {
            var results = _repo.GetResults(query);

            foreach (var result in results.Items)
            {
                result.IsInProgress = _inProgressItemIds.ContainsKey(result.Id);
            }

            return results;
        }

        public FileOrganizationResult GetResult(string id)
        {
            var result = _repo.GetResult(id);

            if (result != null)
            {
                result.IsInProgress = _inProgressItemIds.ContainsKey(result.Id);
            }

            return result;
        }

        public FileOrganizationResult GetResultBySourcePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException("path");
            }

            var id = path.GetMD5().ToString("N");

            return GetResult(id);
        }

        public void DeleteOriginalFile(string resultId)
        {
            var result = _repo.GetResult(resultId);

            _logger.Info("Requested to delete {0}", result.OriginalPath);

            if (!AddToInProgressList(result, false))
            {
                throw new OrganizationException("Path is currently processed otherwise. Please try again later.");
            }

            try
            {
                _fileSystem.DeleteFile(result.OriginalPath);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error deleting {0}", ex, result.OriginalPath);
            }
            finally
            {
                RemoveFromInprogressList(result);
            }

            _repo.Delete(resultId);

            EventHelper.FireEventIfNotNull(ItemRemoved, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
        }

        private AutoOrganizeOptions GetAutoOrganizeOptions()
        {
            return _config.GetAutoOrganizeOptions();
        }

        public async Task PerformOrganization(string resultId, bool? requestToMoveFile)
        {
            var result = _repo.GetResult(resultId);

            var options = GetAutoOrganizeOptions();
            
         
            if (string.IsNullOrEmpty(result.TargetPath))
            {
                throw new ArgumentException("No target path available.");
            }

            FileOrganizationResult organizeResult;
            IFileOrganizer organizer;

            switch (result.Type)
            {
                case FileOrganizerType.Episode:
                     organizer = new EpisodeOrganizer(this, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);                                        

                    organizeResult = await organizer.OrganizeFile(requestToMoveFile, result.OriginalPath, options, CancellationToken.None)
                        .ConfigureAwait(false);

                    break;
                case FileOrganizerType.Movie:
                    organizer = new MovieOrganizer(this, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);

                    organizeResult = await organizer.OrganizeFile(requestToMoveFile, result.OriginalPath, options, CancellationToken.None)
                        .ConfigureAwait(false);
                    break;
                default:
                    throw new OrganizationException("No organizer exist for the type " + result.Type);
            }

            if (organizeResult.Status != FileSortingStatus.Success)
            {
                throw new OrganizationException(result.StatusMessage);
            }
        }

        public void ClearLog()
        {
            _repo.DeleteAll();
            EventHelper.FireEventIfNotNull(LogReset, this, EventArgs.Empty, _logger);
        }

        public void ClearCompleted()
        {
            _repo.DeleteCompleted();
            EventHelper.FireEventIfNotNull(LogReset, this, EventArgs.Empty, _logger);
        }

        public async Task PerformOrganization(EpisodeFileOrganizationRequest request)
        {
            var organizer = new EpisodeOrganizer(this, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);

            var options = GetAutoOrganizeOptions();

            var result = await organizer.OrganizeWithCorrection(request, options, CancellationToken.None).ConfigureAwait(false);

            if (result.Status != FileSortingStatus.Success)
            {
                throw new Exception(result.StatusMessage);
            }
        }

        public void PerformOrganization(MovieFileOrganizationRequest request)
        {
            var organizer = new MovieOrganizer(this, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);

            var options = GetAutoOrganizeOptions();
            var result = organizer.OrganizeWithCorrection(request, options, CancellationToken.None);

            if (result.Status != FileSortingStatus.Success)
            {
                throw new Exception(result.StatusMessage);
            }
        }

        public QueryResult<SmartMatchResult> GetSmartMatchInfos(FileOrganizationResultQuery query)
        {
            return _repo.GetSmartMatch(query);
        }

        public ServerConfiguration GetServerConfiguration()
        {
            return _config.Configuration;
        }

        public string GetVideoEncodingType(string fileName)
        {
            var streamEncodingRegex = new Regex(@"(x264|h264|H264|X264|xvid|xvidvd)([ _\,\.\(\)\[\]\-]|$)", RegexOptions.IgnoreCase);
            var encodingMatch = streamEncodingRegex.Match(fileName);
            return encodingMatch.Success ? encodingMatch.Groups[1].Value : string.Empty;
        }
        public FileOrganizerType GetFileOrganizerType(string fileName)
        {
            var regexDate = new Regex(@"\b(19|20|21)\d{2}\b");
            var testTvShow = new Regex(@"(?:([Ss](\d{1,2})[Ee](\d{1,2})))|(?:([Ss](\d{1,2})))", RegexOptions.IgnoreCase);

            var dateMatch = regexDate.Match(fileName);
            //The file name has a date in it
            if (dateMatch.Success)
            {
                //Some tv episodes have a date in it, test the file name for known tv episode naming.
                return testTvShow.Matches(fileName).Count < 1 ? FileOrganizerType.Movie : FileOrganizerType.Episode;
            }

            //The file name didn't have a date in it. It also doesn't have any episode indicators. Treat it as a movie.
            if (testTvShow.Matches(fileName).Count < 1)
            {
                return FileOrganizerType.Movie;
            }

            return testTvShow.Matches(fileName).Count >= 1 ? FileOrganizerType.Episode : FileOrganizerType.Unknown;
        } 

        public QueryResult<SmartMatchResult> GetSmartMatchInfos()
        {
            return _repo.GetSmartMatch(new FileOrganizationResultQuery());
        }

        public void DeleteSmartMatchEntry(string id, string matchString)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (string.IsNullOrEmpty(matchString))
            {
                throw new ArgumentNullException(nameof(matchString));
            }

            _repo.DeleteSmartMatch(id, matchString);
        }

        /// <summary>
        /// Attempts to add a an item to the list of currently processed items.
        /// </summary>
        /// <param name="result">The result item.</param>
        /// <param name="isNewItem">Passing true will notify the client to reload all items, otherwise only a single item will be refreshed.</param>
        /// <returns>True if the item was added, False if the item is already contained in the list.</returns>
        public bool AddToInProgressList(FileOrganizationResult result, bool isNewItem)
        {
            if (string.IsNullOrWhiteSpace(result.Id))
            {
                result.Id = result.OriginalPath.GetMD5().ToString("N");
            }

            if (!_inProgressItemIds.TryAdd(result.Id, false))
            {
                return false;
            }

            result.IsInProgress = true;
            
            if (isNewItem)
            {
                EventHelper.FireEventIfNotNull(ItemAdded, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
            }
            else
            {
                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);
            }

            return true;
        }

        /// <summary>
        /// Removes an item from the list of currently processed items.
        /// </summary>
        /// <param name="result">The result item.</param>
        /// <returns>True if the item was removed, False if the item was not contained in the list.</returns>
        public bool RemoveFromInprogressList(FileOrganizationResult result)
        {
            try
            {
                bool itemValue;
                var retval = _inProgressItemIds.TryRemove(result.Id, out itemValue);

                result.IsInProgress = false;

                EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(result), _logger);

                return retval;
            }
            catch
            {
                return false;
            }
        }

    }
}
