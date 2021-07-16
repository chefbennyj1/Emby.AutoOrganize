using Emby.Naming.Common;

namespace Emby.AutoOrganize.Core
{
    public class FileOrganizerHelper 
    {
        public static string GetFileResolutionFromName(string movieName)
        {
            var namingOptions = new NamingOptions();
            
            foreach(var resolution in namingOptions.VideoResolutionFlags)
            {
                if(movieName.Contains(resolution))
                {
                    return resolution;

                }
            }
            return string.Empty;
            
        }

    }
}
