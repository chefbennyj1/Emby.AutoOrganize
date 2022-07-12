namespace Emby.AutoOrganize.Model.Corrections
{
    public class FileCorrection
    {
        public string Id            { get; set; }
        public string CorrectedPath { get; set; }
        public string CurrentPath   { get; set; }
        public string SeriesName    { get; set; }
    }
}
