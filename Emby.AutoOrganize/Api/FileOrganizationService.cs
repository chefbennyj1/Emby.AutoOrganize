using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Emby.AutoOrganize.Configuration;
using Emby.AutoOrganize.Core;
using Emby.AutoOrganize.Model;
using Emby.AutoOrganize.Model.Corrections;
using Emby.AutoOrganize.Model.Organization;
using Emby.AutoOrganize.Model.SmartMatch;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.AutoOrganize.Api
{
    [Route("/AutoOrganize/CurrentDefaultTvDriveSize", "GET", Summary = "AutoOrganize Default TV Drive Size")]
    public class GetCurrentDefaultTvDriveSize : IReturn<long>
    {
        public long Size { get; set; }
    }
    
    [Route("/AutoOrganize/CurrentDefaultMovieDriveSize", "GET", Summary = "AutoOrganize Default TV Drive Size")]
    public class GetCurrentDefaultMovieDriveSize : IReturn<long>
    {
        public long Size { get; set; }
    }
    
    [Route("/Library/FileOrganization", "GET", Summary = "Gets file organization results")]
    public class GetFileOrganizationActivity : IReturn<QueryResult<FileOrganizationResult>>
    {
        /// <summary>
        /// Skips over a given number of items within the results. Use for paging.
        /// </summary>
        /// <value>The start index.</value>
        [ApiMember(Name = "StartIndex", Description = "Optional. The record index to start at. All items with a lower index will be dropped from the results.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
        public int? StartIndex { get; set; }

        /// <summary>
        /// The maximum number of items to return
        /// </summary>
        /// <value>The limit.</value>
        [ApiMember(Name = "Limit", Description = "Optional. The maximum number of records to return", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
        public int? Limit { get; set; }

        [ApiMember(Name = "NameStartsWith", Description = "Optional. Name Starts With", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string NameStartsWith { get; set; }

        [ApiMember(Name = "Type", Description = "Optional. The type of media to put in the response", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string Type { get; set; }

        [ApiMember(Name = "Ascending", Description = "Optional. Direction of the data", IsRequired = true, DataType = "bool", ParameterType = "query", Verb = "GET")]
        public bool Ascending { get; set; }

        [ApiMember(Name = "SortBy", Description = "Optional. Sorting of the data", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string SortBy { get; set; }
    }

    [Route("/Library/FileOrganizations", "DELETE", Summary = "Clears the activity log")]
    public class ClearOrganizationLog : IReturnVoid
    {
    }

    [Route("/Library/FileOrganizations/Completed", "DELETE", Summary = "Clears the activity log")]
    public class ClearOrganizationCompletedLog : IReturnVoid
    {
    }

    [Route("/Library/FileOrganizations/{Id}/File", "DELETE", Summary = "Deletes the original file of a organizer result")]
    public class DeleteOriginalFile : IReturnVoid
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Result Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "DELETE")]
        public string Id { get; set; }
    }

    [Route("/Library/FileOrganizations/{Id}/Organize", "POST", Summary = "Performs an organization")]
    public class PerformOrganization : IReturn<QueryResult<FileOrganizationResult>>
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Result Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string Id { get; set; }

        [ApiMember(Name = "RequestToMoveFile", Description = "Optional. Should overwrite the existing File, if it exists. If empty the file will not overwrite.", IsRequired = true, DataType = "bool", ParameterType = "query", Verb = "POST")]
        public bool RequestToMoveFile { get; set; }
        
    }

    [Route("/Library/FileOrganizations/{Id}/Episode/Organize", "POST", Summary = "Performs organization of a tv episode")]
    public class OrganizeEpisode
    {
        [ApiMember(Name = "Id", Description = "Result Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string Id { get; set; }

        [ApiMember(Name = "SeriesId", Description = "Series Id", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string SeriesId { get; set; }

        [ApiMember(Name = "SeasonNumber", IsRequired = true, DataType = "int", ParameterType = "query", Verb = "POST")]
        public int SeasonNumber { get; set; }

        [ApiMember(Name = "EpisodeNumber", IsRequired = true, DataType = "int", ParameterType = "query", Verb = "POST")]
        public int EpisodeNumber { get; set; }

        [ApiMember(Name = "EndingEpisodeNumber", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "POST")]
        public int? EndingEpisodeNumber { get; set; }

        [ApiMember(Name = "RememberCorrection", Description = "Whether or not to apply the same correction to future episodes of the same series.", IsRequired = false, DataType = "bool", ParameterType = "query", Verb = "POST")]
        public bool RememberCorrection { get; set; }

        [ApiMember(Name = "NewSeriesProviderIds", Description = "A list of provider IDs identifying a new series.", IsRequired = false, DataType = "Dictionary<string, string>", ParameterType = "query", Verb = "POST")]
        public ProviderIdDictionary SeriesProviderIds { get; set; }

        [ApiMember(Name = "Name", Description = "Name of a series to add.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string Name { get; set; }

        [ApiMember(Name = "Year", Description = "Year of a series to add.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
        public int? Year { get; set; }

        [ApiMember(Name = "TargetFolder", Description = "Target Folder", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string TargetFolder { get; set; }

        [ApiMember(Name = "RequestToMoveFile", Description = "Force sorting of the file", IsRequired = true, DataType = "bool", ParameterType = "query", Verb = "POST")]
        public bool RequestToMoveFile { get; set; }

        [ApiMember(Name = "CreateNewDestination", Description = "Create New Destination Folder", IsRequired = false, DataType = "bool", ParameterType = "query", Verb = "POST")]
        public bool CreateNewDestination { get; set; }


    }

    [Route("/Library/FileOrganizations/{Id}/Movie/Organize", "POST", Summary = "Performs organization of a movie")]
    public class OrganizeMovie
    {
        [ApiMember(Name = "Id", Description = "Result Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string Id { get; set; }

        [ApiMember(Name = "MovieId", Description = "Movie Id", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string MovieId { get; set; }

        [ApiMember(Name = "NewMovieProviderIds", Description = "A list of provider IDs identifying a new movie.", IsRequired = false, DataType = "Dictionary<string, string>", ParameterType = "query", Verb = "POST")]
        public ProviderIdDictionary NewMovieProviderIds { get; set; }

        [ApiMember(Name = "Name", Description = "Name of a movie to add.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string Name { get; set; }

        [ApiMember(Name = "Year", Description = "Year of a movie to add.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
        public int? Year { get; set; }

        [ApiMember(Name = "TargetFolder", Description = "Target Folder", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string TargetFolder { get; set; }
         
        [ApiMember(Name = "RequestToMoveFile", Description = "Overwrite Existing File", IsRequired = true, DataType = "bool", ParameterType = "query", Verb = "POST")]
        public bool RequestToMoveFile { get; set; }
    }

    [Route("/Library/FileOrganizations/SmartMatches", "GET", Summary = "Gets smart match entries")]
    public class GetSmartMatchInfos : IReturn<SmartMatchResult>
    {
        /// <summary>
        /// Skips over a given number of items within the results. Use for paging.
        /// </summary>
        /// <value>The start index.</value>
        [ApiMember(Name = "StartIndex", Description = "Optional. The record index to start at. All items with a lower index will be dropped from the results.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
        public int? StartIndex { get; set; }

        /// <summary>
        /// The maximum number of items to return
        /// </summary>
        /// <value>The limit.</value>
        [ApiMember(Name = "Limit", Description = "Optional. The maximum number of records to return", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
        public int? Limit { get; set; }
    }

    [Route("/Library/FileOrganizations/SmartMatches/Delete", "POST", Summary = "Deletes a smart match entry")]
    public class DeleteSmartMatchEntry
    {
        [ApiMember(Name = "Entry", Description = "SmartMatch Entry", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string Id  { get; set; }
        public string MatchString { get; set; }
    }

    [Route("/Library/FileOrganizations/SmartMatches/Save", "POST", Summary = "Save a custom smart match entry")]
    public class SaveCustomSmartMatch
    {
        [ApiMember(Name = "TargetPath", Description = "SmartMatch TargetFolder", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string TargetFolder { get; set; }

        [ApiMember(Name = "Entries", Description = "SmartMatch Entry", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public List<string> Matches { get; set; }

        [ApiMember(Name = "Type", Description = "Organization Type", IsRequired = true, DataType = "FileOrganizerType", ParameterType = "query", Verb = "POST")]
        public FileOrganizerType Type { get; set; }

        [ApiMember(Name = "Id", Description = "SmartMatch Id", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string Id { get; set; }

    }

    [Route("/Library/FileOrganizations/FileNameCorrections", "GET", Summary = "Gets a list of file name corrections")]
    public class FileNameCorrectionRequest
    {
        [ApiMember(Name = "StartsWith", Description = "Item names start with", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string StartsWith { get; set; }
    }

    [Route("/Library/FileOrganizations/FileNameCorrections/Update", "POST", Summary = "Update/Change files name")]
    public class UpdateFileNameCorrectionRequest
    {
        [ApiMember(Name = "Ids", Description = "File Name Correction Entry Ids", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public List<string> Ids { get; set; }
    }

    [Authenticated(Roles = "Admin")]
    public class FileOrganizationService : IService, IRequiresRequest
    {
        private readonly IHttpResultFactory _resultFactory;

        public IRequest Request { get; set; }
        //private ILibraryManager LibraryManager { get; set; }
        //private ILibraryMonitor LibraryMonitor { get; }
        private IFileSystem FileSystem { get;  }
        //private IJsonSerializer JsonSerializer { get; }
        private ILogger Log { get; set; }
        private IServerConfigurationManager ServerConfiguration { get; set; }
        public FileOrganizationService(IHttpResultFactory resultFactory, ILibraryManager libraryManager, IFileSystem fileSystem, ILibraryMonitor libraryMonitor, IServerConfigurationManager config, IJsonSerializer json, ILogManager log)
        {
            _resultFactory = resultFactory;
            //LibraryManager = libraryManager;
            //LibraryMonitor = libraryMonitor;
            FileSystem = fileSystem;
            ServerConfiguration = config;
            Log = log.GetLogger("AutoOrganize");
            //JsonSerializer = json;
        }

        private IFileOrganizationService InternalFileOrganizationService => PluginEntryPoint.Instance.FileOrganizationService;
        private IFileCorrectionService InternalFileCorrectionService => PluginEntryPoint.Instance.FileCorrectionService;

        public object Get(GetFileOrganizationActivity request)
        {
            var result = InternalFileOrganizationService.GetResults(new FileOrganizationResultQuery
            {
                Limit = request.Limit,
                StartIndex = request.StartIndex,
                Type = !string.IsNullOrEmpty(request.Type) ? request.Type : "All",
                DataOrderDirection = request.Ascending ? "ASC" : "DESC",
                SortBy = !string.IsNullOrEmpty(request.SortBy) ? request.SortBy : "OrganizationDate"
            });

            
            if (!string.IsNullOrEmpty(request.NameStartsWith))
            {
                var normalizedSearchTerm = request.NameStartsWith.Replace("%20", " ");

                result.Items = result.Items.Where(item => item.OriginalFileName.ToUpperInvariant().Replace(".", " ").Contains(normalizedSearchTerm.ToUpperInvariant())).ToArray();
            }


            return _resultFactory.GetResult(Request, result);
        }

        public void Delete(DeleteOriginalFile request)
        {
            InternalFileOrganizationService.DeleteOriginalFile(request.Id);

        }

        public void Delete(ClearOrganizationLog request)
        {
            InternalFileOrganizationService.ClearLog();

        }
        
        public void Delete(ClearOrganizationCompletedLog request)
        {
            InternalFileOrganizationService.ClearCompleted();

        }
        
        public void Post(PerformOrganization request)
        {
            // Don't await this
            InternalFileOrganizationService.PerformOrganization(request.Id, request.RequestToMoveFile);

            // Async processing (close dialog early instead of waiting until the file has been copied)
            // Wait 2s for exceptions that may occur to have them forwarded to the client for immediate error display
            //task.Wait(2000);
        }

        public void Post(OrganizeEpisode request)
        {
            var dicNewProviderIds = new ProviderIdDictionary();

            if (request.SeriesProviderIds != null)
            {
                dicNewProviderIds = request.SeriesProviderIds;
            }

            // Don't await this
            InternalFileOrganizationService.PerformOrganization(new EpisodeFileOrganizationRequest
            {
                EndingEpisodeNumber             = request.EndingEpisodeNumber,
                EpisodeNumber                   = request.EpisodeNumber,
                RememberCorrection              = request.RememberCorrection,
                ResultId                        = request.Id,
                SeasonNumber                    = request.SeasonNumber,
                SeriesId                        = request.SeriesId ?? string.Empty,
                Name                            = request.Name,
                Year                            = request.Year,
                ProviderIds                     = dicNewProviderIds,
                TargetFolder                    = request.TargetFolder,
                RequestToMoveFile               = request.RequestToMoveFile,
                CreateNewDestination            = request.CreateNewDestination
            });

            // Async processing (close dialog early instead of waiting until the file has been copied)
            // Wait 2s for exceptions that may occur to have them forwarded to the client for immediate error display
            //task.Wait(2000);
        }

        public void Post(OrganizeMovie request)
        {
            var dicNewProviderIds = new ProviderIdDictionary();

            if (request.NewMovieProviderIds != null)
            {
                dicNewProviderIds = request.NewMovieProviderIds;
            }

            // Don't await this
            InternalFileOrganizationService.PerformOrganization(new MovieFileOrganizationRequest
            {
                ResultId                        = request.Id,
                MovieId                         = request.MovieId,
                Name                            = request.Name,
                Year                            = request.Year,
                ProviderIds                     = dicNewProviderIds,
                TargetFolder                    = request.TargetFolder,
                RequestToMoveFile               = request.RequestToMoveFile
            });
        }

        public object Get(GetSmartMatchInfos request)
        {
            var result = InternalFileOrganizationService.GetSmartMatchInfos(new FileOrganizationResultQuery
            {
                Limit = request.Limit,
                StartIndex = request.StartIndex
            });

            return _resultFactory.GetResult(Request, result);
        }

        public void Post(DeleteSmartMatchEntry request)
        {
            InternalFileOrganizationService.DeleteSmartMatchEntry(request.Id, request.MatchString);
        }

        public void Post(SaveCustomSmartMatch request)
        {
            SmartMatchResult result = null;
            var smartInfo = InternalFileOrganizationService.GetSmartMatchInfos();
            if (!string.IsNullOrEmpty(request.TargetFolder))
            {
                result = smartInfo.Items.FirstOrDefault(s => s.TargetFolder == request.TargetFolder);
            }

            if (result is null)
            {
                result = new SmartMatchResult
                {
                    Id = $"custom_smart_match { smartInfo.TotalRecordCount + 1 }".GetMD5().ToString("N"),
                    OrganizerType = request.Type,
                    IsCustomUserDefinedEntry = true,
                    TargetFolder = request.TargetFolder
                };
            }

            //Only add new match items to the smart match result.
            result.MatchStrings.AddRange(request.Matches.Where(r => !result.MatchStrings.Contains(r)));
            

            InternalFileOrganizationService.SaveResult(result, CancellationToken.None);

        }

        public object Get(FileNameCorrectionRequest request)
        {
            var result = InternalFileCorrectionService.GetFilePathCorrections(new FileCorrectionResultQuery
            {
                StartsWith = !string.IsNullOrEmpty(request.StartsWith) ? request.StartsWith : ""
            });

            return _resultFactory.GetResult(Request, result);
        }

        public void Post(UpdateFileNameCorrectionRequest request)
        {
            
            var correctionResult = InternalFileCorrectionService.GetFilePathCorrections(new FileCorrectionResultQuery());
            
            foreach (var id in request.Ids)
            {
                var correction = correctionResult.Items.FirstOrDefault(c => c.Id == id);
                try
                {
                    InternalFileCorrectionService.CorrectFileName(correction);
                }
                catch
                {
                    InternalFileCorrectionService.DeleteFilePathCorrection(correction.Id);
                }
            }
        }

        public long Get(GetCurrentDefaultTvDriveSize request)
        {
            var options = GetAutoOrganizeOptions();
            return string.IsNullOrEmpty(options.DefaultSeriesLibraryPath) ? 0 : DriveInfo.GetDrives().FirstOrDefault(drive => drive.Name == Path.GetPathRoot(options.DefaultSeriesLibraryPath)).AvailableFreeSpace;
        }

        public long Get(GetCurrentDefaultMovieDriveSize request)
        {
            var options = GetAutoOrganizeOptions();            
            return string.IsNullOrEmpty(options.DefaultMovieLibraryPath) ? 0 : DriveInfo.GetDrives().FirstOrDefault(drive => drive.Name == Path.GetPathRoot(options.DefaultMovieLibraryPath)).AvailableFreeSpace;
        }
        
        private AutoOrganizeOptions GetAutoOrganizeOptions()
        {
            return ServerConfiguration.GetAutoOrganizeOptions();
        }
    }
}
