using MediaBrowser.Model.Entities;

namespace Emby.AutoOrganize.Model
{
    public interface IFileOrganizationRequest
    {
        string ResultId                  { get; set; }
        string TargetFolder              { get; set; }
        bool? RequestToMoveFile          { get; set; }
        int? Year                        { get; set; }
        string Name                      { get; set; }
        ProviderIdDictionary ProviderIds { get; set; }
        bool CreateNewDestination        { get; set; }

    }
}
