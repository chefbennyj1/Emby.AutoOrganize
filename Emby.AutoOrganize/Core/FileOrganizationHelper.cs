using Emby.AutoOrganize.Model;
using Emby.Naming.Common;
using MediaBrowser.Model.IO;
using System;
using System.Collections.Generic;
using System.Text;

namespace Emby.AutoOrganize.Core
{
    public class FileOrganizationHelper
    {

        public static bool IsCopying(string source, FileOrganizationResult dbResult, IFileSystem _fileSystem)
        {
            try
            {
                var sourceFile = _fileSystem.GetFileInfo(source);
                if(dbResult.FileSize == sourceFile.Length) return false;

            } catch (Exception)
            {
                return true;
            }
            return true;
        }
        public static string GetStreamResolutionFromFileName(string movieName)
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

        public static bool IgnoredFileName(FileSystemMetadata fileInfo, string[] ignoredFileNameContains)
        {            
            foreach (var ignoredString in ignoredFileNameContains)
            {
                if(ignoredString == string.Empty) continue;
                if (fileInfo.Name.ToLowerInvariant().Contains(ignoredString.ToLowerInvariant()))
                {                   
                    return true;
                }
            }
            return false;
        }

    }
}
