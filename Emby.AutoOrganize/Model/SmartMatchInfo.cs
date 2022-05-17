
using System;
using System.Collections.Generic;

namespace Emby.AutoOrganize.Model
{
    public class SmartMatchInfo1
    {
        //public string SeriesId { get; set; }
        public string ItemName { get; set; }
        public string DisplayName { get; set; }
        public FileOrganizerType OrganizerType { get; set; }
        public string[] MatchStrings { get; set; }
        public string[] Paths { get; set; }
        public Guid Id { get; set; }

        public SmartMatchInfo1()
        {
            MatchStrings = new string[] { };
            Paths = new string[] { };
        }
    }
}
