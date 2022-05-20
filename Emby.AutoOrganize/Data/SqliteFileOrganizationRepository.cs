using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Emby.AutoOrganize.Model;
using MediaBrowser.Controller;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using SQLitePCL.pretty;

namespace Emby.AutoOrganize.Data
{
    public class SqliteFileOrganizationRepository : BaseSqliteRepository, IFileOrganizationRepository, IDisposable
    {
        private readonly IJsonSerializer _json;

        public SqliteFileOrganizationRepository(ILogger logger, IServerApplicationPaths appPaths, IJsonSerializer json) : base(logger)
        {
            _json = json;
            DbFilePath = Path.Combine(appPaths.DataPath, "fileorganization.db");
        }

        /// <summary>
        /// Opens the connection to the database
        /// </summary>
        /// <returns>Task.</returns>
        public void Initialize()
        {
            using (var connection = CreateConnection())
            {
                RunDefaultInitialization(connection);

                string[] queries = {

                                "create table if not exists FileOrganizerResults (ResultId GUID PRIMARY KEY, OriginalPath TEXT, TargetPath TEXT, FileLength INT, OrganizationDate datetime, Status TEXT, OrganizationType TEXT, StatusMessage TEXT, ExtractedName TEXT, ExtractedYear int null, ExtractedSeasonNumber int null, ExtractedEpisodeNumber int null, ExtractedEndingEpisodeNumber, ExtractedEpisodeName TEXT, ExtractedResolution TEXT null, VideoStreamCodecs TEXT null, AudioStreamCodecs TEXT null, SourceQuality TEXT null, Subtitles TEXT null, ExtractedEdition TEXT null, ExternalSubtitlePaths TEXT null, DuplicatePaths TEXT int null, ExistingInternalId INT)",
                                "create index if not exists idx_FileOrganizerResults on FileOrganizerResults(ResultId)",
                                "create table if not exists SmartMatch (Id GUID PRIMARY KEY, Name TEXT, OrganizerType TEXT, MatchStrings TEXT null)",
                                "create index if not exists idx_SmartMatch on SmartMatch(Id)",
                               };

                connection.RunQueries(queries);
            }
        }

        #region FileOrganizationResult

        public void SaveResult(FileOrganizationResult result, CancellationToken cancellationToken)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (WriteLock.Write())
            {
                using (var connection = CreateConnection())
                {
                    connection.RunInTransaction(db =>
                    {
                        var commandText = "replace into FileOrganizerResults (ResultId, OriginalPath, TargetPath, FileLength, OrganizationDate, Status, OrganizationType, StatusMessage, ExtractedName, ExtractedYear, ExtractedSeasonNumber, ExtractedEpisodeNumber, ExtractedEndingEpisodeNumber, ExtractedEpisodeName, ExtractedResolution, VideoStreamCodecs, AudioStreamCodecs, SourceQuality, Subtitles, ExtractedEdition, ExternalSubtitlePaths, DuplicatePaths, ExistingInternalId) values (@ResultId, @OriginalPath, @TargetPath, @FileLength, @OrganizationDate, @Status, @OrganizationType, @StatusMessage, @ExtractedName, @ExtractedYear, @ExtractedSeasonNumber, @ExtractedEpisodeNumber, @ExtractedEndingEpisodeNumber, @ExtractedEpisodeName, @ExtractedResolution, @VideoStreamCodecs, @AudioStreamCodecs, @SourceQuality, @Subtitles, @ExtractedEdition, @ExternalSubtitlePaths, @DuplicatePaths, @ExistingInternalId)";

                        using (var statement = db.PrepareStatement(commandText))
                        {
                            statement.TryBind("@ResultId", result.Id.ToGuidBlob());
                            statement.TryBind("@OriginalPath", result.OriginalPath);
                            statement.TryBind("@TargetPath", result.TargetPath);
                            statement.TryBind("@FileLength", result.FileSize);
                            statement.TryBind("@OrganizationDate", result.Date.ToDateTimeParamValue());
                            statement.TryBind("@Status", result.Status.ToString());
                            statement.TryBind("@OrganizationType", result.Type.ToString());
                            statement.TryBind("@StatusMessage", result.StatusMessage);
                            statement.TryBind("@ExtractedName", result.ExtractedName);
                            statement.TryBind("@ExtractedYear", result.ExtractedYear);
                            statement.TryBind("@ExtractedSeasonNumber", result.ExtractedSeasonNumber);
                            statement.TryBind("@ExtractedEpisodeNumber", result.ExtractedEpisodeNumber);
                            statement.TryBind("@ExtractedEndingEpisodeNumber", result.ExtractedEndingEpisodeNumber);
                            statement.TryBind("@ExtractedEpisodeName", result.ExtractedEpisodeName);
                            statement.TryBind("@ExtractedResolution", _json.SerializeToString(result.ExtractedResolution));
                            statement.TryBind("@VideoStreamCodecs", string.Join("|", result.VideoStreamCodecs.ToArray()));
                            statement.TryBind("@AudioStreamCodecs", string.Join("|", result.AudioStreamCodecs.ToArray()));
                            statement.TryBind("@SourceQuality", result.SourceQuality);
                            statement.TryBind("@Subtitles",string.Join("|", result.Subtitles.ToArray()));
                            statement.TryBind("@ExtractedEdition", result.ExtractedEdition);
                            statement.TryBind("@ExternalSubtitlePaths", string.Join("|", result.ExternalSubtitlePaths.ToArray()));
                            statement.TryBind("@DuplicatePaths", string.Join("|", result.DuplicatePaths.ToArray()));
                            statement.TryBind("@ExistingInternalId",  result.ExistingInternalId);

                            statement.MoveNext();
                        }
                    }, TransactionMode);
                }
            }
        }

