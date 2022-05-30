using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Emby.AutoOrganize.MediaInfo
{
    public class Resolution
    {
        public int Height { get; set; }
        public int Width { get; set; }
        public string Name { get; set; }
    }

    public class MediaInfoProvider
    {
        public List<string> AudioStreamCodecs { get; set; }
        public List<string> VideoStreamCodecs { get; set; }
        public Resolution Resolution          { get; set; }
        public List<string> Subtitles         { get; set; }

        public MediaInfoProvider()
        {
            AudioStreamCodecs = new List<string>();
            VideoStreamCodecs = new List<string>();
            Subtitles         = new List<string>();
            Resolution        = new Resolution();
        }


        public async Task<MediaInfoProvider> GetMediaInfo(string filePath)
        {
            var internalStreamInfo = new MediaInfoProvider();

            Ffprobe.FileInternalMediaInfo mediaInfo;
            try
            {
                mediaInfo = await Ffprobe.Instance.GetFileMediaInfo(filePath);
            }
            catch (Exception)
            {
                throw new Exception("file in use");
            }

            if (mediaInfo != null)
            {
                foreach (var stream in mediaInfo.streams)
                {
                    switch (stream.codec_type)
                    {
                        case "audio":
                            if (!string.IsNullOrEmpty(stream.codec_name)) //&& !result.AudioStreamCodecs.Any())
                            {
                                internalStreamInfo.AudioStreamCodecs.Add(stream.codec_name);
                            }

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
                                        internalStreamInfo.VideoStreamCodecs.Add(codec.Trim().Split(' ')[0]);
                                        continue;
                                    }

                                    internalStreamInfo.VideoStreamCodecs.Add(codec.Trim());
                                }
                            }

                            //if (string.IsNullOrEmpty(result.ExtractedResolution.Name))
                            //{
                            if (stream.width != 0 && stream.height != 0)
                            {
                                internalStreamInfo.Resolution = new Resolution()
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
                                    internalStreamInfo.Subtitles.Add(language);
                                }
                            }

                            break;
                    }
                }
            }

            return internalStreamInfo;
        }

        private string GetResolutionFromMetadata(Ffprobe.MediaStream stream)
        {
            var width = stream.width;
            var height = stream.height;

            var diagonal = Math.Round(Math.Sqrt(Math.Pow(width, 2) + Math.Pow(height, 2)), 2);

            if (stream.profile == "High")
            {
                if (diagonal <= 800) return "480p"; //4:3
                if (diagonal > 800 && diagonal <= 1468.6) return "720p"; //16:9
                if (diagonal > 1468.6 && diagonal <= 2315.32) return "1080p"; //16:9 or 1:1.77
                if (diagonal > 2315.32 && diagonal <= 2937.21) return "1440p"; //16:9
                if (diagonal > 2937.21 && diagonal <= 4405.81) return "2160p"; //1:1.9 - 4K
                if (diagonal > 4405.81 && diagonal <= 8811.63) return "4320p"; //16∶9 - 8K
            }
            else
            {
                return "SD";
            }

            return "Unknown";
        }

    }
}
