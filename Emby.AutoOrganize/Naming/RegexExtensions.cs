﻿using System.Linq;
using System.Text.RegularExpressions;
using Emby.AutoOrganize.Naming.Common;

namespace Emby.AutoOrganize.Naming
{
    public class RegexExtensions
    {
        public static string GetSourceQuality(string fileName)
        {
            const string pattern = @"(?:\bxvid|xvidvd|[Ww]eb-[Rr]ip|[Ww][Ee][Bb]-[Dd][Ll]|[Ww]eb[Dd][Ll]|[Ww]eb[Rr]ip|[Ww][Ee][Bb]|[Bb]lu[Rr]ay|[Bb]lu-[Rr]ay|DUBBED|[Hh][Dd][Tt][Vv]|DVD[Rr]ip|PDTV|HDRip|BRRip|BDMV|[Cc][Aa][Mm]|HC\b)";
            var result = string.Join("-", Regex.Matches(fileName, pattern, RegexOptions.Multiline).Cast<Match>().Select(m => m.Value));
            return result;
        }

        public static string NormalizeMediaItemName(string input)
        {
            return Regex.Replace(input, @"[^A-Za-z0-9\s+()]", " ", RegexOptions.IgnoreCase).Replace("  ", " ").Trim();
        }

        public static string NormalizeSearchStringComparison(string input)
        {
            return Regex.Replace(input, @"(\s+|@|&|'|:|\(|\)|<|>|#|-|\.|\b[Aa]nd\b|,|_)", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
        }

        public static string GetReleaseEditionFromFileName(string sourceFileName)
        {
            var namingOptions = new NamingOptions();
            var pattern = $"(?i)({string.Join("|", namingOptions.VideoReleaseEditionFlags)})";
            var input   = Regex.Replace(sourceFileName, @"(@|&|'|:|\(|\)|<|>|#|\.|,|_)", " ", RegexOptions.IgnoreCase).ToLowerInvariant();
            var results = Regex.Matches(input, pattern, RegexOptions.IgnoreCase);
            var result  = results.Count > 0 ? results[0].Value : "Theatrical";
            return namingOptions.VideoReleaseEditionFlags.FirstOrDefault(flag => flag.ToLowerInvariant().Contains(result.ToLowerInvariant()));
        }

        public static string ParseSubtitleLanguage(string sourceFileName)
        {
            var namingOptions = new NamingOptions();
            var pattern = "(?:";
            for (var index = 0; index < namingOptions.SubtitleLanguageExtensions.Length; index++)
            {
                var lan = namingOptions.SubtitleLanguageExtensions[index];
                pattern += @"\" + lan + @"[A-z]{0,1}\.";

                pattern += index < namingOptions.SubtitleLanguageExtensions.Length - 1 ? "|" : ")";
                
            }

            //+ $"{string.Join(@"[A-z]{0,1}\.|", namingOptions.SubtitleLanguageExtensions)}" + ")";}
            var input = Regex.Replace(sourceFileName, @"(@|&|'|:|\(|\)|<|>|#|,)", ".", RegexOptions.IgnoreCase).ToLowerInvariant();
            //var input = sourceFileName.Replace("_", ".").Replace(",", ".");
            var results = Regex.Matches(input, pattern, RegexOptions.IgnoreCase);
            return results.Count > 0 ? results[0].Value : string.Empty;
        }


    }
}