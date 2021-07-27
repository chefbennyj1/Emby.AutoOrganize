using Emby.AutoOrganize.Model;
using Emby.Naming.Common;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using System;
using System.Collections.Generic;
using System.Linq;

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

        
        public static FileOrganizationResult GetFolderSubtitleData(string path, IFileSystem _fileSystem, ILibraryManager _libraryManager, FileOrganizationResult result)
        {
            var files = _fileSystem.GetFiles(path, true);
            var subtitlePaths = new List<string>();
            
            foreach(var file in files)
            {
                var namingOptions = new NamingOptions();
                var subtitleExtentions = namingOptions.SubtitleFileExtensions;

                if (subtitleExtentions.Contains(file.Extension))
                {
                    subtitlePaths.Add(file.FullName); 
                }
            }

            result.HasSubtitleFiles = subtitlePaths.Count > 0;
            result.SubtitleFilePaths = subtitlePaths;            
            return result;
        }

    }
}
