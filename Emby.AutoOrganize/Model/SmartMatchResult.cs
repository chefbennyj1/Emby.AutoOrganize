
using System;
using System.Collections.Generic;

namespace Emby.AutoOrganize.Model
{
    public class SmartMatchResult
    {
        public Guid Id { get; set; }
        public string ItemName { get; set; }
        public string DisplayName { get; set; }
        public FileOrganizerType OrganizerType { get; set; }
        public List<string> MatchStrings { get; set; }
        public List<string> Paths { get; set; }

        public SmartMatchResult()
        {
            
            Paths = new List<string>();
            MatchStrings = new List<string>();
        }
    }
}
