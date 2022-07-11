using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Emby.AutoOrganize.Configuration;
using Emby.AutoOrganize.Data;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Model.Corrections;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;

namespace Emby.AutoOrganize.Core
{
    public class InternalFileCorrectionService : IFileCorrectionService
    {
        private ILibraryManager LibraryManager  { get; }
        private ILibraryMonitor LibraryMonitor { get; }
        private IFileSystem FileSystem { get;  }
        private IServerConfigurationManager ServerConfigurationManager { get; }

        private readonly CultureInfo _usCulture = new CultureInfo("en-US");
        private ILogger Log { get; }
        private IFileCorrectionService FileCorrectionService { get; }

        private AutoOrganizeOptions GetAutoOrganizeOptions()
        {
            return ServerConfigurationManager.GetAutoOrganizeOptions();
        }

        private readonly IFileOrganizationRepository _repo;

        public InternalFileCorrectionService(ILibraryManager libraryManager,IFileSystem fileSystem, ILibraryMonitor libraryMonitor, IServerConfigurationManager serverConfigurationManager, ILogger log, IFileOrganizationRepository repository)
        {
            LibraryManager = libraryManager;
            FileSystem = fileSystem;
            LibraryMonitor = libraryMonitor;
            
            ServerConfigurationManager = serverConfigurationManager;
            Log = log;
            _repo = repository;
        }

        public FileCorrection GetFilePathCorrection(string id)
        {
            var result = _repo.GetFilePathCorrection(id);
            
            return result;
        }

        public QueryResult<FileCorrection> GetFilePathCorrections(FileCorrectionResultQuery resultQuery)
        {
            var result = _repo.GetFilePathCorrections(resultQuery);
            return result;
        }

        public void DeleteFilePathCorrection(string id)
        {
            _repo.DeleteFilePathCorrection(id);
        }

        public void DeleteAllFilePathCorrections()
        {
            _repo.DeleteAllFilePathCorrections();
        }

        public void SaveResult(FileCorrection result, CancellationToken cancellationToken)
        {
            result.Id = result.CurrentPath.GetMD5().ToString("N");
            _repo.SaveResult(result, cancellationToken);
        }

        public void AuditPathCorrections(CancellationToken cancellationToken, IProgress<double> progress) 
        {

            DeleteAllFilePathCorrections();

            var options = GetAutoOrganizeOptions();

            if (!options.EnableTelevisionOrganization) return; //Empty

            var episodePattern = options.EpisodeNamePattern;

            if (episodePattern.Contains("%en") || episodePattern.Contains("%e.n") || episodePattern.Contains("%e_n"))
            {
                var episodeResults = LibraryManager.GetItemsResult(new InternalItemsQuery()
                {
                    IncludeItemTypes = new[] { nameof(Episode) },
                    Recursive = true,
                    DtoOptions = new DtoOptions(true)

                });
               
                //Log.Info($"Current episode count is: {episodeResults.TotalRecordCount}");
                if (episodeResults.TotalRecordCount <= 0) return;
                var total = 0.0;
                var step = 100.0 / episodeResults.TotalRecordCount;

                var episodes = episodeResults.Items.Cast<Episode>().ToList();

                Log.Info("Correction Task will check: " + episodes.Count() + " items.");

                for (var i = 0; i <= episodes.Count() -1; i++)
                {
                    total += step;
                    progress.Report(total);
                    var episode = episodes[i];

                    if (episode.Path is null)
                    {
                        continue;
                    }

                    string correctEpisodeFileName;
                    try
                    {
                        //Log.Warn($"Name: {episode.Name} IndexNumber: {episode.IndexNumber} IndexNumberEnd: {episode.IndexNumberEnd}");
                        correctEpisodeFileName = GetEpisodeFileName(episode, options).Trim();
                    }
                    catch
                    {
                        continue;
                    }
                    
                    var currentFileName = Path.GetFileName(episode.Path).Trim();

                    if (string.Equals(currentFileName, correctEpisodeFileName))
                    {
                        continue;
                    }

                    var result = new FileCorrection()
                    {
                        CorrectedPath = Path.Combine(Path.GetDirectoryName(episode.Path), correctEpisodeFileName),
                        CurrentPath = Path.Combine(Path.GetDirectoryName(episode.Path), episode.Path),
                        SeriesName = episode.SeriesName
                    };
                    
                    SaveResult(result, cancellationToken);

                   // FileSystem.MoveFile(path, correctedFilePath);

                    //LibraryMonitor.ReportFileSystemChangeComplete(path, true);

                } 
            }

           

        }