        public void Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException("id");
            }

            using (WriteLock.Write())
            {
                using (var connection = CreateConnection())
                {
                    connection.RunInTransaction(db =>
                    {
                        using (var statement = db.PrepareStatement("delete from FileOrganizerResults where ResultId = @ResultId"))
                        {
                            statement.TryBind("@ResultId", id.ToGuidBlob());
                            statement.MoveNext();
                        }
                    }, TransactionMode);
                }
            }
        }

        public void DeleteAll()
        {
            using (WriteLock.Write())
            {
                using (var connection = CreateConnection())
                {
                    connection.RunInTransaction(db =>
                    {
                        var commandText = "delete from FileOrganizerResults";

                        db.Execute(commandText);
                    }, TransactionMode);
                }
            }
        }

        public void DeleteCompleted()
        {
            using (WriteLock.Write())
            {
                using (var connection = CreateConnection())
                {
                    connection.RunInTransaction(db =>
                    {
                        using (var statement = db.PrepareStatement("delete from FileOrganizerResults where Status = @Status"))
                        {
                            statement.TryBind("@Status", FileSortingStatus.Success.ToString());
                            statement.MoveNext();
                        }
                    }, TransactionMode);
                }
            }
        }

        public QueryResult<FileOrganizationResult> GetResults(FileOrganizationResultQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            using (WriteLock.Read())
            {
                using (var connection = CreateConnection(true))
                {
                    var commandText = "SELECT ResultId, OriginalPath, TargetPath, FileLength, OrganizationDate, Status, OrganizationType, StatusMessage, ExtractedName, ExtractedYear, ExtractedSeasonNumber, ExtractedEpisodeNumber, ExtractedEndingEpisodeNumber, ExtractedEpisodeName, ExtractedResolution, VideoStreamCodecs, AudioStreamCodecs, SourceQuality, Subtitles, ExtractedEdition, ExternalSubtitlePaths, DuplicatePaths, ExistingInternalId from FileOrganizerResults";

                    if (!string.IsNullOrEmpty(query.Type) && query.Type != "All")
                    {
                        commandText += $" WHERE OrganizationType = \"{query.Type}\"";
                    }
                    if (query.StartIndex.HasValue && query.StartIndex.Value > 0)
                    {
                        commandText +=
                            " WHERE ResultId NOT IN (SELECT ResultId FROM FileOrganizerResults ORDER BY " + query.SortBy + " " + query.DataOrderDirection + $" LIMIT {query.StartIndex.Value.ToString(CultureInfo.InvariantCulture)})";
                    }

                    commandText += " ORDER BY " + query.SortBy + " " + query.DataOrderDirection;

                    if (query.Limit.HasValue)
                    {
                        commandText += " LIMIT " + query.Limit.Value.ToString(CultureInfo.InvariantCulture);
                    }

                    var list = new List<FileOrganizationResult>();

                    using (var statement = connection.PrepareStatement(commandText))
                    {
                        foreach (var row in statement.ExecuteQuery())
                        {
                            list.Add(GetResult(row));
                        }
                    }

                    int count;
                    using (var statement = connection.PrepareStatement("select count (ResultId) from FileOrganizerResults"))
                    {
                        count = statement.ExecuteQuery().First().GetInt(0);
                    }

                    return new QueryResult<FileOrganizationResult>()
                    {
                        Items = list.ToArray(),
                        TotalRecordCount = count
                    };
                }
            }
        }
        public FileOrganizationResult GetResult(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException("id");
            }

            using (WriteLock.Read())
            {
                using (var connection = CreateConnection(true))
                {
                    using (var statement = connection.PrepareStatement("select ResultId, OriginalPath, TargetPath, FileLength, OrganizationDate, Status, OrganizationType, StatusMessage, ExtractedName, ExtractedYear, ExtractedSeasonNumber, ExtractedEpisodeNumber, ExtractedEndingEpisodeNumber, ExtractedEpisodeName, ExtractedResolution, VideoStreamCodecs, AudioStreamCodecs, SourceQuality, Subtitles, ExtractedEdition, ExternalSubtitlePaths, DuplicatePaths, ExistingInternalId from FileOrganizerResults where ResultId=@ResultId"))
                    {
                        statement.TryBind("@ResultId", id.ToGuidBlob());

                        foreach (var row in statement.ExecuteQuery())
                        {
                            return GetResult(row);
                        }
                    }

                    return null;
                }
            }
        }
        public FileOrganizationResult GetResult(IResultSet reader)
        {
            var index = 0;

            var result = new FileOrganizationResult
            {
                Id = reader.GetGuid(0).ToString("N")
            };

            index++;
            if (!reader.IsDBNull(index))
            {
                result.OriginalPath = reader.GetString(index);
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.TargetPath = reader.GetString(index);
            }

            index++;
            result.FileSize = reader.GetInt64(index);

            index++;
            result.Date = reader.ReadDateTime(index);

            index++;
            result.Status = (FileSortingStatus)Enum.Parse(typeof(FileSortingStatus), reader.GetString(index), true);

            index++;
            result.Type = (FileOrganizerType)Enum.Parse(typeof(FileOrganizerType), reader.GetString(index), true);
            
            index++;
            if (!reader.IsDBNull(index))
            {
                result.StatusMessage = reader.GetString(index);
            }

            result.OriginalFileName = Path.GetFileName(result.OriginalPath);

           

            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExtractedName = reader.GetString(index);
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExtractedYear = reader.GetInt(index);
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExtractedSeasonNumber = reader.GetInt(index);
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExtractedEpisodeNumber = reader.GetInt(index);
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExtractedEndingEpisodeNumber = reader.GetInt(index);
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExtractedEpisodeName = reader.GetString(index);
            }
            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExtractedResolution = _json.DeserializeFromString<Resolution>(reader.GetString(index));
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.VideoStreamCodecs = reader.GetString(index).Split('|').Where(i => !string.IsNullOrEmpty(i)).ToList();
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.AudioStreamCodecs = reader.GetString(index).Split('|').Where(i => !string.IsNullOrEmpty(i)).ToList();
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.SourceQuality = reader.GetString(index);
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.Subtitles = reader.GetString(index).Split('|').Where(i => !string.IsNullOrEmpty(i)).ToList();
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExtractedEdition = reader.GetString(index);
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExternalSubtitlePaths = reader.GetString(index).Split('|').Where(i => !string.IsNullOrEmpty(i)).ToList();
            }
            index++;
            if (!reader.IsDBNull(index))
            {
                result.DuplicatePaths = reader.GetString(index).Split('|').Where(i => !string.IsNullOrEmpty(i)).ToList();
            }

            index++;
            if (!reader.IsDBNull(index))
            {
                result.ExistingInternalId = reader.GetInt64(index);
            }

            return result;
        }

        #endregion

        #region SmartMatch

        public void SaveResult(SmartMatchResult result, CancellationToken cancellationToken)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (WriteLock.Write())
            {
                using (var connection = CreateConnection())
                {
                    connection.RunInTransaction(db =>
                    {
                        var commandText = "replace into SmartMatch (Id, Name, OrganizerType, MatchStrings) values (@Id, @Name, @OrganizerType, @MatchStrings)";

                        using (var statement = db.PrepareStatement(commandText))
                        {
                            statement.TryBind("@Id", result.Id.ToGuidBlob());
                            statement.TryBind("@Name", result.Name);
                            statement.TryBind("@OrganizerType", result.OrganizerType.ToString());
                            statement.TryBind("@MatchStrings", _json.SerializeToString(result.MatchStrings));

                            statement.MoveNext();
                        }
                    }, TransactionMode);
                }
            }
        }

        public void DeleteSmartMatch(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException("id");
            }

            using (WriteLock.Write())
            {
                using (var connection = CreateConnection())
                {
                    connection.RunInTransaction(db =>
                    {
                        using (var statement = db.PrepareStatement("delete from SmartMatch where Id = @Id"))
                        {
                            statement.TryBind("@Id", id.ToGuidBlob());
                            statement.MoveNext();
                        }
                    }, TransactionMode);
                }
            }
        }

        public void DeleteSmartMatch(string id, string matchString)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException("id");
            }

            var match = GetSmartMatch(id);

            match.MatchStrings.Remove(matchString);

            if (match.MatchStrings.Any())
            {
                SaveResult(match, CancellationToken.None);
            }
            else
            {
                DeleteSmartMatch(id);
            }
        }

        public void DeleteAllSmartMatch()
        {
            using (WriteLock.Write())
            {
                using (var connection = CreateConnection())
                {
                    connection.RunInTransaction(db =>
                    {
                        var commandText = "delete from SmartMatch";

                        db.Execute(commandText);
                    }, TransactionMode);
                }
            }
        }

        private SmartMatchResult GetSmartMatch(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException("id");
            }

            using (WriteLock.Read())
            {
                using (var connection = CreateConnection(true))
                {
                    using (var statement = connection.PrepareStatement("SELECT Id, Name, OrganizerType, MatchStrings from SmartMatch where Id=@Id"))
                    {
                        statement.TryBind("@Id", id.ToGuidBlob());

                        foreach (var row in statement.ExecuteQuery())
                        {
                            return GetResultSmartMatch(row);
                        }
                    }

                    return null;
                }
            }
        }

        public QueryResult<SmartMatchResult> GetSmartMatch(FileOrganizationResultQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            using (WriteLock.Read())
            {
                using (var connection = CreateConnection(true))
                {
                    var commandText = "SELECT Id, Name, OrganizerType, MatchStrings from SmartMatch";

                    if (query.StartIndex.HasValue && query.StartIndex.Value > 0)
                    {
                        commandText += string.Format(" WHERE Id NOT IN (SELECT Id FROM SmartMatch ORDER BY Name desc LIMIT {0})",
                            query.StartIndex.Value.ToString(CultureInfo.InvariantCulture));
                    }

                    commandText += " ORDER BY Name desc";

                    if (query.Limit.HasValue)
                    {
                        commandText += " LIMIT " + query.Limit.Value.ToString(CultureInfo.InvariantCulture);
                    }

                    var list = new List<SmartMatchResult>();

                    using (var statement = connection.PrepareStatement(commandText))
                    {
                        foreach (var row in statement.ExecuteQuery())
                        {
                            list.Add(GetResultSmartMatch(row));
                        }
                    }

                    int count;
                    using (var statement = connection.PrepareStatement("select count (Id) from SmartMatch"))
                    {
                        count = statement.ExecuteQuery().First().GetInt(0);
                    }

                    return new QueryResult<SmartMatchResult>()
                    {
                        Items = list.ToArray(),
                        TotalRecordCount = count
                    };
                }
            }
        }

        private SmartMatchResult GetResultSmartMatch(IResultSet reader)
        {
            var index = 0;

            var result = new SmartMatchResult
            {
                Id = reader.GetGuid(0).ToString("N")
            };

            index++;
            result.Name = reader.GetString(index);
            
            index++;
            result.OrganizerType = (FileOrganizerType)Enum.Parse(typeof(FileOrganizerType), reader.GetString(index), true);

            index++;
            if (!reader.IsDBNull(index))
            {
                result.MatchStrings = _json.DeserializeFromString<List<string>>(reader.GetString(index));
            }

            return result;
        }

        #endregion
    }
}
