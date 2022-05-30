﻿
using System;
using System.Collections.Generic;

namespace Emby.AutoOrganize.Model
{
    public class SmartMatchResult
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public FileOrganizerType OrganizerType { get; set; }
        public List<string> MatchStrings { get; set; }
        public string TargetFolder { get; set; }
        /// <summary>
        /// User defined smart match entry.
        /// </summary>
        public bool IsCustomUserDefinedEntry { get; set; } 

        public SmartMatchResult()
        {
            MatchStrings = new List<string>();
            IsCustomUserDefinedEntry = false;
            TargetFolder = string.Empty;

        }
    }
}
