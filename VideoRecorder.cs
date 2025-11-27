using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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

            Debug.WriteLine($"VideoRecorder started. Region: {region}, Session dir: {_sessionDir}");

            // Check FFmpeg availability at startup
            var ffmpeg = FindFfmpeg();
            if (ffmpeg != null)
            {
                Debug.WriteLine($"FFmpeg found and ready: {ffmpeg}");
            }
            else
            {
                Debug.WriteLine("WARNING: FFmpeg not found - will save frames only");
            }
        }

        public void AddFrame(BitmapSource frame)
        {
            if (!_recording)
            {
                Debug.WriteLine("AddFrame called but recording is false");
                return;
            }

            try
            {
                var file = Path.Combine(_sessionDir, $"frame_{_frames.Count:000000}.png");
                using (var fs = File.Create(file))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(frame));
                    encoder.Save(fs);
                }
                _frames.Add(file);

                if (_frames.Count == 1 || _frames.Count % 15 == 0) // Log every second at 15fps
                {
                    Debug.WriteLine($"Frame {_frames.Count} captured: {file}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error capturing frame {_frames.Count}: {ex.Message}");
            }
        }

        public Task<string> StopAsync()
        {
            _recording = false;
            Debug.WriteLine($"StopAsync called. Recording stopped. Total frames before Task.Run: {_frames.Count}");

            return Task.Run(() =>
            {
                // Check if we have any frames
                if (_frames.Count == 0)
                {
                    Debug.WriteLine("No frames captured - check if timer is running and AddFrame is being called");
                    return _sessionDir;
                }

                Debug.WriteLine($"Total frames captured: {_frames.Count}");
                Debug.WriteLine($"Session directory: {_sessionDir}");

                // Attempt ffmpeg if available
                var ffmpeg = FindFfmpeg();
                if (ffmpeg == null)
                {
                    Debug.WriteLine("FFmpeg not found, returning frames directory");
                    return _sessionDir; // Frames only
                }

                var outputDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                Directory.CreateDirectory(outputDir);
                var output = Path.Combine(outputDir, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

                Debug.WriteLine($"Output file: {output}");
                Debug.WriteLine($"Using FFmpeg: {ffmpeg}");

                var inputPattern = Path.Combine(_sessionDir, "frame_%06d.png");
                var args = $"-y -framerate 15 -i \"{inputPattern}\" -c:v libx264 -pix_fmt yuv420p -preset ultrafast \"{output}\"";

                Debug.WriteLine($"FFmpeg arguments: {args}");

                var psi = new ProcessStartInfo(ffmpeg, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = _sessionDir
                };

                try
                {
                    var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        // Read output and error streams
                        var output_data = proc.StandardOutput.ReadToEnd();
                        var error_data = proc.StandardError.ReadToEnd();

                        proc.WaitForExit(60000); // 60 second timeout

                        Debug.WriteLine($"FFmpeg exit code: {proc.ExitCode}");

                        if (!string.IsNullOrWhiteSpace(output_data))
                        {
                            Debug.WriteLine($"FFmpeg output: {output_data}");
                        }

                        if (!string.IsNullOrWhiteSpace(error_data))
                        {
                            Debug.WriteLine($"FFmpeg error/info: {error_data}");
                        }

                        if (proc.ExitCode == 0 && File.Exists(output))
                        {
                            Debug.WriteLine($"Video successfully created: {output}");

                            // Clean up temporary frames after successful encoding
                            try
                            {
                                Directory.Delete(_sessionDir, true);
                                Debug.WriteLine("Temporary frames cleaned up");
                            }
                            catch (Exception cleanupEx)
                            {
                                Debug.WriteLine($"Failed to cleanup frames: {cleanupEx.Message}");
                            }
                            return output;
                        }
                        else
                        {
                            Debug.WriteLine($"FFmpeg encoding failed. Exit code: {proc.ExitCode}, File exists: {File.Exists(output)}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Failed to start FFmpeg process");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FFmpeg encoding error: {ex.GetType().Name} - {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }

                // If encoding failed, return the frames directory
                Debug.WriteLine("Returning frames directory due to encoding failure");
                return _sessionDir;
            });
        }

        private static string? FindFfmpeg()
        {
            Debug.WriteLine("=== Searching for FFmpeg ===");

            // 1. Check in application directory (bundled with solution)
            var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Debug.WriteLine($"Application directory: {appDir}");

            if (!string.IsNullOrEmpty(appDir))
            {
                // Check in FFmpeg subdirectory first
                var ffmpegSubdir = Path.Combine(appDir, "FFmpeg", "ffmpeg.exe");
                Debug.WriteLine($"Checking: {ffmpegSubdir}");
                if (File.Exists(ffmpegSubdir))
                {
                    Debug.WriteLine($" Found FFmpeg in FFmpeg subdirectory: {ffmpegSubdir}");
                    return ffmpegSubdir;
                }

                // Check directly in app directory
                var appDirFfmpeg = Path.Combine(appDir, "ffmpeg.exe");
                Debug.WriteLine($"Checking: {appDirFfmpeg}");
                if (File.Exists(appDirFfmpeg))
                {
                    Debug.WriteLine($" Found FFmpeg in application directory: {appDirFfmpeg}");
                    return appDirFfmpeg;
                }
            }

            // 2. Check current working directory
            var currentDir = Directory.GetCurrentDirectory();
            Debug.WriteLine($"Current working directory: {currentDir}");

            // Check in FFmpeg subdirectory of current directory
            var currentDirFfmpegSubdir = Path.Combine(currentDir, "FFmpeg", "ffmpeg.exe");
            Debug.WriteLine($"Checking: {currentDirFfmpegSubdir}");
            if (File.Exists(currentDirFfmpegSubdir))
            {
                Debug.WriteLine($" Found FFmpeg in current directory FFmpeg subdirectory: {currentDirFfmpegSubdir}");
                return currentDirFfmpegSubdir;
            }

            var currentDirFfmpeg = Path.Combine(currentDir, "ffmpeg.exe");
            Debug.WriteLine($"Checking: {currentDirFfmpeg}");
            if (File.Exists(currentDirFfmpeg))
            {
                Debug.WriteLine($"? Found FFmpeg in current directory: {currentDirFfmpeg}");
                return currentDirFfmpeg;
            }

            // 3. Check common installation paths
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin", "ffmpeg.exe")
            };

            foreach (var c in candidates)
            {
                Debug.WriteLine($"Checking: {c}");
                if (File.Exists(c))
                {
                    Debug.WriteLine($"Found FFmpeg at: {c}");
                    return c;
                }
            }

            // 4. Search in PATH environment variable
            var path = Environment.GetEnvironmentVariable("PATH");
            if (path != null)
            {
                Debug.WriteLine("Searching PATH environment variable...");
                foreach (var dir in path.Split(Path.PathSeparator))
                {
                    try
                    {
                        var f = Path.Combine(dir, "ffmpeg.exe");
                        if (File.Exists(f))
                        {
                            Debug.WriteLine($"Found FFmpeg in PATH: {f}");
                            return f;
                        }
                    }
                    catch
                    {
                        // Ignore invalid paths
                    }
                }
            }

            Debug.WriteLine("FFmpeg not found in any location");
            return null;
        }
    }
}