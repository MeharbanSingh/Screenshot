using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Screenshot
{
    public sealed class VideoRecorder
    {
        private readonly List<string> _frames = new List<string>();
        private Rect _region;
        private string _sessionDir = string.Empty;
        private bool _recording;

        public void Start(Rect region)
        {
            _region = region;
            _sessionDir = Path.Combine(Path.GetTempPath(), "capture_session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(_sessionDir);
            _frames.Clear();
            _recording = true;
        }

        public void AddFrame(BitmapSource frame)
        {
            if (!_recording) return;
            var file = Path.Combine(_sessionDir, $"frame_{_frames.Count:000000}.png");
            using (var fs = File.Create(file))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));
                encoder.Save(fs);
            }
            _frames.Add(file);
        }

        public Task<string> StopAsync()
        {
            _recording = false;
            return Task.Run(() =>
            {
                // Attempt ffmpeg if available
                var ffmpeg = FindFfmpeg();
                if (ffmpeg == null)
                {
                    return _sessionDir; // Frames only
                }

                var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

                var args = $"-y -framerate 15 -i \"{Path.Combine(_sessionDir, "frame_%06d.png")}\" -c:v libx264 -pix_fmt yuv420p \"{output}\"";

                var psi = new ProcessStartInfo(ffmpeg, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit(15000);
                return output;
            });
        }

        private static string? FindFfmpeg()
        {
            string[] candidates =
            {
                "ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin", "ffmpeg.exe")
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            // PATH search
            var path = Environment.GetEnvironmentVariable("PATH");
            if (path != null)
            {
                foreach (var dir in path.Split(Path.PathSeparator))
                {
                    try
                    {
                        var f = Path.Combine(dir, "ffmpeg.exe");
                        if (File.Exists(f)) return f;
                    }
                    catch { /* ignore */ }
                }
            }
            return null;
        }
    }
}