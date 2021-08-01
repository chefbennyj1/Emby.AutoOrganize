namespace Emby.AutoOrganize.Model
{
    public interface IOrganizationOptions
    {
        bool IsEnabled {get; set; }
        int MinFileSizeMb { get; set; }
        string[] IgnoredFileNameContains {get; set;}
        string[] LeftOverFileExtensionsToDelete { get; set; }
        string[] WatchLocations { get; set; }
        bool CopyOriginalFile { get; set; }
        bool OverwriteExistingFiles { get; set; }
        bool DeleteEmptyFolders { get; set; }
        bool ExtendedClean { get; set; }
        bool AutoDetect { get; set; }
        string DefaultLibraryPath { get; set; }
    }
}
