using MediaBrowser.Model.Entities;

namespace Emby.AutoOrganize.Model.Organization
{
    public class EpisodeFileOrganizationRequest : IFileOrganizationRequest
    {
        public string ResultId                  { get; set; }
        
        public string SeriesId                  { get; set; }

        public int? SeasonNumber                { get; set; }

        public int? EpisodeNumber               { get; set; }

        public int? EndingEpisodeNumber         { get; set; }

        public bool RememberCorrection          { get; set; }

        public string Name                      { get; set; }

        public int? Year                        { get; set; }

        public string TargetFolder              { get; set; }

        public ProviderIdDictionary ProviderIds { get; set; }

        public bool? RequestToMoveFile          {get; set;}

        public bool CreateNewDestination        { get; set; }

    }
}