using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Emby.AutoOrganize.Naming.Common
{
    public class SourceQuality
    {
        //private enum QualityWikiFlag
        //{
        //    CAM = 11,
        //    PPV = 15,
        //    DVDRIP = 19,
        //    HDRIP = 25,
        //    WEBRIP = 26,
        //    WEBDL = 27,
        //    BDRIP = 28,
        //    BLURAY = 28,
        //    BDMV = 28,
        //    BD = 28,
        //    HDTV = 25
        //}
        public static string GetSourceQuality(string fileName)
        {
            const string pattern = @"(?:\bxvid|xvidvd|[Ww]eb-[Rr]ip|[Ww][Ee][Bb]-[Dd][Ll]|[Ww]eb[Dd][Ll]|[Ww]eb[Rr]ip|[Ww][Ee][Bb]|[Bb]lu[Rr]ay|[Bb]lu-[Rr]ay|[Hh][Dd][Tt][Vv]|DVD[Rr]ip|PDTV|HDRip|BD|BRRip|BDMV|[Cc][Aa][Mm]|PPV\b)";
            var result = string.Join("-", Regex.Matches(fileName, pattern, RegexOptions.Multiline).Cast<Match>().Select(m => m.Value));
            return result;
        }
        
        //public string GetSourceQualityDescription(string quality)
        //{
        //    var qualityDescriptionFlag = Enum.Parse(typeof(QualityWikiFlag), RegexExtensions.NormalizeString(quality).ToUpper());



        //}
    }
}
