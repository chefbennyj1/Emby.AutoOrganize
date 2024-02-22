//using System;
//using System.Threading;
//using Emby.AutoOrganize.Model.Corrections;
//using MediaBrowser.Model.Querying;

//namespace Emby.AutoOrganize.Core
//{
//    public interface IFileCorrectionService
//    {
//        FileCorrection GetFilePathCorrection(string id);

//        QueryResult<FileCorrection> GetFilePathCorrections(FileCorrectionResultQuery resultQuery);

//        void DeleteFilePathCorrection(string id);

//        void DeleteAllFilePathCorrections();

//        void SaveResult(FileCorrection result, CancellationToken cancellationToken);

//        void CorrectFileNames(FileCorrection correction);

//        void AuditPathCorrections(CancellationToken cancellationToken, IProgress<double> progress);
//    }
//}
