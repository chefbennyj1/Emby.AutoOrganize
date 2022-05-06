using System;
using System.Threading;
using Emby.AutoOrganize.Core;
using Emby.AutoOrganize.Core.FileOrganization;
using Emby.AutoOrganize.Data;
using Emby.AutoOrganize.Model;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;

namespace Emby.AutoOrganize
{
    public class PluginEntryPoint : IServerEntryPoint
    {
        public static PluginEntryPoint Instance;

        public IFileOrganizationService FileOrganizationService  { get; private set; }
        private ISessionManager SessionManager                   { get; set; }
        private ITaskManager TaskManager                         { get; set; }
        private ILogger Logger                                   { get; set; }
        private ILibraryMonitor LibraryMonitor                   { get; set; }
        private ILibraryManager LibraryManager                   { get; set; }
        private IServerConfigurationManager ConfigurationManager { get; set; }
        private IFileSystem FileSystem                           { get; set; }
        private IProviderManager ProviderManager                 { get; set; }
        private IJsonSerializer JsonSerializer                   { get; set; } 

        public IFileOrganizationRepository Repository;

        public PluginEntryPoint(ISessionManager sessionManager, ITaskManager taskManager, ILogger logger, ILibraryMonitor libraryMonitor, ILibraryManager libraryManager, IServerConfigurationManager configurationManager, IFileSystem fileSystem, IProviderManager providerManager, IJsonSerializer jsonSerializer)
        {
            SessionManager  = sessionManager;
            TaskManager     = taskManager;
            Logger          = logger;
            LibraryMonitor  = libraryMonitor;
            LibraryManager  = libraryManager;
            ConfigurationManager = configurationManager;
            FileSystem      = fileSystem;
            ProviderManager = providerManager;
            JsonSerializer = jsonSerializer;
            
        }

        public void Run()
        {
            try
            {
                Repository = GetRepository();
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error initializing auto-organize database", ex);
            }

            Instance = this;
            FileOrganizationService = new InternalFileOrganizationService(TaskManager, Repository, Logger, LibraryMonitor, LibraryManager, ConfigurationManager, FileSystem, ProviderManager);

            FileOrganizationService.ItemAdded     += _organizationService_ItemAdded;
           
            MovieOrganizer.ItemUpdated            += _organizationService_ItemUpdated;
            EpisodeOrganizer.ItemUpdated          += _organizationService_ItemUpdated;
            SubtitleOrganizer.ItemUpdated         += _organizationService_ItemUpdated;
            FileOrganizationService.ItemRemoved   += _organizationService_ItemRemoved;
            FileOrganizationService.ItemUpdated   += _organizationService_ItemUpdated;
            FileOrganizationService.LogReset      += _organizationService_LogReset;
            TaskManager.TaskExecuting             += _taskManager_TaskExecuting;
            TaskManager.TaskCompleted             += _taskManager_TaskCompleted;

            
            
            // Convert Config
            ConfigurationManager.Convert(FileOrganizationService);


        }

        
        private void _taskManager_TaskCompleted(object sender, TaskCompletionEventArgs e)
        {
            SessionManager.SendMessageToAdminSessions("TaskComplete",  e.Task.Name, CancellationToken.None);
           
        }

        private void _taskManager_TaskExecuting(object sender, GenericEventArgs<IScheduledTaskWorker> e)
        {
            SessionManager.SendMessageToAdminSessions("TaskData", e.Argument, CancellationToken.None);
        }

        private IFileOrganizationRepository GetRepository()
        {
            var repo = new SqliteFileOrganizationRepository(Logger, ConfigurationManager.ApplicationPaths, JsonSerializer);

            repo.Initialize();

            return repo;
        }

        private void _organizationService_LogReset(object sender, EventArgs e)
        {
            SessionManager.SendMessageToAdminSessions("AutoOrganize_LogReset", (FileOrganizationResult)null, CancellationToken.None);
        }

        private void _organizationService_ItemUpdated(object sender, GenericEventArgs<FileOrganizationResult> e)
        {
            SessionManager.SendMessageToAdminSessions("AutoOrganize_ItemUpdated", e.Argument, CancellationToken.None);
        }

        private void _organizationService_ItemRemoved(object sender, GenericEventArgs<FileOrganizationResult> e)
        {
            SessionManager.SendMessageToAdminSessions("AutoOrganize_ItemRemoved", e.Argument, CancellationToken.None);
        }

        private void _organizationService_ItemAdded(object sender, GenericEventArgs<FileOrganizationResult> e)
        {
            SessionManager.SendMessageToAdminSessions("AutoOrganize_ItemAdded", e.Argument, CancellationToken.None);
        }

        public void Dispose()
        {
            FileOrganizationService.ItemAdded   -= _organizationService_ItemAdded;
            FileOrganizationService.ItemRemoved -= _organizationService_ItemRemoved;
            FileOrganizationService.ItemUpdated -= _organizationService_ItemUpdated;
            FileOrganizationService.LogReset    -= _organizationService_LogReset;
            TaskManager.TaskExecuting          -= _taskManager_TaskExecuting;
            TaskManager.TaskCompleted          -= _taskManager_TaskCompleted;
            var repo = Repository as IDisposable;
            if (repo != null)
            {
                repo.Dispose();
            }
        }
    }
}
