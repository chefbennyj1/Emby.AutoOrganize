using Emby.AutoOrganize.Naming.Common;

namespace Emby.AutoOrganize.Naming.TV
{
    public class EpisodeResolver
    {
        private readonly NamingOptions _options;

        public EpisodeResolver(NamingOptions options)
        {
            _options = options;
        }

        public EpisodeInfo Resolve(string path, bool isDirectory, bool? isNamed = null, bool? isOptimistic = null, bool? supportsAbsoluteNumbers = null, bool fillExtendedInfo = true)
        {
            var parsingResult = new EpisodePathParser(_options).Parse(path, isDirectory, isNamed, isOptimistic, supportsAbsoluteNumbers, fillExtendedInfo);
            
            return new EpisodeInfo
            {
                Path = path,
                EndingEpisodeNumber = parsingResult.EndingEpisodeNumber,
                EpisodeNumber = parsingResult.EpisodeNumber,
                SeasonNumber = parsingResult.SeasonNumber,
                SeriesName = parsingResult.SeriesName,
                IsByDate = parsingResult.IsByDate,
                Day = parsingResult.Day,
                Month = parsingResult.Month,
                Year = parsingResult.Year
            };
        }
    }
}
