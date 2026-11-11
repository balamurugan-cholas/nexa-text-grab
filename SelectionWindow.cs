using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace NexaTextGrab;

public sealed class SelectionCompletedEventArgs(BitmapSource background, System.Drawing.Rectangle selection, System.Drawing.PointF[]? corners = null, System.Drawing.PointF[]? freehand = null) : EventArgs
{
    public BitmapSource Background { get; } = background;
    public System.Drawing.Rectangle Selection { get; } = selection;
    public System.Drawing.PointF[]? Corners { get; } = corners;
    public System.Drawing.PointF[]? Freehand { get; } = freehand;
}

public sealed class SelectionWindow : Window
{
    private readonly Canvas _canvas;
    private readonly Image _screenImage;
    private readonly Rectangle _dimLayer;
    private readonly Rectangle _selectionRectangle;
    private readonly Border _hint;
    private readonly BitmapSource _screen;
    private Point _anchor;
    private bool _dragging;
    private readonly bool _cornerMode;
    private readonly bool _freehandMode;
    private readonly List<Point> _corners = new();
    private readonly List<Point> _strokePoints = new();
    private Polyline? _stroke;
    private readonly TextBlock _instruction;
    private int _virtualX;
    private int _virtualY;

    public event EventHandler<SelectionCompletedEventArgs>? SelectionCompleted;
    public event EventHandler? SelectionCancelled;

