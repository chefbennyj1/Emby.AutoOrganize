using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Configuration;
using Emby.AutoOrganize.Core.WatchedFolderOrganization;
using Emby.AutoOrganize.Model;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.AutoOrganize.Core.ScheduledTasks
{
    public class OrganizerScheduledTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _config;
        private readonly IProviderManager _providerManager;

        public OrganizerScheduledTask(ILibraryMonitor libraryMonitor, ILibraryManager libraryManager, ILogger logger, IFileSystem fileSystem, IServerConfigurationManager config, IProviderManager providerManager)
        {
            _libraryMonitor = libraryMonitor;
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
            _config = config;
            _providerManager = providerManager;
        }

        private AutoOrganizeOptions GetAutoOrganizeOptions()
        {
            return _config.GetAutoOrganizeOptions();
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = GetAutoOrganizeOptions();
            var fileOrganizationService = PluginEntryPoint.Instance.FileOrganizationService;

            try
            {
                await new WatchedFolderOrganizer(_libraryManager, _logger, _fileSystem, _libraryMonitor,
                        fileOrganizationService, _config, _providerManager).Organize(options, cancellationToken, progress);//.ConfigureAwait(false);
            }
            catch
            {

            }

            progress.Report(100.0);

        }

        /// <summary>
        /// Creates the triggers that define when the task will run
        /// </summary>
        /// <returns>IEnumerable{BaseTaskTrigger}.</returns>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[] 
            { 
                new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromMinutes(5).Ticks}
            };
        }

        public string Name => "Organize new media files";
        public string Key => "AutoOrganize";
        public string Description => "Processes new files available in the configured watch folder.";
        public string Category => "Auto Organize";
        public bool IsHidden => false;
        public bool IsEnabled => true;
        public bool IsLogged => false;

    }
}
