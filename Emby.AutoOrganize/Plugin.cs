using System;
using System.Collections.Generic;
using System.ComponentModel;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System.IO;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.Drawing;

namespace Emby.AutoOrganize
{
    public class Plugin : BasePlugin, IHasWebPages, IHasThumbImage
    {
        
        public override string Name => "Auto Organize";


        public override string Description
            => "Automatically organize new media";

        private Guid _id = new Guid("14f5f69e-4c8d-491b-8917-8e90e8317530");
        public override Guid Id
        {
            get { return _id; }
        }

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.gif");
        }

        public ImageFormat ThumbImageFormat
        {
            get
            {
                return ImageFormat.Gif;
            }
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "AutoOrganizeLog",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizelog.html",
                    EnableInMainMenu = true
                },
                new PluginPageInfo
                {
                    Name = "AutoOrganizeSmart",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizesmart.html"
                },
                new PluginPageInfo
                {
                    Name = "AutoOrganizeSettings",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizesettings.html"
                },
                //new PluginPageInfo
                //{
                //    Name = "AutoOrganizeMovie",
                //    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizemovie.html"
                //},
                new PluginPageInfo
                {
                    Name = "AutoOrganizeLogJs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizelog.js"
                },
                new PluginPageInfo
                {
                    Name = "AutoOrganizeSmartJs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizesmart.js"
                },
                new PluginPageInfo
                {
                    Name = "AutoOrganizeSettingsJs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizesettings.js"
                },
                //new PluginPageInfo
                //{
                //    Name = "AutoOrganizeMovieJs",
                //    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizemovie.js"
                //},
                new PluginPageInfo
                {
                    Name = "FileOrganizerJs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.fileorganizer.js"
                },
                new PluginPageInfo
                {
                    Name = "FileOrganizerHtml",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.fileorganizer.template.html"
                },
                new PluginPageInfo
                {
                    Name = "AutoOrganizeCorrectionsJs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizecorrections.js"
                },
                new PluginPageInfo
                {
                    Name = "AutoOrganizeCorrections",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.autoorganizecorrections.html"
                },
                
                //new PluginPageInfo
                //{
                //Name = "Chart.js",
                //EmbeddedResourcePath = GetType().Namespace + ".Configuration.Chart.js"
                //}
            };
        }
    }
}