    public SelectionWindow(BitmapSource screen, int virtualX, int virtualY, int virtualWidth, int virtualHeight, bool cornerMode = false, bool freehandMode = false)
    {
        _cornerMode = cornerMode;
        _freehandMode = freehandMode;
        _screen = screen;
        _virtualX = virtualX;
        _virtualY = virtualY;
        var virtualBounds = new System.Drawing.Rectangle(virtualX, virtualY, virtualWidth, virtualHeight);
        Left = virtualBounds.Left;
        Top = virtualBounds.Top;
        Width = virtualBounds.Width;
        Height = virtualBounds.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        // A fully transparent layered window can be passed through by Windows hit testing.
        // Use a nearly transparent background so the live screen remains unchanged but receives input.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        AllowsTransparency = true;
        Opacity = 1;

        _screenImage = new Image { Visibility = Visibility.Collapsed };
        _dimLayer = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            IsHitTestVisible = false
        };
        _selectionRectangle = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(56, 59, 130, 246)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _hint = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(19, 44, 34)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(15, 9, 15, 9),
            Child = _instruction = new TextBlock { Text = cornerMode ? "CLICK TOP-LEFT, TOP-RIGHT, BOTTOM-RIGHT, BOTTOM-LEFT · ESC TO CANCEL" : freehandMode ? "DRAW A LOOP AROUND THE TEXT · RELEASE TO COPY · ESC TO CANCEL" : "DRAG TO SELECT TEXT     ·     ESC TO CANCEL", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold }
        };
        _canvas = new Canvas { Focusable = true };
        _canvas.Children.Add(_dimLayer);
        _canvas.Children.Add(_selectionRectangle);
        _canvas.Children.Add(_hint);
        Content = _canvas;

        Loaded += (_, _) =>
        {
            _dimLayer.Width = ActualWidth;
            _dimLayer.Height = ActualHeight;
            Canvas.SetLeft(_hint, Math.Max(10, (ActualWidth - 290) / 2));
            Canvas.SetTop(_hint, 20);
            _canvas.Focus();
        };
        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_freehandMode)
        {
            _strokePoints.Clear(); _strokePoints.Add(e.GetPosition(this));
            _stroke = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(42, 202, 140)), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
            _stroke.Points.Add(_strokePoints[0]); _canvas.Children.Add(_stroke); CaptureMouse(); _dragging = true; return;
        }
        if (_cornerMode)
        {
            var pt = e.GetPosition(this);
            _corners.Add(pt);
            var marker = new Ellipse { Width = 12, Height = 12, Fill = Brushes.LimeGreen, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
            _canvas.Children.Add(marker); Canvas.SetLeft(marker, pt.X - 6); Canvas.SetTop(marker, pt.Y - 6);
            if (_corners.Count == 4)
            {
                var sx = _screen.PixelWidth / Math.Max(ActualWidth, 1); var sy = _screen.PixelHeight / Math.Max(ActualHeight, 1);
                var pts = _corners.Select(p => new System.Drawing.PointF((float)(p.X*sx), (float)(p.Y*sy))).ToArray();
                var xs = pts.Select(p => p.X).ToArray(); var ys = pts.Select(p => p.Y).ToArray();
                SelectionCompleted?.Invoke(this, new SelectionCompletedEventArgs(_screen, System.Drawing.Rectangle.Empty, pts));
            }
            else _instruction.Text = $"CLICK CORNER {_corners.Count + 1} OF 4 · ESC TO CANCEL";
            return;
        }
        _anchor = e.GetPosition(this);
        _dragging = true;
        _selectionRectangle.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateSelection(e.GetPosition(this));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging && _freehandMode && _stroke is not null)
        {
            var pt=e.GetPosition(this); if (_strokePoints.Count==0 || (pt-_strokePoints[^1]).Length>3) { _strokePoints.Add(pt); _stroke.Points.Add(pt); }
        }
        else if (_dragging) UpdateSelection(e.GetPosition(this));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var end = e.GetPosition(this);
        if (_freehandMode)
        {
            if (_strokePoints.Count < 8) { SelectionCancelled?.Invoke(this, EventArgs.Empty); return; }
            var sx=_screen.PixelWidth/Math.Max(ActualWidth,1); var sy=_screen.PixelHeight/Math.Max(ActualHeight,1);
            var pts=_strokePoints.Select(p=>new System.Drawing.PointF((float)(p.X*sx),(float)(p.Y*sy))).ToArray();
            SelectionCompleted?.Invoke(this,new SelectionCompletedEventArgs(_screen,System.Drawing.Rectangle.Empty,null,pts)); return;
        }
        var widthScale = _screen.PixelWidth / Math.Max(ActualWidth, 1);
        var heightScale = _screen.PixelHeight / Math.Max(ActualHeight, 1);
        var x = (int)Math.Round(Math.Min(_anchor.X, end.X) * widthScale);
        var y = (int)Math.Round(Math.Min(_anchor.Y, end.Y) * heightScale);
        var w = (int)Math.Round(Math.Abs(end.X - _anchor.X) * widthScale);
        var h = (int)Math.Round(Math.Abs(end.Y - _anchor.Y) * heightScale);
        SelectionCompleted?.Invoke(this, new SelectionCompletedEventArgs(_screen, new System.Drawing.Rectangle(x, y, w, h)));
    }

    private void UpdateSelection(Point current)
    {
        var left = Math.Min(_anchor.X, current.X);
        var top = Math.Min(_anchor.Y, current.Y);
        _selectionRectangle.Width = Math.Abs(current.X - _anchor.X);
        _selectionRectangle.Height = Math.Abs(current.Y - _anchor.Y);
        Canvas.SetLeft(_selectionRectangle, left);
        Canvas.SetTop(_selectionRectangle, top);

        var widthScale = _screen.PixelWidth / Math.Max(ActualWidth, 1);
        var heightScale = _screen.PixelHeight / Math.Max(ActualHeight, 1);
        var x = (int)Math.Round(left * widthScale);
        var y = (int)Math.Round(top * heightScale);
        var width = (int)Math.Round(_selectionRectangle.Width * widthScale);
        var height = (int)Math.Round(_selectionRectangle.Height * heightScale);
        x = Math.Clamp(x, 0, _screen.PixelWidth);
        y = Math.Clamp(y, 0, _screen.PixelHeight);
        width = Math.Clamp(width, 0, _screen.PixelWidth - x);
        height = Math.Clamp(height, 0, _screen.PixelHeight - y);

    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            SelectionCancelled?.Invoke(this, EventArgs.Empty);
        }
    }
}
