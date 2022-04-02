
namespace Emby.AutoOrganize.Model
{
    public class AutoOrganizeOptions
    {
        public SmartMatchInfo[] SmartMatchInfos        { get; set; }
        public bool Converted                          { get; set; }
        public string[] WatchLocations                 { get; set; }
        public bool EnableScheduledTask                { get; set; } = true;
        public bool IsMovieSortingEnabled              { get; set; } = true;
        public bool IsEpisodeSortingEnabled            { get; set; } = true;
        public int MinFileSizeMb                       { get; set; }
        public string[] LeftOverFileExtensionsToDelete { get; set; }
        public string SeasonFolderPattern              { get; set; }
        public string SeasonZeroFolderName             { get; set; }
        public string EpisodeNamePattern               { get; set; }
        public string MultiEpisodeNamePattern          { get; set; }
        public string[] IgnoredFileNameContains        { get; set;}
        public bool DeleteEmptyFolders                 { get; set; }
        public bool ExtendedClean                      { get; set; }
        public bool CopyOriginalFile                   { get; set; }
        public bool AutoDetectSeries                   { get; set; }
        public bool AutoDetectSeasons                  { get; set;}
        public string DefaultSeriesLibraryPath         { get; set; }
        public string SeriesFolderPattern              { get; set; }
        public string MoviePattern                     { get; set; }
        public bool OverwriteExistingFiles             { get; set; }
        public bool AutoDetectMovie                    { get; set; }
        public string DefaultMovieLibraryPath          { get; set; }
        public bool CreateMovieInFolder                { get; set; }
        public string MovieFolderPattern               { get; set; }
        public string[] OverwriteExistingFilesKeyWords { get; set; }
        public AutoOrganizeOptions()
        {
            MinFileSizeMb = 50;
            SmartMatchInfos = new SmartMatchInfo[] { };
            Converted = false;
            MovieFolderPattern = "%mn (%my)";
            MoviePattern = "%mn (%my) - %res [ %e ].%ext";
            IgnoredFileNameContains = new string[] { };
            WatchLocations = new string[] { };
            CopyOriginalFile = false;
            CreateMovieInFolder = true;
            ExtendedClean = false;
            IgnoredFileNameContains = new string[] { };
            EpisodeNamePattern = "%sn - %sx%0e - %en.%ext";
            MultiEpisodeNamePattern = "%sn - %sx%0e-x%0ed - %en.%ext";
            SeasonFolderPattern = "Season %s";
            SeasonZeroFolderName = "Season 0";
            SeriesFolderPattern = "%fn";
            LeftOverFileExtensionsToDelete = new string[] { };
            OverwriteExistingFilesKeyWords = new string[] { };
        }
    }
}
