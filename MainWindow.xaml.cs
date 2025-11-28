using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace Screenshot
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Rect _selectedScreenRect;
        private SelectionOverlayWindow? _overlay;
        private readonly SettingsManager _settings = new SettingsManager();
        private WF.NotifyIcon? _tray;
        private VideoRecorder? _videoRecorder;
        private DispatcherTimer? _recordingTimer;
        private bool _isRecording;

        public ObservableCollection<CapturedItem> Items { get; } = new();

        private string? _captureDirectoryInput;
        public string? CaptureDirectory
        {
            get => _captureDirectoryInput;
            set
            {
                if (_captureDirectoryInput != value)
                {
                    _captureDirectoryInput = value;
                    OnPropertyChanged();
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            _settings.Load();
            _settings.EnsureDirectory();
            CaptureDirectory = _settings.EffectiveCaptureDirectory;
            // Removed: BtnRecordVideo.IsEnabled = false; - Now enabled by default
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;

            // Load existing captures
            LoadExistingCaptures();
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            InitializeTray();
            // Start hidden in tray
            Hide();
            ShowInTaskbar = false;
        }

        private void LoadExistingCaptures()
        {
            try
            {
                var targetDir = ResolveEffectiveCaptureDirectory();
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    return;
                }

                // Load all image and video files
                var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
                var videoExtensions = new[] { ".mp4", ".avi", ".mov", ".wmv" };
                var allExtensions = imageExtensions.Concat(videoExtensions).ToArray();

                var files = Directory.GetFiles(targetDir)
                    .Where(f => allExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToList();

                Debug.WriteLine($"Loading {files.Count} existing captures from {targetDir}");

                foreach (var file in files)
                {
                    try
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (imageExtensions.Contains(ext))
                        {
                            // Load image
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(file, UriKind.Absolute);
                            bitmap.EndInit();
                            bitmap.Freeze();

                            Items.Add(new CapturedItem(file, bitmap));
                        }
                        else if (videoExtensions.Contains(ext))
                        {
                            // Create video thumbnail (placeholder icon)
                            Items.Add(new CapturedItem(file, CreateVideoThumbnail()));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading capture {file}: {ex.Message}");
                    }
                }

                Debug.WriteLine($"Loaded {Items.Count} items successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading existing captures: {ex}");
            }
        }

        private BitmapSource CreateVideoThumbnail()
        {
            // Create a simple video icon thumbnail (160x108 with play button symbol)
            var width = 160;
            var height = 108;
            var dpi = 96.0;

            var drawingVisual = new DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                // Background
                context.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45)), null, new Rect(0, 0, width, height));

                // Play button circle
                var centerX = width / 2.0;
                var centerY = height / 2.0;
                var radius = 25.0;
                context.DrawEllipse(new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)), null, new System.Windows.Point(centerX, centerY), radius, radius);

                // Play triangle
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new System.Windows.Point(centerX - 8, centerY - 12), true, true);
                    ctx.LineTo(new System.Windows.Point(centerX - 8, centerY + 12), true, false);
                    ctx.LineTo(new System.Windows.Point(centerX + 10, centerY), true, false);
                }
                geometry.Freeze();
                context.DrawGeometry(new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45)), null, geometry);

                // Video text
                var text = new FormattedText("VIDEO", System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, System.Windows.Media.Brushes.White, dpi / 96.0);
                context.DrawText(text, new System.Windows.Point((width - text.Width) / 2, height - 20));
            }

            var renderTarget = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            renderTarget.Render(drawingVisual);
            renderTarget.Freeze();
            return renderTarget;
        }

        private void InitializeTray()
        {
            if (_tray != null) return;
            _tray = new WF.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "Screen Capture",
                Visible = true,
                ContextMenuStrip = new WF.ContextMenuStrip()
            };

            var menu = _tray.ContextMenuStrip;
            var regionItem = new WF.ToolStripMenuItem("Screenshot region");
            regionItem.Click += (_, _) => Dispatcher.Invoke(() => TriggerScreenshotRegionFromTray());
            var openItem = new WF.ToolStripMenuItem("Open");
            openItem.Click += (_, _) => Dispatcher.Invoke(ShowMainWindow);
            var exitItem = new WF.ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
            menu.Items.Add(regionItem);
            menu.Items.Add(openItem);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
        }

        private void ShowMainWindow()
        {
            Show();
            ShowInTaskbar = true;
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _recordingTimer?.Stop();
            _tray?.Dispose();
        }

        private void TriggerScreenshotRegionFromTray()
        {
            MinimizeForCapture();
            BeginRegionSelection(false);
        }

        private void MinimizeForCapture()
        {
            // Hide main window to avoid being captured.
            Hide();
            ShowInTaskbar = false;
        }

        private void ScreenshotRegion_Click(object sender, RoutedEventArgs e)
        {
            MinimizeForCapture();
            BeginRegionSelection(false);
        }

        private void BeginRegionSelection(bool isVideoMode)
        {
            _overlay = new SelectionOverlayWindow(isVideoMode);
            _overlay.RegionSelected += Overlay_RegionSelected;
            _overlay.RecordingStartRequested += Overlay_RecordingStartRequested;
            _overlay.Show();
        }

        private void Overlay_RegionSelected(object? sender, Rect e)
        {
            _selectedScreenRect = e;
            BtnScreenshotRegion.IsEnabled = true;
            BtnRecordVideo.IsEnabled = true;
            CaptureAndAddItem();
            ShowMainWindow();
        }

        private void Overlay_RecordingStartRequested(object? sender, Rect e)
        {
            _selectedScreenRect = e;
            Debug.WriteLine($"Recording start requested. Region: {e}");
            StartRecording();
        }

        private void CaptureAndAddItem()
        {
            if (_selectedScreenRect.IsEmpty) return;
            try
            {
                string targetDir = ResolveEffectiveCaptureDirectory();
                Directory.CreateDirectory(targetDir);
                var bmp = NativeCapture.CaptureScreenRect(_selectedScreenRect);
                var file = Path.Combine(targetDir, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                using (var fs = File.Create(file))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    encoder.Save(fs);
                }
                Items.Insert(0, new CapturedItem(file, bmp));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void RecordVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording)
            {
                _ = StopRecordingAsync();
            }
            else
            {
                MinimizeForCapture();
                BeginRegionSelection(true);
            }
        }

        // Find the StartRecording method (around line 167) and update it:
        private void StartRecording()
        {
            if (_selectedScreenRect.IsEmpty)
            {
                Debug.WriteLine("StartRecording: Screen rect is empty!");
                return;
            }

            Debug.WriteLine($"StartRecording called. Region: {_selectedScreenRect}");

            _isRecording = true;
            _videoRecorder = new VideoRecorder();

            // Pass the same directory used for screenshots
            string targetDir = ResolveEffectiveCaptureDirectory();
            Directory.CreateDirectory(targetDir);
            _videoRecorder.Start(_selectedScreenRect, targetDir);

            // Update button text
            BtnRecordVideo.Content = "⏹ Stop Recording";
            BtnScreenshotRegion.IsEnabled = false;

            // Show the overlay with recording controls
            if (_overlay != null)
            {
                _overlay.SwitchToRecordingMode();
            }

            // Start capturing frames at ~15 FPS
            _recordingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(67) // ~15 FPS
            };
            _recordingTimer.Tick += RecordingTimer_Tick;
            _recordingTimer.Start();

            Debug.WriteLine($"Recording timer started. Interval: {_recordingTimer.Interval}ms, IsEnabled: {_recordingTimer.IsEnabled}");
        }

        private void RecordingTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isRecording)
            {
                Debug.WriteLine("Timer tick but not recording");
                return;
            }

            try
            {
                var frame = NativeCapture.CaptureScreenRect(_selectedScreenRect);
                _videoRecorder?.AddFrame(frame);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Frame capture error: {ex}");
            }
        }

        private async Task StopRecordingAsync()
        {
            Debug.WriteLine("StopRecordingAsync called");

            // Stop timer first
            if (_recordingTimer != null)
            {
                Debug.WriteLine($"Stopping timer. IsEnabled: {_recordingTimer.IsEnabled}");
                _recordingTimer.Stop();
                _recordingTimer = null;
            }

            _isRecording = false;

            // Close the overlay if it's still open
            _overlay?.Close();
            _overlay = null;

            // Update button text
            BtnRecordVideo.Content = "⏺ Record Video";
            BtnRecordVideo.IsEnabled = false;
            BtnScreenshotRegion.IsEnabled = true;

            ShowMainWindow();

            if (_videoRecorder == null)
            {
                Debug.WriteLine("VideoRecorder is null");
                return;
            }

            try
            {
                Debug.WriteLine("Calling VideoRecorder.StopAsync()");
                var result = await _videoRecorder.StopAsync();
                _videoRecorder = null;

                if (File.Exists(result) && result.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    // Add video to list
                    Items.Insert(0, new CapturedItem(result, CreateVideoThumbnail()));

                    System.Windows.MessageBox.Show(this, $"Video saved to:\n{result}", "Recording Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Optionally open the folder
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{result}\"",
                        UseShellExecute = false
                    });
                }
                else
                {
                    System.Windows.MessageBox.Show(this, $"Frames saved to:\n{result}\n\nNote: FFmpeg not found. Install FFmpeg to encode videos.", "Recording Complete (Frames Only)", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Error stopping recording:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine(ex);
            }
            finally
            {
                BtnRecordVideo.IsEnabled = true;
            }
        }

        public void RequestStopRecording()
        {
            Debug.WriteLine("RequestStopRecording called from overlay");
            _ = StopRecordingAsync();
        }

        private void EditItem_Click(object sender, RoutedEventArgs e)
        {
            // placeholder for editing logic
        }

        private void Publish_Click(object sender, RoutedEventArgs e)
        {
            // placeholder for publishing logic
        }

        private string ResolveEffectiveCaptureDirectory()
        {
            if (string.IsNullOrWhiteSpace(CaptureDirectory))
                return _settings.DefaultCaptureDirectory;
            return CaptureDirectory!;
        }

        private void SettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            // settings placeholder
        }

        private void AboutMenu_Click(object sender, RoutedEventArgs e)
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            System.Windows.MessageBox.Show(this, $"Screen Capture\nVersion: {ver}", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void HelpMenu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://learn.microsoft.com/windows/apps/desktop/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void ExitMenu_Click(object sender, RoutedEventArgs e) => System.Windows.Application.Current.Shutdown();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void CapturedList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
    }

    public sealed class CapturedItem
    {
        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);
        public BitmapSource Thumbnail { get; }
        public bool IsVideo => Path.GetExtension(FilePath).ToLowerInvariant() is ".mp4" or ".avi" or ".mov" or ".wmv";

        public CapturedItem(string path, BitmapSource source)
        {
            FilePath = path;

            // For videos, use the provided thumbnail directly
            if (source.PixelWidth == 160 && source.PixelHeight == 108)
            {
                Thumbnail = source;
            }
            else
            {
                // For images, scale down
                double scaleX = 160.0 / source.PixelWidth;
                double scaleY = 108.0 / source.PixelHeight;
                Thumbnail = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scaleX, scaleY));
            }

            Thumbnail.Freeze();
        }
    }

    public class SelectionOverlayWindow : Window
    {
        private System.Windows.Point _start;
        private System.Windows.Media.RectangleGeometry _rectGeo = new System.Windows.Media.RectangleGeometry();
        private System.Windows.Shapes.Path _rectPath = new System.Windows.Shapes.Path
        {
            Stroke = System.Windows.Media.Brushes.DeepSkyBlue,
            StrokeThickness = 2,
            Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 30, 144, 255)),
            Data = new System.Windows.Media.RectangleGeometry()
        };
        private readonly bool _isVideoMode;
        private System.Windows.Controls.StackPanel? _controlPanel;
        private System.Windows.Controls.Button? _recordBtn;
        private bool _isDragging;
        private bool _isRecordingMode;

        public event EventHandler<Rect>? RegionSelected;
        public event EventHandler<Rect>? RecordingStartRequested;
        public Rect? SelectedRegion { get; private set; }

        public SelectionOverlayWindow(bool isVideoMode = false)
        {
            _isVideoMode = isVideoMode;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 0, 0, 0));
            Opacity = 1;
            Topmost = true;
            ShowInTaskbar = false;
            Cursor = System.Windows.Input.Cursors.Cross;
            Left = 0;
            Top = 0;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            var canvas = new System.Windows.Controls.Canvas();
            Content = canvas;
            canvas.Children.Add(_rectPath);

            if (_isVideoMode)
            {
                // Set default selection region (center of screen, 800x600)
                var defaultWidth = Math.Min(800, Width * 0.6);
                var defaultHeight = Math.Min(600, Height * 0.6);
                var defaultX = (Width - defaultWidth) / 2;
                var defaultY = (Height - defaultHeight) / 2;
                var defaultRect = new Rect(defaultX, defaultY, defaultWidth, defaultHeight);
                _rectGeo.Rect = defaultRect;
                _rectPath.Data = _rectGeo;
                SelectedRegion = defaultRect;

                Debug.WriteLine($"SelectionOverlayWindow: Video mode with default region: {defaultRect}");

                // Create control panel
                CreateControlPanel();
                canvas.Children.Add(_controlPanel);
                PositionControlPanel();
            }

            MouseDown += Overlay_MouseDown;
            MouseMove += Overlay_MouseMove;
            MouseUp += Overlay_MouseUp;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { SelectedRegion = null; Close(); } };
        }

        private void CreateControlPanel()
        {
            _controlPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 40, 40, 40)),
                Height = 60
            };

            var style = new Style(typeof(System.Windows.Controls.Button));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.FontSizeProperty, 20.0));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 50.0));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 50.0));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(5)));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));

            // Record button (red circle)
            _recordBtn = new System.Windows.Controls.Button
            {
                Style = style,
                Content = "⏺",
                Foreground = System.Windows.Media.Brushes.Red,
                FontSize = 30,
                ToolTip = "Start Recording"
            };
            _recordBtn.Click += RecordButton_Click;

            // Screenshot button
            var screenshotBtn = new System.Windows.Controls.Button
            {
                Style = style,
                Content = "📷",
                ToolTip = "Take Screenshot"
            };
            screenshotBtn.Click += (s, e) => TakeScreenshot();

            // Microphone button
            var micBtn = new System.Windows.Controls.Button
            {
                Style = style,
                Content = "🎤",
                ToolTip = "Toggle Microphone"
            };

            // Webcam button
            var webcamBtn = new System.Windows.Controls.Button
            {
                Style = style,
                Content = "🎥",
                ToolTip = "Toggle Webcam"
            };

            // Settings button
            var settingsBtn = new System.Windows.Controls.Button
            {
                Style = style,
                Content = "⚙",
                ToolTip = "Settings"
            };

            // Close button
            var closeBtn = new System.Windows.Controls.Button
            {
                Style = style,
                Content = "✕",
                ToolTip = "Cancel"
            };
            closeBtn.Click += (s, e) => { SelectedRegion = null; Close(); };

            _controlPanel.Children.Add(_recordBtn);
            _controlPanel.Children.Add(screenshotBtn);
            _controlPanel.Children.Add(new System.Windows.Controls.Separator { Width = 2, Background = System.Windows.Media.Brushes.Gray });
            _controlPanel.Children.Add(micBtn);
            _controlPanel.Children.Add(webcamBtn);
            _controlPanel.Children.Add(new System.Windows.Controls.Separator { Width = 2, Background = System.Windows.Media.Brushes.Gray });
            _controlPanel.Children.Add(settingsBtn);
            _controlPanel.Children.Add(closeBtn);
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecordingMode)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        private void PositionControlPanel()
        {
            if (_controlPanel == null) return;
            _controlPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var panelWidth = _controlPanel.DesiredSize.Width;
            System.Windows.Controls.Canvas.SetLeft(_controlPanel, (Width - panelWidth) / 2);
            System.Windows.Controls.Canvas.SetBottom(_controlPanel, 30);
        }

        public void SwitchToRecordingMode()
        {
            Debug.WriteLine("SelectionOverlayWindow: Switching to recording mode");
            _isRecordingMode = true;

            // Update record button to stop button
            if (_recordBtn != null)
            {
                _recordBtn.Content = "⏹";
                _recordBtn.Foreground = System.Windows.Media.Brushes.White;
                _recordBtn.ToolTip = "Stop Recording";
            }

            // Make background more transparent during recording
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(5, 0, 0, 0));

            // Disable region selection during recording
            Cursor = System.Windows.Input.Cursors.Arrow;
        }

        private void StartRecording()
        {
            Debug.WriteLine($"SelectionOverlayWindow: StartRecording clicked. SelectedRegion: {SelectedRegion}");
            if (SelectedRegion.HasValue && SelectedRegion.Value.Width >= 4 && SelectedRegion.Value.Height >= 4)
            {
                RecordingStartRequested?.Invoke(this, SelectedRegion.Value);
                // Don't close - stay open with controls visible
            }
            else
            {
                Debug.WriteLine("SelectionOverlayWindow: Invalid region for recording");
            }
        }

        private void StopRecording()
        {
            Debug.WriteLine("SelectionOverlayWindow: StopRecording clicked");
            // Notify MainWindow to stop recording
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RequestStopRecording();
            }
        }

        private void TakeScreenshot()
        {
            if (SelectedRegion.HasValue && SelectedRegion.Value.Width >= 4 && SelectedRegion.Value.Height >= 4)
            {
                RegionSelected?.Invoke(this, SelectedRegion.Value);
                Close();
            }
        }

        private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isRecordingMode)
            {
                _isDragging = true;
                _start = e.GetPosition(this);

                if (!_isVideoMode)
                {
                    _rectGeo = new System.Windows.Media.RectangleGeometry(new Rect(_start, _start));
                    _rectPath.Data = _rectGeo;
                }
            }
        }

        private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _isDragging && !_isRecordingMode)
            {
                var current = e.GetPosition(this);
                var rect = new Rect(_start, current);
                rect = NormalizeRect(rect);
                _rectGeo.Rect = rect;
                SelectedRegion = rect;
            }
        }

        private void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _isDragging && !_isRecordingMode)
            {
                _isDragging = false;

                if (!_isVideoMode)
                {
                    var end = e.GetPosition(this);
                    var rect = NormalizeRect(new Rect(_start, end));
                    if (rect.Width >= 4 && rect.Height >= 4)
                    {
                        SelectedRegion = rect;
                        RegionSelected?.Invoke(this, rect);
                    }
                    Close();
                }
            }
        }

        private static Rect NormalizeRect(Rect r)
        {
            if (r.Width < 0) r = new Rect(r.X + r.Width, r.Y, -r.Width, r.Height);
            if (r.Height < 0) r = new Rect(r.X, r.Y + r.Height, r.Width, -r.Height);
            return r;
        }
    }
}