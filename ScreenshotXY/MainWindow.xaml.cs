using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ScreenshotXY.Interop;
using Point = System.Windows.Point;
using SD = System.Drawing; // alias to avoid Point/Rectangle conflicts
using SDI = System.Drawing.Imaging;

namespace ScreenshotXY;

public partial class MainWindow
{
    private bool _dragging;
    private Point _start;
    private Point _end;
    private double _imgW, _imgH;
    private static readonly List<string> HardcodedProcessList = [];
    private const int HotkeyIdF12 = 0xA001;
    private bool _hasSelection;
    private int _selX1, _selY1, _selX2, _selY2;

    private static readonly HashSet<string> SystemLikeNames = new(StringComparer.OrdinalIgnoreCase)
        { "System", "Idle" };

    public MainWindow()
    {
        InitializeComponent();
        SelectionRect.Stroke = new SolidColorBrush(Colors.Lime);
        SelectionRect.Fill = new SolidColorBrush(Color.FromArgb(60, 0, 255, 0));
        UpdateProcessList();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var source = (HwndSource)PresentationSource.FromVisual(this);
        source!.AddHook(WndProc);

        var hwnd = new WindowInteropHelper(this).Handle;
        var vkF12 = (uint)KeyInterop.VirtualKeyFromKey(Key.F12);

        var ok = NativeMethods.RegisterHotKey(hwnd, HotkeyIdF12, NativeMethods.ModControl, vkF12);
        if (!ok)
            MessageBox.Show("F12 is occupied by other app!", "Warning",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmHotkey || wParam.ToInt32() != HotkeyIdF12) return IntPtr.Zero;
        BtnCapture_Click(this, new RoutedEventArgs());
        handled = true;
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.UnregisterHotKey(hwnd, HotkeyIdF12);
        }
        catch
        {
            // ignored
        }

