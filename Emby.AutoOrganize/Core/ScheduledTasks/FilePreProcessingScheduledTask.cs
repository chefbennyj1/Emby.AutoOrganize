using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Configuration;
using Emby.AutoOrganize.Core.PreProcessing;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Naming.Common;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.AutoOrganize.Core.ScheduledTasks
{
    public class FilePreProcessingScheduledTask : IScheduledTask, IConfigurableScheduledTask
    {
        private ILogger Logger                 { get; }
        private IFileSystem FileSystem         { get; }
        private ILogManager LogManager         { get; }
        private ILibraryManager LibraryManager { get; }
        private IServerConfigurationManager ServerConfigurationManager { get; }
        
        private bool IsValidWatchLocation(string path, List<string> libraryFolderPaths)
        {
            if (IsPathAlreadyInMediaLibrary(path, libraryFolderPaths))
            {
                Logger.Info("Folder {0} is not eligible for auto-organize because it is also part of an Emby library", path);
                return false;
            }

            return true;
        }
        
        private bool IsPathAlreadyInMediaLibrary(string path, List<string> libraryFolderPaths)
        {
            return libraryFolderPaths.Any(i => string.Equals(i, path, StringComparison.Ordinal) || FileSystem.ContainsSubPath(i.AsSpan(), path.AsSpan()));
        }
        
        private AutoOrganizeOptions GetAutoOrganizeOptions()
        {
            return ServerConfigurationManager.GetAutoOrganizeOptions();
        }

        public FilePreProcessingScheduledTask(IFileSystem file, ILogManager logManager, IServerConfigurationManager serverConfigurationManager, ILibraryManager libraryManager)
        {
            FileSystem                 = file;
            LogManager                 = logManager;
            Logger                     = LogManager.GetLogger("AutoOrganize");
            ServerConfigurationManager = serverConfigurationManager;
            LibraryManager             = libraryManager;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = GetAutoOrganizeOptions();

            var libraryFolderPaths = LibraryManager.GetVirtualFolders().SelectMany(i => i.Locations).ToList();

            var watchLocations = options.WatchLocations.Where(i => IsValidWatchLocation(i, libraryFolderPaths)).ToList();
            
            //We can't pre process files without these folders being named.
            if (!watchLocations.Any() || string.IsNullOrEmpty(options.PreProcessingFolderPath)) return;

            var preProcessOrganizer = new PreProcessOrganizer(FileSystem, Logger);
            
            preProcessOrganizer.Organize(progress, watchLocations, options, cancellationToken);

        }
        
        private static void CreateExtractionMarker(string folderPath, ILogger logger)
        {
            logger.Info("Creating extraction marker: " + folderPath + "\\####emby.extracted####");
            using (var sw = new StreamWriter(folderPath + "\\####emby.extracted####"))
            {
              sw.Flush();
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                // Every so often
                new TaskTriggerInfo
                {
                    Type          = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromMinutes(5).Ticks
                }
            };
        }

        public string Name        => "Extract new media files";
        public string Description => "Extract new files available in the configured watch folder into Emby's Auto Organize folder.";
        public string Category    => "Library";
        public string Key         => "FileCompressionCopy";
        public bool IsHidden      => !GetAutoOrganizeOptions().EnablePreProcessing;
        public bool IsEnabled     => GetAutoOrganizeOptions().EnablePreProcessing;
        public bool IsLogged      => true;
    }
}