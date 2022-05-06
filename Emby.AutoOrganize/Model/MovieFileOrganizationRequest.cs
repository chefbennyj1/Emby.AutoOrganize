using MediaBrowser.Model.Entities;

namespace Emby.AutoOrganize.Model
{
    public class MovieFileOrganizationRequest : IFileOrganizationRequest
    {
        public string ResultId                  { get; set; }
        
        public string MovieId                   { get; set; }

        public string Name                      { get; set; }

        public int? Year                        { get; set; }

        public string TargetFolder              { get; set; }

        public bool? RequestToMoveFile          { get; set; }

        public ProviderIdDictionary ProviderIds { get; set; }

        public bool CreateNewDestination        { get; set; }
    }
}