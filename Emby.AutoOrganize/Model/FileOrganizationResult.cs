using System;
using System.Collections.Generic;
using Emby.AutoOrganize.Data;
using Emby.AutoOrganize.FileMetadata;
using Emby.AutoOrganize.Model.Organization;

namespace Emby.AutoOrganize.Model
{
    public class FileOrganizationResult
    {
        /// <summary>
        /// Gets or sets the result identifier.
        /// </summary>
        /// <value>The result identifier.</value>
        public string Id { get; set; }
        
        /// <summary>
        /// Series, or Movie Id
        /// </summary>
        public string ItemId { get; set; }

        /// <summary>
        /// Gets or sets the original path.
        /// </summary>
        /// <value>The original path.</value>
        public string OriginalPath { get; set; }

        /// <summary>
        /// Gets or sets the name of the original file.
        /// </summary>
        /// <value>The name of the original file.</value>
        public string OriginalFileName { get; set; }

        /// <summary>
        /// Gets or sets the name of the extracted.
        /// </summary>
        /// <value>The name of the extracted.</value>
        public string ExtractedName { get; set; }

        /// <summary>
        /// Gets or sets the extracted year.
        /// </summary>
        /// <value>The extracted year.</value>
        public int? ExtractedYear { get; set; }

         /// <summary>
        /// Gets or sets the extracted Resolution.
        /// </summary>
        /// <value>The extracted Resolution.</value>
        public Resolution ExtractedResolution { get; set; }
        
         /// <summary>
        /// Gets or sets the extracted Edition.
        /// </summary>
        public string ExtractedEdition { get; set; } = string.Empty;

        public string ExtractedSeriesName { get; set; }

        /// <summary>
        /// Gets or sets the extracted season number.
        /// </summary>
        /// <value>The extracted season number.</value>
        public int? ExtractedSeasonNumber { get; set; }

        /// <summary>
        /// Gets or sets the extracted episode number.
        /// </summary>
        /// <value>The extracted episode number.</value>
        public int? ExtractedEpisodeNumber { get; set; }

        /// <summary>
        /// Gets or sets the extracted ending episode number.
        /// </summary>
        /// <value>The extracted ending episode number.</value>
        public int? ExtractedEndingEpisodeNumber { get; set; }
        
        public string ExtractedEndingEpisodeName { get; set; }
        public string ExtractedEpisodeName { get; set; }
        /// <summary>
        /// Gets or sets the target path.
        /// </summary>
        /// <value>The target path.</value>
        public string TargetPath { get; set; }

        /// <summary>
        /// Gets or sets the date.
        /// </summary>
        /// <value>The date.</value>
        public DateTime Date { get; set; }

        /// <summary>
        /// Gets or sets the error message.
        /// </summary>
        /// <value>The error message.</value>
        public string StatusMessage { get; set; }

        /// <summary>
        /// Gets or sets the status.
        /// </summary>
        /// <value>The status.</value>
        public FileSortingStatus Status { get; set; } 

        /// <summary>
        /// Gets or sets the type.
        /// </summary>
        /// <value>The type.</value>
        public FileOrganizerType Type { get; set; }

        /// <summary>
        /// Gets or sets the duplicate paths.
        /// </summary>
        /// <value>The duplicate paths.</value>
        public List<string> DuplicatePaths { get; set; }

        /// <summary>
        /// Gets or sets the size of the file.
        /// </summary>
        /// <value>The size of the file.</value>
        public long FileSize { get; set; }

        /// <summary>
        /// Indicates if the item is currently being processed.
        /// </summary>
        /// <remarks>Runtime property not persisted to the store.</remarks>
        public bool IsInProgress { get; set; }
          
        public List<string> ExternalSubtitlePaths { get; set; }

        public string SourceQuality { get; set; }

        public List<string> VideoStreamCodecs { get; set; }

        public List<string> AudioStreamCodecs { get; set; }

        public List<string> Subtitles { get; set; }

        public long ExistingInternalId{ get; set; }


        public FileOrganizationResult()
        {
            DuplicatePaths = new List<string>();
            ExternalSubtitlePaths = new List<string>();
            AudioStreamCodecs = new List<string>();
            VideoStreamCodecs = new List<string>();
            Subtitles = new List<string>();
            
        }
    }

    //public class Resolution
    //{
    //    public string Name { get; set; }
    //    public int Width { get; set; }
    //    public int Height { get; set; }
    //}
}
