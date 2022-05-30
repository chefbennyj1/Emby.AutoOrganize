using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.AutoOrganize.MediaInfo
{
    public class Ffprobe : IServerEntryPoint
    {
        public class Disposition
        {
            public int @default { get; set; }
            public int dub { get; set; }
            public int original { get; set; }
            public int comment { get; set; }
            public int lyrics { get; set; }
            public int karaoke { get; set; }
            public int forced { get; set; }
            public int hearing_impaired { get; set; }
            public int visual_impaired { get; set; }
            public int clean_effects { get; set; }
            public int attached_pic { get; set; }
            public int timed_thumbnails { get; set; }


        }

        public class Format
        {
            public string filename { get; set; }
            public int nb_streams { get; set; }
            public int nb_programs { get; set; }
            public string format_name { get; set; }
            public string format_long_name { get; set; }
            public string start_time { get; set; }
            public string duration { get; set; }
            public string size { get; set; }
            public string bit_rate { get; set; }
            public int probe_score { get; set; }
            public Tags tags { get; set; }
        }

        public class FileInternalMediaInfo
        {
            public List<MediaStream> streams { get; set; }
            public Format format { get; set; }
        }

        public class MediaStream
        {

            public string profile { get; set; }
            public int coded_width { get; set; }
            public int coded_height { get; set; }
            public int has_b_frames { get; set; }
            public string sample_aspect_ratio { get; set; }
            public string display_aspect_ratio { get; set; }
            public string pix_fmt { get; set; }
            public int level { get; set; }
            public string color_range { get; set; }
            public string color_space { get; set; }
            public string color_transfer { get; set; }
            public string color_primaries { get; set; }
            public string chroma_location { get; set; }
            public string field_order { get; set; }
            public int refs { get; set; }
            public string is_avc { get; set; }
            public string nal_length_size { get; set; }

            public string bits_per_raw_sample { get; set; }

            public string dmix_mode { get; set; }
            public string ltrt_cmixlev { get; set; }
            public string ltrt_surmixlev { get; set; }
            public string loro_cmixlev { get; set; }
            public string loro_surmixlev { get; set; }

            public int width { get; set; }
            public int height { get; set; }
            public int index { get; set; }
            public string codec_name { get; set; }
            public string codec_long_name { get; set; }
            public string codec_type { get; set; }
            public string codec_time_base { get; set; }
            public string codec_tag_string { get; set; }
            public string codec_tag { get; set; }
            public string sample_fmt { get; set; }
            public string sample_rate { get; set; }
            public int channels { get; set; }
            public string channel_layout { get; set; }
            public int bits_per_sample { get; set; }
            public string r_frame_rate { get; set; }
            public string avg_frame_rate { get; set; }
            public string time_base { get; set; }
            public int start_pts { get; set; }
            public string start_time { get; set; }
            public long duration_ts { get; set; }
            public string duration { get; set; }
            public string bit_rate { get; set; }
            public Disposition disposition { get; set; }
            public Tags tags { get; set; }
        }

        public class Tags
        {
            public string title { get; set; }
            public string language { get; set; }
            public string encoder { get; set; }
            public string major_brand { get; set; }
            public string minor_version { get; set; }
            public string compatible_brands { get; set; }
        }

        private IFfmpegManager FfmpegManager { get; }
        public static Ffprobe Instance { get; set; }
        private IJsonSerializer JsonSerializer { get; set; }

        private ConcurrentDictionary<string, int> FfprobeProcessMonitor = new ConcurrentDictionary<string, int>();
        public Ffprobe(IFfmpegManager ffmpeg, IJsonSerializer json)
        {
            FfmpegManager = ffmpeg;
            JsonSerializer = json;
            Instance = this;
        }

        public async Task<FileInternalMediaInfo> GetFileMediaInfo(string path)
        {
            var ffmpegConfiguration = FfmpegManager.FfmpegConfiguration;
            var ffprobePath = ffmpegConfiguration.EncoderPath.Replace("ffmpeg", "ffprobe");
            var args = new[]
            {
                "-loglevel 0",
                "-print_format json",
                "-show_format",
                "-show_streams",
                $"\"{path}\""
            };

            var procStartInfo = new ProcessStartInfo(ffprobePath, string.Join(" ", args))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var json = string.Empty;
            try
            {
                using (var process = new Process { StartInfo = procStartInfo })
                {
                    process.Start();
                    FfprobeProcessMonitor.TryAdd(path, process.Id);
                    var reader = process.StandardOutput;
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        json += line;
                    }

                    reader.Close();
                }
            }
            catch (Exception)
            {
                throw new Exception();
            }

            EnsureFfprobeEol(path);
            return await Task.FromResult(JsonSerializer.DeserializeFromString<FileInternalMediaInfo>(json));

        }

        private bool EnsureFfprobeEol(string path)
        {
            var process = Process.GetProcesses()
                .Where(p => p.Id == FfprobeProcessMonitor.FirstOrDefault(s => s.Key == path).Value).ToList().FirstOrDefault();

            if (process is null)
            {
                return FfprobeProcessMonitor.TryRemove(path, out _);
            }

            try
            {
                process.Kill();
            }
            catch
            {

            }
            return FfprobeProcessMonitor.TryRemove(path, out _);
        }
        public void Dispose()
        {

        }

        public void Run()
        {

        }
    }
}
