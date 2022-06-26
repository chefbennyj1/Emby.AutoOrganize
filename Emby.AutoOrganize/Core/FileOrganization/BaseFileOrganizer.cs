using System;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Model;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
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

        // ReSharper disable once UnusedMember.Local
        void OrganizeWithCorrection(T request, AutoOrganizeOptions options, CancellationToken cancellationToken){}

    }
}
