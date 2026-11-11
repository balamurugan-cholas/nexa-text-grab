using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tesseract;
using Hardcodet.Wpf.TaskbarNotification;
using OpenCvSharp;
using Window = System.Windows.Window;

namespace NexaTextGrab;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x4E58;
    private const int CornerHotkeyId = 0x4E59;
    private const int FreehandHotkeyId = 0x4E5A;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;
    private static readonly string[] KeyOptions = ["Space", "C", "V", "Z", "A", "S", "D", "Q", "W", "E", "R", "T", "F", "G", "H", "J", "K", "L"];
    private static readonly string[] LanguageOptions = ["English", "Hindi", "Tamil", "Auto (all)"];

    private readonly UserSettings _settings = UserSettings.Load();
    private TaskbarIcon? _trayIcon;
    private HwndSource? _hookSource;
    private bool _hotkeyRegistered;
    private bool _cornerHotkeyRegistered;
    private bool _freehandHotkeyRegistered;
    private bool _allowClose;
    private bool _isSelecting;
    private SelectionWindow? _selectionWindow;
    private int _startingVirtualX;
    private int _startingVirtualY;
    private int _virtualWidth;
    private int _virtualHeight;
    private CancellationTokenSource? _operationCancellation;
    private TesseractEngine? _ocrEngine;
    private readonly SemaphoreSlim _ocrGate = new(1, 1);

    public MainWindow()
    {
        InitializeComponent();
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Nexa Text Grab — Ctrl + Shift + " + _settings.Hotkey,
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "NexaTextGrab.ico")),
            Visibility = Visibility.Visible,
            ContextMenu = CreateTrayMenu()
        };
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowSettings();
        InitializeControls();
    }

    public void Initialize(string[] args)
    {
        if (args.Contains("--select", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, StartSelection);
    }

    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu();
        var select = new MenuItem { Header = "Select text   Ctrl + Shift + " + _settings.Hotkey };
        select.Click += (_, _) => StartSelection();
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => ShowSettings();
        var quit = new MenuItem { Header = "Exit Nexa Text Grab" };
        quit.Click += (_, _) => ExitApplication();
        menu.Items.Add(select);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        return menu;
    }

    private void InitializeControls()
    {
        HotkeyKeyBox.ItemsSource = KeyOptions;
        HotkeyKeyBox.SelectedItem = KeyOptions.Contains(_settings.Hotkey) ? _settings.Hotkey : "Space";
        if (!KeyOptions.Contains(_settings.Hotkey)) { _settings.Hotkey = "Space"; _settings.Save(); }
        UpdateQuickGuideHotkey();
        LinesModeRadio.IsChecked = !_settings.CopyAsBlock;
        BlockModeRadio.IsChecked = _settings.CopyAsBlock;
        StartupCheckBox.IsChecked = StartupManager.IsEnabled();
        LanguageBox.ItemsSource = LanguageOptions;
        LanguageBox.SelectedItem = LanguageOptions.Contains(_settings.OcrLanguage) ? _settings.OcrLanguage : "English";
    }

    private void CaptureNav_Click(object sender, RoutedEventArgs e) { }
    private void PreferencesNav_Click(object sender, RoutedEventArgs e) { }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hookSource = HwndSource.FromHwnd(handle);
        _hookSource.AddHook(WindowMessageHook);
        RegisterGlobalHotkey();
        _ = UpdateOcrStatusAsync();
    }

    private async Task UpdateOcrStatusAsync()
    {
        try
        {
            var tessdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaTextGrab", "tessdata");
            Directory.CreateDirectory(tessdata);
            foreach (var language in new[] { "eng", "hin", "tam" })
            {
                var targetModel = Path.Combine(tessdata, language + ".traineddata");
                var bundledModel = Path.Combine(AppContext.BaseDirectory, "tessdata", language + ".traineddata");
                if (!File.Exists(bundledModel)) throw new FileNotFoundException("OCR model was not found: " + language);
                if (!File.Exists(targetModel) || File.GetLastWriteTimeUtc(bundledModel) > File.GetLastWriteTimeUtc(targetModel))
                {
                    File.Copy(bundledModel, targetModel);
                }
            }
            _ocrEngine = new TesseractEngine(tessdata, GetLanguageCode(), EngineMode.Default);
            SetStatus("Local OCR ready", "#159767");
            StatusLabel.Text = "Ready in the tray · OCR runs locally on this PC.";
        }
        catch (Exception ex)
        {
            SetStatus("OCR unavailable", "#CE7E27");
            StatusLabel.Text = "Local OCR could not start: " + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    private string GetLanguageCode() => _settings.OcrLanguage switch { "Hindi" => "hin", "Tamil" => "tam", "Auto (all)" => "eng+hin+tam", _ => "eng" };

    private async void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LanguageBox.SelectedItem is not string selected) return;
        _settings.OcrLanguage = selected;
        _settings.Save();
        try
        {
            await _ocrGate.WaitAsync();
            try
            {
                _ocrEngine?.Dispose();
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaTextGrab", "tessdata");
                _ocrEngine = new TesseractEngine(data, GetLanguageCode(), EngineMode.Default);
            }
            finally { _ocrGate.Release(); }
            StatusLabel.Text = $"OCR language: {selected}";
        }
        catch (Exception ex) { StatusLabel.Text = "Could not load OCR language: " + ex.Message; }
    }

    private void SetStatus(string text, string color)
    {
        StatusLabel.Text = text;
    }

    private void RegisterGlobalHotkey()
    {
        if (_hookSource is null) return;
        UnregisterHotKey(_hookSource.Handle, HotkeyId);
        UnregisterHotKey(_hookSource.Handle, CornerHotkeyId);
        UnregisterHotKey(_hookSource.Handle, FreehandHotkeyId);
        _hotkeyRegistered = false;
        var selected = HotkeyKeyBox.SelectedItem as string ?? "Space";
        UpdateQuickGuideHotkey();
        var key = selected == "Space" ? 0x20 : char.ToUpperInvariant(selected[0]);
        _hotkeyRegistered = RegisterHotKey(_hookSource.Handle, HotkeyId, ModControl | ModShift | ModNoRepeat, (uint)key);
        _cornerHotkeyRegistered = RegisterHotKey(_hookSource.Handle, CornerHotkeyId, ModControl | ModShift | ModNoRepeat, (uint)'1');
        _freehandHotkeyRegistered = RegisterHotKey(_hookSource.Handle, FreehandHotkeyId, ModControl | ModShift | 0x0001 | ModNoRepeat, 0x20);
        _trayIcon!.ToolTipText = "Nexa Text Grab — Ctrl + Shift + " + selected;
        if (!_hotkeyRegistered || !_cornerHotkeyRegistered || !_freehandHotkeyRegistered)
        {
            var failed = !_hotkeyRegistered ? $"Ctrl + Shift + {selected}" : !_cornerHotkeyRegistered ? "Ctrl + Shift + 1" : "Ctrl + Shift + Alt + Space";
            StatusLabel.Text = $"Shortcut unavailable: {failed} is already in use.";
        }
    }

    private void UpdateQuickGuideHotkey()
    {
        if (QuickGuideHotkey is not null)
            QuickGuideHotkey.Text = $"Ctrl + Shift + Alt + Space: freeform   ·   Ctrl + Shift + {_settings.Hotkey}: rectangle   ·   Ctrl + Shift + 1: four corners";
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            StartSelection();
        }
        else if (msg == WmHotkey && wParam.ToInt32() == CornerHotkeyId)
        {
            handled = true;
            StartSelection(true);
        }
        else if (msg == WmHotkey && wParam.ToInt32() == FreehandHotkeyId)
        {
            handled = true;
            StartSelection(false, true);
        }
        return IntPtr.Zero;
    }

    private void HotkeyKeyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            _settings.Hotkey = HotkeyKeyBox.SelectedItem as string ?? "Space";
            _settings.Save();
            RegisterGlobalHotkey();
        }
    }

    private void CopyMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.CopyAsBlock = BlockModeRadio.IsChecked == true;
        _settings.Save();
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        try
        {
            if (StartupCheckBox.IsChecked == true) StartupManager.Enable();
            else StartupManager.Disable();
        }
        catch (Exception ex)
        {
            StartupCheckBox.IsChecked = StartupManager.IsEnabled();
            StatusLabel.Text = "Could not update startup setting: " + ex.Message;
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e) => StartSelection();

    private void StartSelection(bool cornerMode = false, bool freehandMode = false)
    {
        if (_isSelecting) return;
        _isSelecting = true;
        _operationCancellation?.Cancel();
        try
        {
            _startingVirtualX = GetSystemMetrics(76);
            _startingVirtualY = GetSystemMetrics(77);
            _virtualWidth = GetSystemMetrics(78);
            _virtualHeight = GetSystemMetrics(79);
            if (_virtualWidth <= 0 || _virtualHeight <= 0) throw new InvalidOperationException("Could not read screen size.");

            using var screenBitmap = new System.Drawing.Bitmap(_virtualWidth, _virtualHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(screenBitmap))
                graphics.CopyFromScreen(_startingVirtualX, _startingVirtualY, 0, 0, new System.Drawing.Size(_virtualWidth, _virtualHeight), System.Drawing.CopyPixelOperation.SourceCopy);

            var wpfBitmap = ConvertBitmap(screenBitmap);
            Hide();
            _selectionWindow = new SelectionWindow(wpfBitmap, _startingVirtualX, _startingVirtualY, _virtualWidth, _virtualHeight, cornerMode, freehandMode);
            _selectionWindow.SelectionCompleted += SelectionWindow_SelectionCompleted;
            _selectionWindow.SelectionCancelled += SelectionWindow_SelectionCancelled;
            _selectionWindow.Show();
            _selectionWindow.Activate();
        }
        catch (Exception ex)
        {
            _isSelecting = false;
            ShowSettings();
            StatusLabel.Text = "Could not start selection: " + ex.Message;
        }
    }

    private static BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
        stream.Position = 0;
        var result = new BitmapImage();
        result.BeginInit();
        result.CacheOption = BitmapCacheOption.OnLoad;
        result.StreamSource = stream;
        result.EndInit();
        result.Freeze();
        return result;
    }

    private async void SelectionWindow_SelectionCompleted(object? sender, SelectionCompletedEventArgs e)
    {
        _selectionWindow?.Close();
        _selectionWindow = null;
        var cropRect = e.Corners is not null ? System.Drawing.Rectangle.Empty : e.Freehand is not null ? Bounds(e.Freehand) : Rectangle.Intersect(e.Selection, new System.Drawing.Rectangle(0, 0, _virtualWidth, _virtualHeight));
        if (e.Corners is null && (cropRect.Width < 3 || cropRect.Height < 3))
        {
            _isSelecting = false;
            StatusLabel.Text = "Selection too small. Press the shortcut and drag a larger area.";
            _trayIcon?.ShowBalloonTip("Selection too small", "Try selecting a larger text area.", BalloonIcon.Warning);
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        try
        {
            var engine = _ocrEngine ?? throw new InvalidOperationException("Local OCR engine is not ready.");
            await _ocrGate.WaitAsync();
            string[] lines;
            try { lines = await Task.Run(() => e.Corners is not null ? RecognizePerspective(engine,e.Background,e.Corners) : e.Freehand is not null ? RecognizeFreehand(engine,e.Background,e.Freehand,cropRect) : Recognize(engine,e.Background,cropRect)); }
            finally { _ocrGate.Release(); }
            var output = _settings.CopyAsBlock ? string.Join(" ", lines) : string.Join(Environment.NewLine, lines);
            if (string.IsNullOrWhiteSpace(output))
            {
                _isSelecting = false;
                StatusLabel.Text = "No text found. Try a closer crop with sharper, well-lit text.";
                _trayIcon?.ShowBalloonTip("No text found", "Try a closer crop with sharper, well-lit text.", BalloonIcon.Warning);
                return;
            }

            Clipboard.SetText(output);
            _isSelecting = false;
            ShowTransientSuccess(lines.Length, output.Length);
        }
        catch (Exception ex)
        {
            _isSelecting = false;
            StatusLabel.Text = "Could not copy text: " + ex.Message;
            _trayIcon?.ShowBalloonTip("Text capture failed", ex.Message, BalloonIcon.Warning);
        }
    }

    private static System.Drawing.Rectangle Bounds(System.Drawing.PointF[] pts)
    {
        var x=(int)Math.Floor(pts.Min(p=>p.X)); var y=(int)Math.Floor(pts.Min(p=>p.Y));
        var r=(int)Math.Ceiling(pts.Max(p=>p.X)); var b=(int)Math.Ceiling(pts.Max(p=>p.Y));
        return Rectangle.Intersect(new System.Drawing.Rectangle(x,y,Math.Max(1,r-x),Math.Max(1,b-y)),new System.Drawing.Rectangle(0,0,GetSystemMetrics(78),GetSystemMetrics(79)));
    }

    private static string[] RecognizeFreehand(TesseractEngine engine, BitmapSource source, System.Drawing.PointF[] points, System.Drawing.Rectangle bounds)
    {
        var enc=new BmpBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(source)); using var stream=new MemoryStream(); enc.Save(stream);
        using var src=Cv2.ImDecode(stream.ToArray(),ImreadModes.Color); using var crop=new Mat(src,new OpenCvSharp.Rect(bounds.X,bounds.Y,bounds.Width,bounds.Height)).Clone();
        var local=points.Select(p=>new OpenCvSharp.Point((int)Math.Round(p.X-bounds.X),(int)Math.Round(p.Y-bounds.Y))).ToArray();
        using var mask=new Mat(crop.Rows,crop.Cols,MatType.CV_8UC1,Scalar.Black); Cv2.FillPoly(mask,new[]{local},Scalar.White);
        using var result=new Mat(crop.Rows,crop.Cols,MatType.CV_8UC3,Scalar.White); crop.CopyTo(result,mask);
        using var pix=Pix.LoadFromMemory(result.ToBytes(".bmp")); return RecognizeBestOrientation(engine,pix);
    }

    private static string[] RecognizePerspective(TesseractEngine engine, BitmapSource source, System.Drawing.PointF[] corners)
    {
        var width = (int)Math.Max(Distance(corners[0], corners[1]), Distance(corners[3], corners[2]));
        var height = (int)Math.Max(Distance(corners[0], corners[3]), Distance(corners[1], corners[2]));
        if (width < 10 || height < 10) return [];
        var encoder = new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var stream = new MemoryStream(); encoder.Save(stream);
        using var src = Cv2.ImDecode(stream.ToArray(), ImreadModes.Color);
        var from = corners.Select(p => new Point2f(p.X, p.Y)).ToArray();
        var to = new[] { new Point2f(0,0), new Point2f(width-1,0), new Point2f(width-1,height-1), new Point2f(0,height-1) };
        using var transform = Cv2.GetPerspectiveTransform(from, to); using var corrected = new Mat();
        Cv2.WarpPerspective(src, corrected, transform, new OpenCvSharp.Size(width,height), InterpolationFlags.Cubic, BorderTypes.Replicate);
        using var pix = Pix.LoadFromMemory(corrected.ToBytes(".bmp")); return RecognizeBestOrientation(engine,pix);
    }

    private static double Distance(System.Drawing.PointF a, System.Drawing.PointF b) { var x=a.X-b.X; var y=a.Y-b.Y; return Math.Sqrt(x*x+y*y); }

    private static string[] Recognize(TesseractEngine engine, BitmapSource source, System.Drawing.Rectangle crop)
    {
        var cropped = new CroppedBitmap(source, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        using var pix = Pix.LoadFromMemory(stream.ToArray());
        return RecognizeBestOrientation(engine,pix);
    }

    private static string[] RecognizeBestOrientation(TesseractEngine engine, Pix pix)
    {
        using var page = engine.Process(pix, PageSegMode.Auto);
        return page.GetText().Replace("\r", "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
    private void ShowTransientSuccess(int lineCount, int charCount)
    {
        _trayIcon!.ShowBalloonTip("Text copied", $"{charCount} characters copied to the clipboard. Paste them into your form.", BalloonIcon.Info);
        StatusLabel.Text = $"Copied {charCount} characters from {lineCount} line{(lineCount == 1 ? "" : "s")} · ready for Ctrl + V.";
        var toast = new ToastWindow($"✓   {charCount} characters copied", "Your text is ready to paste.");
        toast.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        timer.Tick += (_, _) => { timer.Stop(); toast.Close(); if (!_allowClose) Hide(); };
        timer.Start();
    }

    private void SelectionWindow_SelectionCancelled(object? sender, EventArgs e)
    {
        _selectionWindow?.Close();
        _selectionWindow = null;
        _isSelecting = false;
        StatusLabel.Text = "Selection cancelled.";
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => Hide();
    private void ShowSettings() { Show(); WindowState = WindowState.Normal; Activate(); }

    private void ExitApplication()
    {
        _allowClose = true;
        if (_hookSource is not null && _hotkeyRegistered) UnregisterHotKey(_hookSource.Handle, HotkeyId);
        if (_hookSource is not null && _cornerHotkeyRegistered) UnregisterHotKey(_hookSource.Handle, CornerHotkeyId);
        if (_hookSource is not null && _freehandHotkeyRegistered) UnregisterHotKey(_hookSource.Handle, FreehandHotkeyId);
        _operationCancellation?.Cancel();
        _ocrEngine?.Dispose();
        _ocrGate.Dispose();
        _trayIcon?.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
}
