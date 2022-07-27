namespace Emby.AutoOrganize.Model
{
    public enum FileSortingStatus
    {
        Success,
        Failure,
        SkippedExisting,
        NewResolution,
        Checking,
        Processing,
        UserInputRequired,
        NotEnoughDiskSpace,
        InUse,
        NewMedia,
        NewEdition
    }
}