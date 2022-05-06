using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Naming.Common;
using Emby.Naming.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace Emby.AutoOrganize.Core.FileOrganization
{

    public class SubtitleOrganizer : BaseFileOrganizer<SubtitleFileOrganizationRequest>
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

        public SubtitleOrganizer(IFileOrganizationService organizationService, IFileSystem fileSystem, ILogger log, ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, IProviderManager providerManager) 
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

        public async Task<FileOrganizationResult> OrganizeFile(bool requestToMoveFile, string path, AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            //Database results
            var dbResults = OrganizationService.GetResults(new FileOrganizationResultQuery());
            
            var subtitleOrganizerType = GetSubtitleOrganizerType(Path.GetFileNameWithoutExtension(path));

            

            FileOrganizationResult companion = null;

            var result = new FileOrganizationResult();

            switch (subtitleOrganizerType)
            {
                case FileOrganizerType.Unknown:
                    Log.Warn("Unable to determine subtitle target path from file name");
                    return result;

                case FileOrganizerType.Movie:
                    var movieSubtitleInfo = LibraryManager.ParseName(path.AsSpan());
                    companion = dbResults.Items.FirstOrDefault(item => NormalizeString(item.ExtractedName) == NormalizeString(movieSubtitleInfo.Name));
                    break;

                case FileOrganizerType.Episode:

                    var namingOptions = GetNamingOptionsInternal();
                    var resolver = new EpisodeResolver(namingOptions);

                    var episodeSubtitleInfo = resolver.Resolve(path, false) ?? new Naming.TV.EpisodeInfo();
                    
                    companion = dbResults.Items.FirstOrDefault(item =>
                        NormalizeString(item.ExtractedName) == NormalizeString(episodeSubtitleInfo.SeriesName) &&
                        item.ExtractedEpisodeNumber == episodeSubtitleInfo.EpisodeNumber && item.ExtractedSeasonNumber == episodeSubtitleInfo.SeasonNumber);

                    Log.Info($"Media file companion found for: {path}\n {episodeSubtitleInfo.SeriesName} : S{episodeSubtitleInfo.SeasonNumber} E{episodeSubtitleInfo.EpisodeNumber} ");

                    break;
            }
            
           
            
            //Check the companion was found, if not result an empty result - no sorting today!
            if (companion == null) return result;
         
            //Only act upon sorting the subtitle if the companion media file has been successfully moved into the library 
            if (companion.Status != FileSortingStatus.Success) return result;
            
            //The extension of the subtitle file
            var extension = Path.GetExtension(path);
             
            //This is the directory we sent the companion media file too. This is where we want to place the subtitle file.
            var targetDirectory = Path.GetDirectoryName(companion.TargetPath);
            
            //Yeah we have to check that we have a targetDirectory...
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                //The full path we are going to send the subtitle file to, including the name, and it's extension.
                result.TargetPath = Path.Combine(targetDirectory, Path.GetFileNameWithoutExtension(companion.TargetPath) + extension);
            }

            return result;
        }

        public void PerformFileSorting(AutoOrganizeOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {
           
        }



        private FileOrganizerType GetSubtitleOrganizerType(string fileName)
        {
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

        public void OrganizeWithCorrection(SubtitleFileOrganizationRequest request, AutoOrganizeOptions options,
            CancellationToken cancellationToken)
        {
            //Doesn't Exist. If we have a subtitle file we are just going to copy it over
        }

        private string NormalizeString(string value)
        {
            return Regex.Replace(value, @"(\s+|@|&|'|:|\(|\)|<|>|#|\.)", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
        }
    }
}
