using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Naming;
using Emby.AutoOrganize.Naming.Common;
using Emby.Naming.TV;
using MediaBrowser.Common.Events;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Extensions;
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
            var dbResults             = OrganizationService.GetResults(new FileOrganizationResultQuery() { DataOrderDirection = "DESC" });
            var subtitleOrganizerType = GetSubtitleOrganizerType(Path.GetFileNameWithoutExtension(path));
            var targetDirectory       = string.Empty;
            var fileName              = string.Empty;
            var extension             = Path.GetExtension(path);
            var language              = RegexExtensions.ParseSubtitleLanguage(path).TrimEnd('.');

            FileOrganizationResult companion = null;

            var result = new FileOrganizationResult { OriginalPath = path };

            switch (subtitleOrganizerType)
            {
                case FileOrganizerType.Unknown:
                    Log.Warn("Unable to determine subtitle target path from file name");
                    return result;

                case FileOrganizerType.Movie:
                    var movieSubtitleInfo = LibraryManager.ParseName(path.AsSpan());

                    if (movieSubtitleInfo != null)
                    {
                        companion = dbResults.Items.FirstOrDefault(item => RegexExtensions.NormalizeSearchStringComparison(item.ExtractedName).ContainsIgnoreCase(RegexExtensions.NormalizeSearchStringComparison(movieSubtitleInfo.Name)));

                        //The result is no longer in the database table, try to find it in the library
                        if (companion is null)
                        {
                            var movie = GetMatchingMovie(movieSubtitleInfo.Name, movieSubtitleInfo.Year);
                            if (movie is null)
                            {
                                Log.Warn($"Unable to find matching movie for {path}");
                                return result;
                            }

                            targetDirectory = Path.GetDirectoryName(movie.Path);
                            fileName = Path.GetFileNameWithoutExtension(movie.Path);
                        }
                        else
                        {
                            targetDirectory = Path.GetDirectoryName(companion.TargetPath);
                            fileName = Path.GetFileNameWithoutExtension(companion.TargetPath);
                        }
                    }
                    else
                    {
                        Log.Warn($"Can not sort subtitle file: {path}");
                        return result;
                    }

                   

                    break;

                case FileOrganizerType.Episode:

                    var namingOptions = GetNamingOptionsInternal();
                    var resolver = new EpisodeResolver(namingOptions);

                    var episodeSubtitleInfo = resolver.Resolve(path, false) ?? new Naming.TV.EpisodeInfo();
                    
                    
                    companion = dbResults.Items.FirstOrDefault(item =>
                        RegexExtensions.NormalizeSearchStringComparison(item.ExtractedName).ContainsIgnoreCase(RegexExtensions.NormalizeSearchStringComparison(episodeSubtitleInfo.SeriesName)) &&
                        item.ExtractedEpisodeNumber == episodeSubtitleInfo.EpisodeNumber && item.ExtractedSeasonNumber == episodeSubtitleInfo.SeasonNumber);

                    //The result is no longer in the database table, try to find it in the library
                    if (companion is null)
                    {
                        if (episodeSubtitleInfo.EpisodeNumber.HasValue && episodeSubtitleInfo.SeasonNumber.HasValue)
                        {
                            var episode = GetMatchingEpisode(episodeSubtitleInfo.SeriesName, episodeSubtitleInfo.EpisodeNumber.Value, episodeSubtitleInfo.SeasonNumber.Value);

                            if (episode is null)
                            {
                                Log.Warn($"Unable to find matching episode for {path}");
                                return result;
                            }

                            targetDirectory = Path.GetDirectoryName(episode.Path);
                            fileName = Path.GetFileNameWithoutExtension(episode.Path);

                        } else
                        {
                            //returning empty result. No sorting today!
                            Log.Warn($"Unable to sort subtitle file {path}");
                            return result;
                        }
                        
                    }
                    else
                    {
                        
                        targetDirectory = Path.GetDirectoryName(companion.TargetPath);
                        fileName = Path.GetFileNameWithoutExtension(companion.TargetPath);
                    }

                    if (!string.IsNullOrEmpty(targetDirectory) && !string.IsNullOrEmpty(fileName))
                    {
                        
                        //The full path we are going to send the subtitle file to, including the name, and it's extension.
                        result.TargetPath = Path.Combine(targetDirectory, (fileName + language + extension));
                        Log.Info($"Subtitle result Target path is: { result.TargetPath }");
                        //Add the path to the companion if it exists. It's so we can show it in the UI
                        if (companion != null)
                        {
                            companion.ExternalSubtitlePaths.Add(result.TargetPath);
                            OrganizationService.SaveResult(companion, cancellationToken);
                            EventHelper.FireEventIfNotNull(ItemUpdated, this, new GenericEventArgs<FileOrganizationResult>(companion), Log); //Update the UI
                        }
                    }
                    
                    break;
            }
            
            PerformFileSorting(options, result, cancellationToken);

            return result;
        }

        private void PerformFileSorting(AutoOrganizeOptions options, FileOrganizationResult result, CancellationToken cancellationToken)
        {
            // We should probably handle this earlier so that we never even make it this far
            if (string.Equals(result.OriginalPath, result.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            LibraryMonitor.ReportFileSystemChangeBeginning(result.TargetPath);
            try
            {
                Log.Info($"Copying Subtitle File: {result.TargetPath}");
                FileSystem.CopyFile(result.OriginalPath, result.TargetPath, true);
            }
            catch (Exception ex)
            {
                Log.Warn(ex.Message);
            }

            LibraryMonitor.ReportFileSystemChangeComplete(result.TargetPath, true);

            try
            {
                FileSystem.DeleteFile(result.OriginalPath);
            }
            catch (Exception ex)
            {
                Log.Warn("Error deleting {0}", ex, result.OriginalPath);
            }
        }


        private BaseItem GetMatchingMovie(string movieName, int? year)
        {
            var movieQuery = LibraryManager.GetItemList(new InternalItemsQuery()
            {
                IncludeItemTypes = new[] { nameof(Movie)},
                Recursive = true,
                SearchTerm = movieName,
                DtoOptions = new DtoOptions(true),
                Years = year.HasValue ? new[] { year.Value } : Array.Empty<int>(),
            });

            var movie = movieQuery.FirstOrDefault(e => e.ProductionYear == year);

            return movie;
        }

        private BaseItem GetMatchingEpisode(string seriesName, int episodeIndex, int seasonIndex)
        {
            var seriesQuery = LibraryManager.GetItemList(new InternalItemsQuery()
            {
                IncludeItemTypes = new[] { nameof(Series)},
                Recursive = true,
                SearchTerm = seriesName
            });

            var series = seriesQuery.FirstOrDefault();

            if (series is null) return null;

            var episodeQuery = LibraryManager.GetItemList(new InternalItemsQuery()
            {
                AncestorIds = new[] { series.InternalId },
                Recursive = true
            });

            var episode = episodeQuery.FirstOrDefault(e => e.IndexNumber == episodeIndex && e.Parent.IndexNumber == seasonIndex);

            return episode;
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

    }
}
