using System;

namespace Emby.AutoOrganize.Model.Corrections
{
    public class FileCorrectionException : Exception
    {
        public FileCorrectionException() : base() { }
        public FileCorrectionException(string message) : base(message) { }
        public FileCorrectionException(string message, System.Exception inner) : base(message, inner) { }

        protected FileCorrectionException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
