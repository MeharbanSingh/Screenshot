using System;
using System.IO;
using System.Text.Json;

namespace Screenshot
{
    public sealed class SettingsManager
    {
        private const string SettingsFileName = "settings.json";
        private static readonly string AppFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenshotApp");

        private static readonly string SettingsFilePath = Path.Combine(AppFolder, SettingsFileName);

        private class SettingsModel
        {
            public string? CaptureDirectory { get; set; }
        }

        private SettingsModel _model = new();

        public string DefaultCaptureDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshot");

        public string EffectiveCaptureDirectory =>
            string.IsNullOrWhiteSpace(_model.CaptureDirectory) ? DefaultCaptureDirectory : _model.CaptureDirectory!;

        public void Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    _model = new SettingsModel();
                    return;
                }

                var json = File.ReadAllText(SettingsFilePath);
                _model = JsonSerializer.Deserialize<SettingsModel>(json) ?? new SettingsModel();
            }
            catch
            {
                _model = new SettingsModel();
            }
        }

        public void SetCaptureDirectory(string? path)
        {
            _model.CaptureDirectory = string.IsNullOrWhiteSpace(path) ? null : path!.Trim();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppFolder);
                var json = JsonSerializer.Serialize(_model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Silently ignore; could add logging.
            }
        }

        public void EnsureDirectory()
        {
            try
            {
                Directory.CreateDirectory(EffectiveCaptureDirectory);
            }
            catch
            {
                // Ignore; capture will fail later with clearer message.
            }
        }
    }
}