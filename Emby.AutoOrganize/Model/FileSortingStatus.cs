namespace Emby.AutoOrganize.Model
{
    public enum FileSortingStatus
    {
        Success,
        Failure,
        SkippedExisting,
        NewResolution,
        Processing,
        UserInputRequired,
        NotEnoughDiskSpace,
        InUse,
        NewMedia,
        NewEdition,
        Checking
    }
}