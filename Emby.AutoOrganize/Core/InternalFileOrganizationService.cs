using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading;
using Emby.AutoOrganize.Configuration;
using Emby.AutoOrganize.Core.FileOrganization;
using Emby.AutoOrganize.Core.ScheduledTasks;
using Emby.AutoOrganize.Data;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Model.Organization;
using Emby.AutoOrganize.Model.SmartMatch;
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
            _taskManager.CancelIfRunningAndQueue<FileOrganizerScheduledTask>();
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

        public async void PerformOrganization(string resultId, bool requestToMoveFile)
        {
            var result = _repo.GetResult(resultId);

            var options = GetAutoOrganizeOptions();
            
         
            if (string.IsNullOrEmpty(result.TargetPath))
            {
                _logger.Warn("No target path available.");
                return;
            }

            FileOrganizationResult organizeResult = null;
                //IFileOrganizer organizer;

            switch (result.Type)
            {
                case FileOrganizerType.Episode:
                    var episodeOrganizer = new EpisodeOrganizer(this, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);

                    organizeResult = await episodeOrganizer.OrganizeFile(requestToMoveFile, result.OriginalPath, options, CancellationToken.None);

                    break;
                case FileOrganizerType.Movie:
                    var movieOrganizer = new MovieOrganizer(this, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);
                    _logger.Warn($"Beginning movie organizer... requestToMoveFile: {requestToMoveFile} ");
                    organizeResult = await movieOrganizer.OrganizeFile(requestToMoveFile, result.OriginalPath, options, CancellationToken.None);

                    break;
                default:
                    _logger.Warn("No organizer exist for the type " + result.Type);
                    break;
            }

            if (organizeResult != null && organizeResult.Status != FileSortingStatus.Success)
            {
                _logger.Warn($"Error organizing file: { result.OriginalFileName}");

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

        public void PerformOrganization(EpisodeFileOrganizationRequest request)
        {
            if (string.IsNullOrEmpty(request.TargetFolder))
            {
                _logger.Warn("Target folder can not be empty...");
                return;
            }
            var organizer = new EpisodeOrganizer(this, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);

            var options = GetAutoOrganizeOptions();
            _logger.Info($"Beginning file organization with corrections: {request.Name} to {request.TargetFolder}");
            organizer.OrganizeWithCorrection(request, options, CancellationToken.None); //.ConfigureAwait(false);

            //if (result.Status != FileSortingStatus.Success)
            //{
            //    _logger.Warn(result.StatusMessage);
            //}
        }

        public void PerformOrganization(MovieFileOrganizationRequest request)
        {
            var organizer = new MovieOrganizer(this, _fileSystem, _logger, _libraryManager, _libraryMonitor, _providerManager);

            var options = GetAutoOrganizeOptions();
            organizer.OrganizeWithCorrection(request, options, CancellationToken.None);

            //if (result.Status != FileSortingStatus.Success)
            //{
            //   _logger.Warn(result.StatusMessage);
            //}
        }

        public QueryResult<SmartMatchResult> GetSmartMatchInfos(FileOrganizationResultQuery query)
        {
            return _repo.GetSmartMatch(query);
        }

        public QueryResult<SmartMatchResult> GetSmartMatchInfos()
        {
            return _repo.GetSmartMatch(new FileOrganizationResultQuery());
        }

        public ServerConfiguration GetServerConfiguration()
        {
            return _config.Configuration;
        }
        
        public FileOrganizerType GetFileOrganizerType(string fileName)
        {
            if (_libraryManager.IsSubtitleFile(fileName.AsSpan()))
            {
                return FileOrganizerType.Subtitle;
            }

            var regexDate = new Regex(@"\b(19|20|21)\d{2}\b");
            var testTvShow = new Regex(@"(?:([Ss](\d{1,2})[Ee](\d{1,2})))|(?:(\d{1,2}x\d{1,2}))|(?:[Ss](\d{1,2}x[Ee]\d{1,2}))|(?:([Ss](\d{1,2})))", RegexOptions.IgnoreCase);

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
