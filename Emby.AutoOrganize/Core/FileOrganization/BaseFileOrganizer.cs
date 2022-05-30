using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Api;
using Emby.AutoOrganize.Data;
using Emby.AutoOrganize.Model;
using MediaBrowser.Common.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace Emby.AutoOrganize.Core.FileOrganization
{
    public abstract class BaseFileOrganizer<T>
    {
        protected BaseFileOrganizer(IFileOrganizationService organizationService, IFileSystem fileSystem, ILogger log,
            ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, IProviderManager providerManager) { }

        Task<FileOrganizationResult> OrganizeFile(bool requestToMovieFile, string path,
            AutoOrganizeOptions options, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();

        }

        void PerformFileSorting(AutoOrganizeOptions options, FileOrganizationResult result,
            CancellationToken cancellationToken){}

        void OrganizeWithCorrection(T request, AutoOrganizeOptions options, CancellationToken cancellationToken){}

        
    }
}
