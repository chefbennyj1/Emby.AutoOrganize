
namespace Emby.AutoOrganize.Model
{
    public class FileOrganizationResultQuery
    {
         
        // Skips over a given number of items within the results. Use for paging.
        public int? StartIndex { get; set; }

        // The maximum number of items to return
        public int? Limit { get; set; }

        //The type of media to respond with
        public string Type { get; set; }

        //List Direction
        public string DataOrderDirection { get; set; } = "DESC";

        public string SortBy { get; set; } = "OrganizationDate";
    }
}