        public void CorrectFileName(FileCorrection correction)
        {
            Log.Info($"Correcting: {correction.CurrentPath}");
            LibraryMonitor.ReportFileSystemChangeBeginning(correction.CurrentPath);
            var correctionDirectory = Path.GetDirectoryName(correction.CurrentPath);

            Log.Info($"Beginning file name correction in {correctionDirectory}");
            //Fix NFO, and Video File
            foreach (var file in Directory.GetFiles(correctionDirectory))
            {
                var extension = Path.GetExtension(file);

                if (Path.GetFileNameWithoutExtension(file) == Path.GetFileNameWithoutExtension(correction.CurrentPath))
                {
                    var name = $"{Path.GetFileNameWithoutExtension(correction.CorrectedPath)}{extension}";
                    var path = Path.Combine(correctionDirectory, name);
                    Log.Info($"Correcting: {file} renamed to { path }");

                    try
                    {
                        File.Move(file, Path.Combine(correctionDirectory, name));
                    }
                    catch { }

                    continue;

                }
                if (Path.GetFileNameWithoutExtension(file) == $"{Path.GetFileNameWithoutExtension(correction.CurrentPath)}-thumb")
                {
                    var name = $"{Path.GetFileNameWithoutExtension(correction.CorrectedPath)}-thumb{extension}";
                    var path = Path.Combine(correctionDirectory, name);

                    Log.Info($"Correcting: {file} renamed to { path }");

                    try
                    {
                        File.Move(file, Path.Combine(correctionDirectory, name));
                    }
                    catch { }

                    continue;
                }
                if (LibraryManager.IsSubtitleFile(file.AsSpan()))
                {
                    var parts = Path.GetFileNameWithoutExtension(file).Split('.');
                    var language = parts[parts.Length - 1];
                    var name = $"{Path.GetFileNameWithoutExtension(correction.CorrectedPath) + "." + language + Path.GetExtension(file)}";
                    Log.Info($"Correcting: {file} renamed to {name}");
                    try
                    {
                        File.Move(file, Path.Combine(correctionDirectory, name));
                    }
                    catch { }
                    continue;
                }


            }

            DeleteFilePathCorrection(correction.Id);
            
            LibraryMonitor.ReportFileSystemChangeComplete(correction.CurrentPath, true);
        }

        private string GetEpisodeFileName(Episode episode, AutoOrganizeOptions options)
        {
            var seriesName          = episode.Parent.Parent.Name;
            var seasonNumber        = episode.Parent.IndexNumber.Value;
            var sourceExtension     = Path.GetExtension(episode.Path).Replace(".", string.Empty);
            var episodeNumber       = episode.IndexNumber.Value;
            var endingEpisodeNumber = episode.IndexNumberEnd;
            var episodeTitle        = episode.Name;
            var resolution          = string.Empty;

            try
            {
                resolution = episode.GetMediaStreams().FirstOrDefault(r => r.Type == MediaStreamType.Video)?.DisplayTitle;
                resolution = Regex.Matches(resolution, "[0-9]{3,4}[Pp]")[0].Value;
            }
            catch
            {

            }

            var pattern = endingEpisodeNumber.HasValue ? options.MultiEpisodeNamePattern : options.EpisodeNamePattern;

            var filename = pattern.Replace("%sn", seriesName)
                .Replace("%s.n", seriesName.Replace(" ", "."))
                .Replace("%s_n", seriesName.Replace(" ", "_"))
                .Replace("%s", seasonNumber.ToString(_usCulture))
                .Replace("%0s", seasonNumber.ToString("00", _usCulture))
                .Replace("%00s", seasonNumber.ToString("000", _usCulture))
                .Replace("%ext", sourceExtension)
                .Replace("%en", "%#1")
                .Replace("%res", resolution)
                .Replace("%e.n", "%#2")
                .Replace("%e_n", "%#3")
                .Replace("%fn", Path.GetFileNameWithoutExtension(episode.Path));

            if (endingEpisodeNumber.HasValue)
            {
                //doing this isnt great - other option is to check media provider for every multi episode like we do for sorting ?
                //Main issue is if episode name has a comma
                episodeTitle = episodeTitle.Replace(", ", options.MultiEpisodeNameDeliminator);
                filename = filename.Replace("%ed", endingEpisodeNumber.Value.ToString(_usCulture))
                    .Replace("%0ed", endingEpisodeNumber.Value.ToString("00", _usCulture))
                    .Replace("%00ed", endingEpisodeNumber.Value.ToString("000", _usCulture));
            }

            filename = filename.Replace("%e", episodeNumber.ToString(_usCulture))
                .Replace("%0e", episodeNumber.ToString("00", _usCulture))
                .Replace("%00e", episodeNumber.ToString("000", _usCulture));

            if (filename.Contains("%#"))
            {
                episodeTitle = episodeTitle.Replace("/", ", "); //replace here so multi episode doesnt conflict
                filename = filename.Replace("%#1", episodeTitle)
                    .Replace("%#2", episodeTitle.Replace(" ", "."))
                    .Replace("%#3", episodeTitle.Replace(" ", "_"));
            }

            // Finally, call GetValidFilename again in case user customized the episode expression with any invalid filename characters
            //return Path.Combine(season.Path, FileSystem.GetValidFilename(result).Trim());
            var result = FileSystem.GetValidFilename(filename).Trim();
           
            return result;
        }

    }
}
