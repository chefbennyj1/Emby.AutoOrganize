namespace Emby.AutoOrganize.Model
{
    public interface IFileOrganizationRequest
    {
        string ResultId { get; set; }
        string TargetFolder { get; set; }
        bool? RequestToMoveFile { get; set; }
    }
}