        base.OnClosed(e);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        HardcodedProcessList.Clear();
        UpdateProcessList();
    }

    private void UpdateProcessList()
    {
        HardcodedProcessList.AddRange(GetUserProcessNamesForPick());
        ProcessCombo.ItemsSource = HardcodedProcessList;
        ProcessCombo.Text = HardcodedProcessList.Count != 0 ? HardcodedProcessList[0] : "";
    }

    private static List<string> GetUserProcessNamesForPick()
    {
        var mySession = Process.GetCurrentProcess().SessionId;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // list only processes in the same session and has window
                if (p.SessionId != mySession) continue;
                if (SystemLikeNames.Contains(p.ProcessName)) continue;
                if (p.MainWindowHandle == IntPtr.Zero) continue;

                var n = p.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? p.ProcessName
                    : p.ProcessName + ".exe";

                names.Add(n);
            }
            catch
            {
                // some processes may throw when picked, can ignore
            }
            finally
            {
                try
                {
                    p.Dispose();
                }
                catch
                {
                    // ignored
                }
            }
        }

        var list = names.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private void BtnSaveCrop_Click(object sender, RoutedEventArgs e)
    {
        if (CapturedImage.Source == null)
        {
            MessageBox.Show("no crop", "Warning", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_hasSelection)
        {
            MessageBox.Show("you haven't selected an area", "Warning", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var src = (BitmapSource)CapturedImage.Source;

        // 夾到合法範圍
        var x = Math.Max(0, Math.Min(_selX1, src.PixelWidth - 1));
        var y = Math.Max(0, Math.Min(_selY1, src.PixelHeight - 1));
        var w = Math.Max(1, Math.Min(_selX2 - _selX1, src.PixelWidth - x));
        var h = Math.Max(1, Math.Min(_selY2 - _selY1, src.PixelHeight - y));

        var rect = new Int32Rect(x, y, w, h);
        var crop = new CroppedBitmap(src, rect);

        var dlg = new SaveFileDialog
        {
            Title = "Save Crop",
            Filter = "BMP Image|*.bmp|PNG Image|*.png|JPEG Image|*.jpg;*.jpeg",
            DefaultExt = "bmp",
            FileName = $"crop_{x}_{y}_{w}x{h}"
        };

        if (dlg.ShowDialog(this) != true) return;

        BitmapEncoder encoder = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };

        encoder.Frames.Add(BitmapFrame.Create(crop));
        using var fs = File.Create(dlg.FileName);
        encoder.Save(fs);
    }

    private void BtnCapture_Click(object sender, RoutedEventArgs e)
    {
        _hasSelection = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        var chosen = (ProcessCombo.Text ?? ((ComboBoxItem)ProcessCombo.SelectedItem)?.Content?.ToString())?.Trim();
        if (string.IsNullOrEmpty(chosen)) chosen = "game.exe";
        var procName = chosen.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? chosen[..^4]
            : chosen;

        var processes = Process.GetProcessesByName(procName);
        if (processes.Length == 0)
        {
            MessageBox.Show($"Cannot find process：{chosen}\nMake sure {chosen} is running.", "Alert",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var hwnd = IntPtr.Zero;
        foreach (var p in processes)
        {
            if (p.MainWindowHandle == IntPtr.Zero) continue;
            hwnd = p.MainWindowHandle;
            break;
        }

        if (hwnd == IntPtr.Zero) hwnd = processes[0].MainWindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            MessageBox.Show("Selected process has no window.", "Alert",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var img = ReadScreenImage(hwnd);
            using var bmp = img as SD.Bitmap ?? new SD.Bitmap(img);
            if (bmp == null)
            {
                MessageBox.Show("Capture failed.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var src = BitmapHelpers.ToBitmapSource(bmp, dpiX: 96, dpiY: 96);
            CapturedImage.Source = src;

            _imgW = src.PixelWidth;
            _imgH = src.PixelHeight;
            CapturedImage.Width = _imgW;
            CapturedImage.Height = _imgH;
            Overlay.Width = _imgW;
            Overlay.Height = _imgH;

            _dragging = false;
            SelectionRect.Visibility = Visibility.Collapsed;
            InfoBox.Clear();

            FitWindowToImage(_imgW, _imgH);
            LblSize.Text = $"Screenshot size: {_imgW} × {_imgH}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Exception：\n" + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FitWindowToImage(double imgW, double imgH)
    {
        // eval desired size
        var desiredW = Math.Max(900, imgW + 340 + 40);
        var desiredH = Math.Max(540, imgH + 120);

        // shrink to fit work area
        var wa = SystemParameters.WorkArea; // DIP
        var newW = Math.Min(desiredW, wa.Width - 40);
        var newH = Math.Min(desiredH, wa.Height - 40);

        Width = newW;
        Height = newH;
    }

    private static SD.Bitmap CaptureWindowBitmap(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return null;
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return null;

        var bmp = new SD.Bitmap(w, h, SDI.PixelFormat.Format32bppArgb);
        using var g = SD.Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        bool ok;
        try
        {
            ok = NativeMethods.PrintWindow(hwnd, hdc, 2);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }

        if (!ok)
        {
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new SD.Size(w, h), SD.CopyPixelOperation.SourceCopy);
        }

        return bmp;
    }
    
    private static SD.Image ReadScreenImage(IntPtr hwnd)
    {
        var rect = new NativeMethods.RECT();
        NativeMethods.GetClientRect(hwnd, ref rect);
        var point = new SD.Point();
        NativeMethods.ClientToScreen(hwnd, ref point);
        var r =  new SD.Rectangle(point.X, point.Y, rect.right - rect.left, rect.bottom - rect.top);
        SD.Bitmap memoryImage = new(r.Width, r.Height);
        SD.Size s = new(memoryImage.Width, memoryImage.Height);
        var memoryGraphics = SD.Graphics.FromImage(memoryImage);
        memoryGraphics.CopyFromScreen(r.X, r.Y, 0, 0, s);
        MemoryStream ms = new();
        memoryImage.Save(ms, SDI.ImageFormat.Png);
        var image = SD.Image.FromStream(ms);
        ms.Position = 0;

        return image;
    }

    private void Image_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (CapturedImage.Source == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        _hasSelection = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        _dragging = true;
        _start = ClampToImage(e.GetPosition(CapturedImage));
        _end = _start;
        UpdateSelectionVisual();
    }

    private void Image_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || CapturedImage.Source == null) return;

        _end = ClampToImage(e.GetPosition(CapturedImage));
        UpdateSelectionVisual();
    }

    private void Image_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || CapturedImage.Source == null) return;
        if (e.ChangedButton != MouseButton.Left) return;

        _dragging = false;
        _end = ClampToImage(e.GetPosition(CapturedImage));

        // normalize
        var x1 = Math.Min(_start.X, _end.X);
        var y1 = Math.Min(_start.Y, _end.Y);
        var x2 = Math.Max(_start.X, _end.X);
        var y2 = Math.Max(_start.Y, _end.Y);

        // rounding
        var ix1 = (int)Math.Round(x1);
        var iy1 = (int)Math.Round(y1);
        var ix2 = (int)Math.Round(x2);
        var iy2 = (int)Math.Round(y2);

        var w = Math.Max(0, ix2 - ix1);
        var h = Math.Max(0, iy2 - iy1);

        _selX1 = ix1;
        _selY1 = iy1;
        _selX2 = ix2;
        _selY2 = iy2;
        _hasSelection = (w > 0 && h > 0);
        UpdateSelectionVisual();

        InfoBox.Text =
            $"(X{ix1},Y{iy1})\r\n" +
            $"(X{ix1},Y{iy2})\r\n" +
            $"(X{ix2},Y{iy1})\r\n" +
            $"(X{ix2},Y{iy2})\r\n" +
            $"W:{w}\r\n" +
            $"H:{h}";
    }

    private Point ClampToImage(Point p)
    {
        var x = Math.Max(0, Math.Min(p.X, _imgW));
        var y = Math.Max(0, Math.Min(p.Y, _imgH));
        return new Point(x, y);
    }

    private void UpdateSelectionVisual()
    {
        if (_dragging)
        {
            var x1 = Math.Min(_start.X, _end.X);
            var y1 = Math.Min(_start.Y, _end.Y);
            var x2 = Math.Max(_start.X, _end.X);
            var y2 = Math.Max(_start.Y, _end.Y);

            Canvas.SetLeft(SelectionRect, x1);
            Canvas.SetTop(SelectionRect, y1);
            SelectionRect.Width = Math.Max(0, x2 - x1);
            SelectionRect.Height = Math.Max(0, y2 - y1);
            SelectionRect.Visibility = Visibility.Visible;
        }
        else if (_hasSelection)
        {
            Canvas.SetLeft(SelectionRect, _selX1);
            Canvas.SetTop(SelectionRect, _selY1);
            SelectionRect.Width = Math.Max(0, _selX2 - _selX1);
            SelectionRect.Height = Math.Max(0, _selY2 - _selY1);
            SelectionRect.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionRect.Visibility = Visibility.Collapsed;
        }
    }
}