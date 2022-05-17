using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Emby.AutoOrganize.Model;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.AutoOrganize.Ffprobe
{
    public class Ffprobe : IServerEntryPoint
    {
        private IFfmpegManager FfmpegManager           { get; }
        public static Ffprobe Instance { get; set; }
        private IJsonSerializer JsonSerializer { get; set; }

        private ConcurrentDictionary<string, int> FfprobeProcessMonitor = new ConcurrentDictionary<string, int>();
        public Ffprobe(IFfmpegManager ffmpeg, IJsonSerializer json)
        {
            FfmpegManager = ffmpeg;
            JsonSerializer = json;
            Instance = this;
        }

        public async Task<FileInternalMetadata> GetFileInternalMetadata(string path, ILogger Log)
        {
            var ffmpegConfiguration = FfmpegManager.FfmpegConfiguration;
            var ffprobePath = ffmpegConfiguration.EncoderPath.Replace("ffmpeg", "ffprobe");
            var args = new[]
            {
                "-loglevel 0",
                "-print_format json",
                "-show_format",
                "-show_streams",
                $"\"{path}\"",
                
               
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
                using (var process = new Process {StartInfo = procStartInfo})
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
            return await Task.FromResult(JsonSerializer.DeserializeFromString<FileInternalMetadata>(json));

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
