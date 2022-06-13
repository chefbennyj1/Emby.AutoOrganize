using System;

namespace Emby.AutoOrganize.Core.FileOrganization
{
    public class InvalidTargetFolderException : Exception
    {
        public InvalidTargetFolderException() : base() { }
        public InvalidTargetFolderException(string message) : base(message) { }
        public InvalidTargetFolderException(string message, System.Exception inner) : base(message, inner) { }

        protected InvalidTargetFolderException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
