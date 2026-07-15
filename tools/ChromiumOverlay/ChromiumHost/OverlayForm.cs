using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using CefSharp;
using CefSharp.OffScreen;

namespace ChromiumOverlay;

/// <summary>
/// Click-through per-pixel alpha overlay over the game client.
/// Uses CEF when available; otherwise GDI "Hello World" so the overlay is still visible.
/// </summary>
internal sealed class OverlayForm : Form
{
    private readonly int _gamePid;
    private readonly bool _enableCef;
    private readonly ChromiumWebBrowser? _browser;
    private readonly System.Windows.Forms.Timer _trackTimer;
    private readonly System.Windows.Forms.Timer _stateTimer;
    private readonly System.Windows.Forms.Timer _presentTimer;
    private readonly object _presentLock = new();
    private GameStateChannel? _channel;
    private int _lastSeq = -1;
    private IntPtr _gameHwnd = IntPtr.Zero;
    private int _overlayX;
    private int _overlayY;
    private int _overlayW = 1024;
    private int _overlayH = 768;
    private bool _browserSized;
    private bool _coveringGame;
    private int _presentCount;
    private readonly DateTime _coverAfterUtc = DateTime.UtcNow.AddMilliseconds(500);
    private bool _presentBusy;
    private string _statusLine = "waiting for bridge…";
    private int _noHwndTicks;

