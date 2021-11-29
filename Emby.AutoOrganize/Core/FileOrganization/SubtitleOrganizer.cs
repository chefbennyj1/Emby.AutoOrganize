using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Emby.AutoOrganize.Model;

namespace Emby.AutoOrganize.Core.FileOrganization
{
    public class SubtitleOrganizer
    {
        public void OrganizeSubtitleFile(List<string> eligibleFiles, FileOrganizationResult result)
        {
            var source = result.OriginalFileName;
            foreach (var file in eligibleFiles)
            {
                if (Path.GetFileNameWithoutExtension(file) == Path.GetFileNameWithoutExtension(source))
                {
                    result.ExternalSubtitlePaths.Add(file);
                }
            }
        }

    }
}
