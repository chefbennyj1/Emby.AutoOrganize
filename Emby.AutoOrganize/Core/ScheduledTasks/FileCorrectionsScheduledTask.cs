//using System;
//using System.Collections.Generic;
//using System.Threading;
//using System.Threading.Tasks;
//using Emby.AutoOrganize.Configuration;
//using Emby.AutoOrganize.Model;
//using MediaBrowser.Controller.Configuration;
//using MediaBrowser.Controller.Library;
//using MediaBrowser.Model.IO;
//using MediaBrowser.Model.Logging;
//using MediaBrowser.Model.Tasks;

//namespace Emby.AutoOrganize.Core.ScheduledTasks
//{
//    public class FileCorrectionsScheduledTask : IScheduledTask, IConfigurableScheduledTask
//    {
//        private readonly ILibraryMonitor _libraryMonitor;
//        private readonly ILibraryManager _libraryManager;
//        private readonly ILogger _logger;
//        private readonly IFileSystem _fileSystem;
//        private readonly IServerConfigurationManager _config;
       
//        public FileCorrectionsScheduledTask(ILibraryMonitor libraryMonitor, ILibraryManager libraryManager, ILogger logger, IFileSystem fileSystem, IServerConfigurationManager config)
//        {
//            _libraryMonitor = libraryMonitor;
//            _libraryManager = libraryManager;
//            _logger = logger;
//            _fileSystem = fileSystem;
//            _config = config;
           
           
//        }

//        public AutoOrganizeOptions GetAutoOrganizeOptions()
//        {
//            return _config.GetAutoOrganizeOptions();
//        }

//        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
//        {

//            var fileCorrectionService = PluginEntryPoint.Instance.FileCorrectionService;
//            try
//            {
                
//                fileCorrectionService.AuditPathCorrections(cancellationToken, progress);
//                //progress.Report(100.0);
//            }
//            catch (Exception ex)
//            {
//                _logger.ErrorException(ex.Message, ex);
//            }
//        }

//        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
//        {
//            return new[] 
//            {
//                new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerDaily, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks }
//            };
//        }

//        public string Name => "File Name Corrections";
//        public string Key => "AutoOrganizeFileNameCorrections";
//        public string Description => "Locate files in the file system which names don't match the user defined pattern";
//        public string Category => "Library";
//        public bool IsHidden => !GetAutoOrganizeOptions().EnableFileNameCorrections;
//        public bool IsEnabled => GetAutoOrganizeOptions().EnableFileNameCorrections;
//        public bool IsLogged => true;
//    }
//}
