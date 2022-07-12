using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Emby.AutoOrganize.FileMetadata
{
    public class Resolution
    {
        public int Height { get; set; }
        public int Width { get; set; }
        public string Name { get; set; }
    }

    public class MediaInfo
    {
        public List<string> AudioStreamCodecs { get; }
        public List<string> VideoStreamCodecs { get; }
        public Resolution Resolution          { get; private set; }
        public List<string> Subtitles         { get; }
        public int AudioChannels              { get; set; }
        public DateTime CreationDate          { get; set; }
        
        public MediaInfo()
        {
            AudioStreamCodecs = new List<string>();
            VideoStreamCodecs = new List<string>();
            Subtitles         = new List<string>();
            Resolution        = new Resolution();
        }


        public async Task<MediaInfo> GetMediaInfo(string filePath)
        {
            var mediaInfo = new MediaInfo();

            FileInternalMediaInfo mediaInfoProvider;
            try
            {
                mediaInfoProvider = await MediaInfoProvider.Instance.GetFileMediaInfo(filePath);
            }
            catch (Exception)
            {
                throw new Exception("file in use");
            }

            if (mediaInfoProvider == null) return mediaInfo;

            mediaInfo.CreationDate = DateTime.UtcNow;

            if (mediaInfoProvider.format != null)
            {
                if (mediaInfoProvider.format.tags != null)
                {
                    if (mediaInfoProvider.format.tags.creation_time.HasValue)
                    {
                        mediaInfo.CreationDate = mediaInfoProvider.format.tags.creation_time.Value;
                    }
                }
            }
            

            foreach (var stream in mediaInfoProvider.streams)
            {
                switch (stream.codec_type)
                {
                    case "audio":
                            
                        if (!string.IsNullOrEmpty(stream.codec_name)) //&& !result.AudioStreamCodecs.Any())
                        {
                            mediaInfo.AudioStreamCodecs.Add(stream.codec_name);
                        }

                            
                        mediaInfo.AudioChannels = stream.channels;
                            

                        break;
                    case "video":
                    {
                        if (!string.IsNullOrEmpty(stream.codec_long_name)) //&& !result.VideoStreamCodecs.Any())
                        {
                            var codecs = stream.codec_long_name.Split('/');
                            foreach (var codec in codecs.Take(3))
                            {
                                if (codec.Trim().Split(' ').Length > 1)
                                {
                                    mediaInfo.VideoStreamCodecs.Add(codec.Trim().Split(' ')[0]);
                                    continue;
                                }

                                mediaInfo.VideoStreamCodecs.Add(codec.Trim());
                            }
                        }

                        //if (string.IsNullOrEmpty(result.ExtractedResolution.Name))
                        //{
                        if (stream.width != 0 && stream.height != 0)
                        {
                            mediaInfo.Resolution = new Resolution()
                            {
                                Name = GetResolutionFromMetadata(stream),
                                Width = stream.width,
                                Height = stream.height
                            };

                        }

                           
                           
                                
                        //}

                        break;
                    }
                    case "subtitle":

                        if (stream.tags != null)
                        {
                            var language = stream.tags.title ?? stream.tags.language;
                            if (!string.IsNullOrEmpty(language))
                            {
                                mediaInfo.Subtitles.Add(language);
                            }
                        }

                        break;
                }
            }

            return mediaInfo;
        }

        private string GetResolutionFromMetadata(MediaStream stream)
        {
            var width = stream.width;
            var height = stream.height;

            var diagonal = Math.Round(Math.Sqrt(Math.Pow(width, 2) + Math.Pow(height, 2)), 2);

            
                if (diagonal < 579.0)                            return "SD";    //4:3
                if (diagonal > 579.0 && diagonal   <= 749)       return "480p";  //4:3
                if (diagonal > 749.0 && diagonal   <= 920.0)     return "540p";
                if (diagonal > 920.0 && diagonal   <= 1101.4)    return "576p";
                if (diagonal > 1101.4 && diagonal  <= 1468.6)    return "720p";  //16:9
                if (diagonal > 1468.6 && diagonal  <= 2937.21)   return "1080p"; //16:9 or 1:1.77
                if (diagonal > 2937.21 && diagonal <= 4405.81)   return "2160p"; //1:1.9 - 4K
                if (diagonal > 4405.81 && diagonal <= 8811.63)   return "4320p"; //16∶9 - 8K

                return "Unknown";
        }

    }
}
