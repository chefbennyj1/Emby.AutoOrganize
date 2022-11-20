
namespace Emby.AutoOrganize.Model
{
    public class AutoOrganizeOptions
    {
        public bool EnableTelevisionOrganization              { get; set; }
        public bool EnableMovieOrganization                   { get; set; }
        public string[] WatchLocations                        { get; set; }
        public bool EnableSubtitleOrganization                { get; set; }
        public int MinFileSizeMb                              { get; set; }
       
        public string SeasonFolderPattern                     { get; set; }
        public string SeasonZeroFolderName                    { get; set; }
        public string EpisodeNamePattern                      { get; set; }
        public string MultiEpisodeNamePattern                 { get; set; }
        public string[] IgnoredFileNameContains               { get; set; }
        public bool DeleteSortedItemsFromPreProcessingFolder  { get; set; } 
       
        public bool ExtendedClean                             { get; set; }
        public bool CopyOriginalFile                          { get; set; }
        public bool AutoDetectSeries                          { get; set; }
        public bool CreateNewSeriesFolders                    { get; set; }
        public string DefaultSeriesLibraryPath                { get; set; }
        public string SeriesFolderPattern                     { get; set; }
        public string MoviePattern                            { get; set; }
        public bool OverwriteExistingEpisodeFiles             { get; set; }
        public bool OverwriteExistingMovieFiles               { get; set; }
        public bool AutoDetectMovie                           { get; set; }
        public string DefaultMovieLibraryPath                 { get; set; }
        public bool CreateMovieInFolder                       { get; set; }
        public string MovieFolderPattern                      { get; set; }
        public string[] OverwriteExistingEpisodeFilesKeyWords { get; set; }
        public string[] OverwriteExistingMovieFilesKeyWords   { get; set; }
        public string PreProcessingFolderPath                 { get; set; }
        public bool EnablePreProcessing                       { get; set; }
        public bool EnableCleanupOptions                      { get; set; }
        public bool DeleteEmptyFolders                        { get; set; }
         public string[] LeftOverFileExtensionsToDelete       { get; set; }
        public bool EnableFileNameCorrections                 { get; set; }
        public AutoOrganizeOptions()
        {
            MinFileSizeMb                           = 50;
            EnableTelevisionOrganization            = false;
            EnableMovieOrganization                 = false;
            MovieFolderPattern                      = "%mn (%my)";
            MoviePattern                            = "%mn (%my) - %res [ %e ].%ext";
            IgnoredFileNameContains                 = new string[] { };
            WatchLocations                          = new string[] { };
            CopyOriginalFile                        = false;
            CreateMovieInFolder                     = true;
            EnableSubtitleOrganization              = false;
            ExtendedClean                           = false;
            IgnoredFileNameContains                 = new string[] { };
            EpisodeNamePattern                      = "%sn - %sx%0e - %en.%ext";
            MultiEpisodeNamePattern                 = "%sn - %sx%0e-x%0ed - %en.%ext";
            SeasonFolderPattern                     = "Season %s";
            SeasonZeroFolderName                    = "Season 0";
            SeriesFolderPattern                     = "%fn";
            LeftOverFileExtensionsToDelete          = new string[] { };
            OverwriteExistingEpisodeFilesKeyWords   = new string[] { };
            OverwriteExistingMovieFilesKeyWords     = new string[] { };
            EnablePreProcessing                     = false;
            EnableFileNameCorrections               = true;            
            DeleteEmptyFolders                      = false;
            EnableCleanupOptions                    = false;
        }
    }
}
