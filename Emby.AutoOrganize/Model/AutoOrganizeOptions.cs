﻿
namespace Emby.AutoOrganize.Model
{
    public class AutoOrganizeOptions
    {
        /// <summary>
        /// Gets or sets the tv options.
        /// </summary>
        /// <value>The tv options.</value>
        public EpisodeFileOrganizationOptions TvOptions { get; set; }

        /// <summary>
        /// Gets or sets the movie options.
        /// </summary>
        /// <value>The movie  options.</value>
        public MovieFileOrganizationOptions MovieOptions { get; set; }

        /// <summary>
        /// Gets or sets a list of smart match entries.
        /// </summary>
        /// <value>The smart match entries.</value>
        public SmartMatchInfo[] SmartMatchInfos { get; set; }

        public bool Converted { get; set; }

        public AutoOrganizeOptions()
        {
            TvOptions = new EpisodeFileOrganizationOptions();
            MovieOptions = new MovieFileOrganizationOptions();
            SmartMatchInfos = new SmartMatchInfo[] { };
            Converted = false;
        }
    }
}