    public OverlayForm(int gamePid, bool enableCef)
    {
        _gamePid = gamePid;
        _enableCef = enableCef;
        HostLog.Write($"OverlayForm create pid={gamePid} enableCef={enableCef}");

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(64, 64);
        SetStyle(ControlStyles.Selectable, false);

        if (_enableCef)
        {
            var webRoot = Path.Combine(AppContext.BaseDirectory, "web");
            var index = Path.Combine(webRoot, "index.html");
            if (!File.Exists(index))
            {
                HostLog.Write("page missing, falling back to GDI: " + index);
                _enableCef = false;
            }
            else
            {
                var browserSettings = new BrowserSettings
                {
                    WindowlessFrameRate = 30,
                    BackgroundColor = Cef.ColorSetARGB(0, 0, 0, 0),
                };
                var url = new Uri(index).AbsoluteUri;
                HostLog.Write("loading " + url);
                _browser = new ChromiumWebBrowser(url, browserSettings: browserSettings, automaticallyCreateBrowser: true)
                {
                    Size = new Size(_overlayW, _overlayH),
                };
                _browser.FrameLoadEnd += OnFrameLoadEnd;
                _browser.LoadError += (_, e) => HostLog.Write($"LoadError: {e.ErrorCode} {e.ErrorText}");
                _browser.BrowserInitialized += (_, _) =>
                {
                    HostLog.Write("BrowserInitialized");
                    try
                    {
                        _browser.GetBrowser()?.GetHost()?.WasResized();
                        _browser.GetBrowser()?.GetHost()?.Invalidate(PaintElementType.View);
                    }
                    catch (Exception ex)
                    {
                        HostLog.Write("invalidate failed: " + ex.Message);
                    }
                };
            }
        }

        if (!_enableCef)
            HostLog.Write("Using GDI fallback overlay (CEF disabled/unavailable)");

        _trackTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _trackTimer.Tick += (_, _) => TrackGameWindow();
        _trackTimer.Start();

        _stateTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _stateTimer.Tick += (_, _) => PushGameState();
        _stateTimer.Start();

        _presentTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _presentTimer.Tick += async (_, _) => await PresentTickAsync();
        _presentTimer.Start();

        Load += (_, _) =>
        {
            ApplyClickThroughStyles();
            HostLog.Write("Form Load handle=" + Handle);
        };
        Shown += (_, _) => TrackGameWindow();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_LAYERED
                          | NativeMethods.WS_EX_TRANSPARENT
                          | NativeMethods.WS_EX_TOOLWINDOW
                          | NativeMethods.WS_EX_NOACTIVATE
                          | NativeMethods.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
            return;
        }
        if (m.Msg == NativeMethods.WM_NCHITTEST)
        {
            m.Result = (IntPtr)NativeMethods.HTTRANSPARENT;
            return;
        }
        base.WndProc(ref m);
    }

    private void ApplyClickThroughStyles()
    {
        if (!IsHandleCreated)
            return;
        var ex = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_LAYERED
              | NativeMethods.WS_EX_TRANSPARENT
              | NativeMethods.WS_EX_TOOLWINDOW
              | NativeMethods.WS_EX_NOACTIVATE
              | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, ex);
    }

    private void OnFrameLoadEnd(object? sender, FrameLoadEndEventArgs e)
    {
        if (_browser is null || !e.Frame.IsMain)
            return;
        HostLog.Write($"FrameLoadEnd {e.HttpStatusCode} {e.Url}");
        _ = e.Frame.EvaluateScriptAsync("""
            document.documentElement.style.background='transparent';
            document.body.style.background='transparent';
            """);
    }

    private async Task PresentTickAsync()
    {
        if (!_coveringGame || _presentBusy || IsDisposed || !IsHandleCreated)
            return;
        _presentBusy = true;
        try
        {
            if (_enableCef && _browser is { IsBrowserInitialized: true })
            {
                try
                {
                    var png = await _browser.CaptureScreenshotAsync().ConfigureAwait(true);
                    if (png is { Length: > 0 })
                    {
                        using var ms = new MemoryStream(png);
                        using var src = new Bitmap(ms);
                        using var premul = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(premul))
                        {
                            g.Clear(Color.Transparent);
                            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                            g.DrawImageUnscaled(src, 0, 0);
                        }
                        PresentBitmap(premul);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (_presentCount < 8)
                        HostLog.Write("CEF capture failed: " + ex.Message);
                }
            }

            // Always-available path: draw Hello World ourselves into a transparent layered buffer.
            PresentGdiHelloWorld();
        }
        finally
        {
            _presentBusy = false;
        }
    }

    private void PresentGdiHelloWorld()
    {
        var w = Math.Max(1, _overlayW);
        var h = Math.Max(1, _overlayH);
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Segoe UI", 72, FontStyle.Bold, GraphicsUnit.Pixel);
            using var small = new Font("Consolas", 16, FontStyle.Regular, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.White);
            using var green = new SolidBrush(Color.FromArgb(255, 160, 255, 180));
            using var shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
            g.DrawString("Hello World", font, shadow, 28, 28);
            g.DrawString("Hello World", font, white, 24, 24);
            g.DrawString(_statusLine, small, green, 28, 110);
            var mode = _enableCef ? "CEF+GDI" : "GDI-only (CEF init failed)";
            g.DrawString(mode, small, green, 28, 132);
        }
        PresentBitmap(bmp);
    }

    private void PresentBitmap(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        // Lock as 32bppArgb then treat as BGRA for ULW; for PArgb lock as PArgb.
        var fmt = bmp.PixelFormat == PixelFormat.Format32bppPArgb
            ? PixelFormat.Format32bppPArgb
            : PixelFormat.Format32bppArgb;
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, fmt);
        try
        {
            PresentBgra(data.Scan0, bmp.Width, bmp.Height, data.Stride);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private void PresentBgra(IntPtr bgra, int width, int height, int stride)
    {
        lock (_presentLock)
        {
            if (IsDisposed || !IsHandleCreated || width <= 0 || height <= 0 || !_coveringGame)
                return;

            var screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                return;
            var memDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                return;
            }

            var packed = width * 4;
            var bmi = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = (uint)NativeMethods.BI_RGB,
                    biSizeImage = (uint)(packed * height),
                },
            };

            var dib = NativeMethods.CreateDIBSection(
                memDc, ref bmi, (uint)NativeMethods.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                return;
            }

            unsafe
            {
                var dst = (byte*)bits;
                var src = (byte*)bgra;
                var abs = Math.Abs(stride);
                for (var row = 0; row < height; row++)
                {
                    var srcRow = stride >= 0 ? src + row * stride : src + (height - 1 - row) * abs;
                    Buffer.MemoryCopy(srcRow, dst + row * packed, packed, packed);
                }
            }

            var old = NativeMethods.SelectObject(memDc, dib);
            var dest = new NativeMethods.POINT { X = _overlayX, Y = _overlayY };
            var size = new NativeMethods.SIZE { cx = width, cy = height };
            var srcPt = new NativeMethods.POINT { X = 0, Y = 0 };
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };

            var ok = NativeMethods.UpdateLayeredWindow(
                Handle, screenDc, ref dest, ref size, memDc, ref srcPt, 0, ref blend, NativeMethods.ULW_ALPHA);

            _presentCount++;
            if (!ok || _presentCount <= 5 || _presentCount % 60 == 0)
            {
                HostLog.Write(
                    $"ULW ok={ok} win32={Marshal.GetLastWin32Error()} #{_presentCount} " +
                    $"{width}x{height} @ {_overlayX},{_overlayY} hwnd=0x{_gameHwnd.ToInt64():X}");
            }

            NativeMethods.SelectObject(memDc, old);
            NativeMethods.DeleteObject(dib);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void TrackGameWindow()
    {
        try
        {
            if (_gameHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_gameHwnd))
            {
                _gameHwnd = FindMainWindow(_gamePid);
                if (_gameHwnd != IntPtr.Zero)
                    HostLog.Write($"game hwnd=0x{_gameHwnd.ToInt64():X}");
            }

            if (_gameHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_gameHwnd))
            {
                _noHwndTicks++;
                try
                {
                    using var p = Process.GetProcessById(_gamePid);
                    p.Refresh();
                    if (p.HasExited)
                    {
                        // Require a few consecutive observations — startup can race.
                        if (_noHwndTicks > 30)
                        {
                            HostLog.Write("game exited (confirmed)");
                            Close();
                        }
                    }
                    else if (_noHwndTicks == 1 || _noHwndTicks % 30 == 0)
                    {
                        HostLog.Write($"waiting for game hwnd (pid={_gamePid} alive, ticks={_noHwndTicks})");
                    }
                }
                catch (Exception ex)
                {
                    if (_noHwndTicks > 60)
                    {
                        HostLog.Write("game process missing after wait: " + ex.Message);
                        Close();
                    }
                    else if (_noHwndTicks == 1 || _noHwndTicks % 30 == 0)
                    {
                        HostLog.Write($"game pid query failed ({ex.GetType().Name}), still waiting…");
                    }
                }
                return;
            }

            _noHwndTicks = 0;

            if (!NativeMethods.GetClientRect(_gameHwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
                return;

            var pt = new NativeMethods.POINT { X = 0, Y = 0 };
            if (!NativeMethods.ClientToScreen(_gameHwnd, ref pt))
                return;

            if (DateTime.UtcNow < _coverAfterUtc)
                return;

            var sizeChanged = rect.Width != _overlayW || rect.Height != _overlayH;
            _overlayX = pt.X;
            _overlayY = pt.Y;
            _overlayW = Math.Max(320, rect.Width);
            _overlayH = Math.Max(200, rect.Height);

            if (!_coveringGame)
            {
                _coveringGame = true;
                HostLog.Write($"COVER {_overlayW}x{_overlayH} @ {_overlayX},{_overlayY}");
                // Exclusive fullscreen D3D typically will not show layered TOPMOST windows on top.
                try
                {
                    var screen = Screen.FromHandle(_gameHwnd);
                    var b = screen.Bounds;
                    if (_overlayW >= b.Width - 2 && _overlayH >= b.Height - 2)
                    {
                        HostLog.Write(
                            "WARNING: game client fills the monitor. Exclusive fullscreen (MODE_WINDOWED=0) " +
                            "usually HIDES layered CEF overlays even when ULW succeeds. Set MODE_WINDOWED=1 in exe\\vog.ini.");
                    }
                    HostLog.Write($"monitor={screen.DeviceName} bounds={b.Width}x{b.Height}+{b.X},{b.Y}");
                }
                catch (Exception ex)
                {
                    HostLog.Write("screen probe: " + ex.Message);
                }
            }

            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOPMOST,
                _overlayX,
                _overlayY,
                _overlayW,
                _overlayH,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            ApplyClickThroughStyles();

            if (_browser is not null && (sizeChanged || !_browserSized))
            {
                _browserSized = true;
                _browser.Size = new Size(_overlayW, _overlayH);
                try
                {
                    _browser.GetBrowser()?.GetHost()?.WasResized();
                    _browser.GetBrowser()?.GetHost()?.Invalidate(PaintElementType.View);
                    HostLog.Write($"browser size {_overlayW}x{_overlayH}");
                }
                catch (Exception ex)
                {
                    HostLog.Write("resize: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            HostLog.Write("Track: " + ex.Message);
        }
    }

    private void PushGameState()
    {
        try
        {
            _channel ??= TryOpenChannel();
            if (_channel is null || !_channel.IsValid)
                return;
            var seq = _channel.ReadSeq();
            if (seq == _lastSeq)
                return;
            if (!_channel.TryReadJson(out var json))
                return;
            _lastSeq = seq;
            _statusLine = json.Length > 120 ? json[..120] + "…" : json;

            if (_browser is { IsBrowserInitialized: true })
                _ = _browser.EvaluateScriptAsync($"window.__applyGameState({json});");
        }
        catch
        {
            _channel?.Dispose();
            _channel = null;
        }
    }

    private static GameStateChannel? TryOpenChannel()
    {
        try { return GameStateChannel.Open(); }
        catch
        {
            try { return GameStateChannel.Create(); }
            catch { return null; }
        }
    }

    private static IntPtr FindMainWindow(int pid)
    {
        var hwnd = NativeMethods.FindBestWindowForProcess(pid);
        if (hwnd != IntPtr.Zero)
            return hwnd;
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Refresh();
            return p.MainWindowHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            HostLog.Write($"Dispose presents={_presentCount}");
            _trackTimer.Dispose();
            _stateTimer.Dispose();
            _presentTimer.Dispose();
            _channel?.Dispose();
            _browser?.Dispose();
        }
        base.Dispose(disposing);
    }
}
