using System.Threading;
using System.Threading.Tasks;
using Emby.AutoOrganize.Model;

namespace Emby.AutoOrganize.Core.FileOrganization
{
    public interface IFileOrganizer
    {
        Task<FileOrganizationResult> OrganizeFile(bool? requestToMoveFile, string path,
            AutoOrganizeOptions options, CancellationToken cancellationToken);


    }
}
