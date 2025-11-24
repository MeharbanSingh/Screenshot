using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

namespace Screenshot
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Rect _selectedScreenRect;
        private SelectionOverlayWindow? _overlay;
        private readonly SettingsManager _settings = new SettingsManager();
        private WF.NotifyIcon? _tray;

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
            BtnRecordVideo.IsEnabled = false; // disabled until implemented
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            InitializeTray();
            // Start hidden in tray
            Hide();
            ShowInTaskbar = false;
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
            _tray?.Dispose();
        }

        private void TriggerScreenshotRegionFromTray()
        {
            MinimizeForCapture();
            BeginRegionSelection();
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
            BeginRegionSelection();
        }

        private void BeginRegionSelection()
        {
            _overlay = new SelectionOverlayWindow();
            _overlay.RegionSelected += Overlay_RegionSelected;
            _overlay.Show();
        }

        private void Overlay_RegionSelected(object? sender, Rect e)
        {
            _selectedScreenRect = e;
            BtnScreenshotRegion.IsEnabled = true;
            BtnRecordVideo.IsEnabled = true;
            CaptureAndAddItem(); // auto capture once region selected for demo
            // After capture, restore window (optional). Comment out if you prefer staying hidden.
            ShowMainWindow();
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
                Items.Add(new CapturedItem(file, bmp));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void RecordVideo_Click(object sender, RoutedEventArgs e)
        {
            // placeholder
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
        public CapturedItem(string path, BitmapSource source)
        {
            FilePath = path;
            double scaleX = 160.0 / source.PixelWidth;
            double scaleY = 108.0 / source.PixelHeight;
            Thumbnail = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scaleX, scaleY));
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
        public event EventHandler<Rect>? RegionSelected;
        public Rect? SelectedRegion { get; private set; }

        public SelectionOverlayWindow()
        {
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
            MouseDown += Overlay_MouseDown;
            MouseMove += Overlay_MouseMove;
            MouseUp += Overlay_MouseUp;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { SelectedRegion = null; Close(); } };
        }

        private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _start = e.GetPosition(this);
                _rectGeo = new System.Windows.Media.RectangleGeometry(new Rect(_start, _start));
                _rectPath.Data = _rectGeo;
            }
        }

        private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var current = e.GetPosition(this);
                var rect = new Rect(_start, current);
                rect = NormalizeRect(rect);
                _rectGeo.Rect = rect;
            }
        }

        private void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
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

        private static Rect NormalizeRect(Rect r)
        {
            if (r.Width < 0) r = new Rect(r.X + r.Width, r.Y, -r.Width, r.Height);
            if (r.Height < 0) r = new Rect(r.X, r.Y + r.Height, r.Width, -r.Height);
            return r;
        }
    }
}