using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Shell;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Media.Animation;
using WpfPath = System.Windows.Shapes.Path;
using IoPath = System.IO.Path;

namespace YCB;

public partial class MainWindow : Window
{
    private readonly List<BrowserTab> _tabs = new();
    private int _activeTabIndex = -1;
    private bool _isDarkMode = true;
    private bool _copilotVisible = false;
    private bool _isFullscreen = false;
    // Rapid-close: while mouse is over the tab strip, don't resize tabs (Chrome behaviour)
    private bool _tabStripMouseOver = false;
    private bool _tabsClosedWhileOver = false;
    private HashSet<string> _adBlockDisabledSites = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _userDataFolder;
    private readonly string _incognitoUserDataFolder;
    private readonly string _settingsPath;
    // When "Start fresh" is chosen, track which tab was the startup tab so its URL isn't
    // saved as a "last tab" (prevents the restore prompt appearing again next session).
    private BrowserTab? _freshStartTab = null;
    private string?     _freshStartTabInitialUrl = null;
    private readonly string _historyPath;
    private readonly string _downloadsPath;
    private readonly string _bookmarksPath;
    private readonly string _passwordsPath;
    private readonly string _permissionsPath;
    private Settings _settings = new();
    private string _searchEngine = "google";
    private double _zoomFactor = 1.0;
    private readonly bool _isIncognito;
    private readonly List<ChatMessage> _chatHistory = new();
    private static readonly bool _aiEnabled =
        (Registry.GetValue(@"HKEY_LOCAL_MACHINE\Software\YCB", "AIOption", "on") as string ?? "on") != "off";
    private Process? _copilotProcess;
    private TextBlock? _currentResponseBlock;
    private string? _startupUrl;
    private readonly Dictionary<WebView2, string> _autofillShownForTab = new();
    private readonly Dictionary<WebView2, DateTime> _navStartTimes = new();
    private CoreWebView2Environment? _webViewEnvironment;
    private CoreWebView2Environment? _incognitoWebViewEnvironment;
    private string? _attachedImagePath;
    private double _savedLeft, _savedTop, _savedWidth, _savedHeight;
    private WindowState _savedWindowState;
    private bool _manuallyMaximized = false;
    private DateTime _historyClearedAt = DateTime.MinValue;
    private CancellationTokenSource? _suggestCts;
    private System.Windows.Threading.DispatcherTimer? _suggestCloseTimer;
    private bool _userEditingUrl = false;
    private readonly Dictionary<WebView2, string> _lastRealPageUrl = new(); // for learning
    
    public MainWindow() : this(false, null) { }

    public MainWindow(bool incognito) : this(incognito, null) { }

    public MainWindow(string? startupUrl) : this(false, startupUrl) { }

    public MainWindow(bool incognito, string? startupUrl = null)
    {
        InitializeComponent();
        _isIncognito = incognito;
        _startupUrl  = startupUrl;
        
        _userDataFolder = IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YCB-Browser");
        _incognitoUserDataFolder = IoPath.Combine(IoPath.GetTempPath(), "YCB-Incognito-" + Guid.NewGuid().ToString("N"));
        _settingsPath = IoPath.Combine(_userDataFolder, "settings.json");
        _historyPath = IoPath.Combine(_userDataFolder, "history.json");
        _downloadsPath = IoPath.Combine(_userDataFolder, "downloads.json");
        _bookmarksPath = IoPath.Combine(_userDataFolder, "bookmarks.json");
        _passwordsPath = IoPath.Combine(_userDataFolder, "passwords.json");
        _permissionsPath = IoPath.Combine(_userDataFolder, "permissions.json");
        
        Directory.CreateDirectory(_userDataFolder);
        EnsureRendererExtracted();
        if (_isIncognito)
        {
            Directory.CreateDirectory(_incognitoUserDataFolder);
        }
        
        LoadSettings();
        
        Loaded += MainWindow_Loaded;
        LocationChanged += (_, _) => { if (!_isFullscreen && Top < 0) Top = 0; };
        StateChanged += MainWindow_StateChanged;
        KeyDown += MainWindow_KeyDown;
        SizeChanged += (_, _) => UpdateTabWidths();
        // Track mouse over the ENTIRE tab strip — only resize on leave if tabs were actually closed
        Loaded += (_, _) =>
        {
            TabStrip.MouseEnter += (s, e) => _tabStripMouseOver = true;
            TabStrip.MouseLeave += (s, e) =>
            {
                _tabStripMouseOver = false;
                if (_tabsClosedWhileOver)
                {
                    _tabsClosedWhileOver = false;
                    UpdateTabWidths(); // resize now that cursor has left after rapid-close
                }
            };
        };
        if (_isIncognito)
        {
            Closed += IncognitoWindow_Closed;
        }
    }
    
    // Returns the renderer folder path (in AppData, extracted from embedded resources)
    private string RendererPath => IoPath.Combine(_userDataFolder, "renderer");

    private void EnsureRendererExtracted()
    {
        var outDir = RendererPath;
        Directory.CreateDirectory(outDir);
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("renderer/")) continue;
            // name like "renderer/newtab.html" or "renderer/subdir/file.js"
            var relative = name.Substring("renderer/".Length).Replace('/', IoPath.DirectorySeparatorChar);
            var destPath = IoPath.Combine(outDir, relative);
            Directory.CreateDirectory(IoPath.GetDirectoryName(destPath)!);
            using var src = asm.GetManifestResourceStream(name)!;
            // Always overwrite so updates ship on next run
            using var dst = File.Open(destPath, FileMode.Create, FileAccess.Write);
            src.CopyTo(dst);
        }
    }

    private void IncognitoWindow_Closed(object? sender, EventArgs e)
    {
        // Clean up incognito temp data
        try
        {
            if (Directory.Exists(_incognitoUserDataFolder))
            {
                Directory.Delete(_incognitoUserDataFolder, true);
            }
        }
        catch { }
    }
    
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {

        // Setup incognito mode
        if (_isIncognito)
        {
            Title = "YCB (Incognito)";
            IncognitoPill.Visibility = Visibility.Visible;
        }
        
        ApplyTheme();
        ApplyAllSettings();
        ApplyWindowPositionFromSettings();

        // Hide all AI UI if AI was disabled during install
        if (!_aiEnabled)
        {
            CopilotBtn.Visibility = Visibility.Collapsed;
            CopilotSidebar.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
        }

        // Restore tabs from last session or create new tab (incognito always starts fresh)
        if (!_isIncognito && _settings.StartupMode == "continue" && _settings.LastTabs?.Count > 0)
        {
            var tabsToRestore = _settings.LastTabs.ToList();
            foreach (var url in tabsToRestore)
                await CreateTab(url);
            _settings.LastTabs = null;
        }
        else if (!_isIncognito && _settings.StartupMode == "ask" && _settings.LastTabs?.Count > 0)
        {
            await CreateTab(_settings.HomePage ?? "ycb://newtab");
            await Task.Delay(300);
            ShowRestorePrompt();
        }
        else
        {
            await CreateTab(_settings.HomePage ?? "ycb://newtab");
        }

        // If launched with a URL argument, open it in a new tab
        if (!string.IsNullOrEmpty(_startupUrl))
        {
            await CreateTab(_startupUrl);
        }

        // Show guide on first launch
        if (!_isIncognito && !_settings.HasSeenGuide)
        {
            await CreateTab("ycb://guide");
            _settings.HasSeenGuide = true;
            SaveSettings();
        }
    }

    // Called by App.xaml.cs pipe server when another instance sends a URL
    public async void OpenUrl(string url)
    {
        // Normalize bare URLs (e.g. "google.com" → "https://google.com")
        if (!string.IsNullOrWhiteSpace(url) &&
            !url.StartsWith("ycb://") &&
            !url.StartsWith("http://") &&
            !url.StartsWith("https://") &&
            !url.StartsWith("file://") &&
            !url.StartsWith("about:"))
        {
            url = "https://" + url;
        }

        BringToFront();
        try { await CreateTab(url); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenUrl] Failed to open '{url}': {ex.Message}");
        }
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
    
    private void ApplyAllSettings()
    {
        // Apply bookmarks bar visibility
        if (_settings.BookmarksBarVisible)
        {
            BookmarksBar.Visibility = Visibility.Visible;
            BookmarksBarRow.Height = new GridLength(32);
            LoadBookmarksBar();
        }
    }
    
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            // System maximize (Aero snap, Win+Up) — cancel it and use manual maximize
            WindowState = WindowState.Normal;
            ManualMaximize();
        }
        else if (!_isFullscreen && !_manuallyMaximized)
        {
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight         = 0,
                ResizeBorderThickness = new Thickness(5),
                GlassFrameThickness   = new Thickness(-1),
                CornerRadius          = new CornerRadius(0)
            });
            ShowMaximizeIcon();
        }
    }

    private void ShowRestoreIcon()
    {
        var iconColor = _isDarkMode ? "#9aa0a6" : "#5f6368";
        var canvas = new Canvas { Width = 10, Height = 10 };
        var rect1 = new Rectangle { Width = 7, Height = 7, Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconColor)!), StrokeThickness = 1.2, Fill = Brushes.Transparent };
        var rect2 = new Rectangle { Width = 7, Height = 7, Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconColor)!), StrokeThickness = 1.2, Fill = Brushes.Transparent };
        Canvas.SetLeft(rect1, 0); Canvas.SetTop(rect1, 3);
        Canvas.SetLeft(rect2, 3); Canvas.SetTop(rect2, 0);
        canvas.Children.Add(rect1);
        canvas.Children.Add(rect2);
        MaxRestoreBtn.Content = canvas;
    }

    private void ShowMaximizeIcon()
    {
        var iconColor = _isDarkMode ? "#9aa0a6" : "#5f6368";
        MaxRestoreBtn.Content = new Rectangle
        {
            Width = 8.5, Height = 8.5,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconColor)!),
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent
        };
    }
    
    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Keyboard shortcuts
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case Key.N:
                    // Open new incognito window
                    OpenIncognitoWindow();
                    e.Handled = true;
                    break;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.T:
                    _ = CreateTab();
                    e.Handled = true;
                    break;
                case Key.N:
                    // Open new window
                    OpenNewWindow();
                    e.Handled = true;
                    break;
                case Key.W:
                    if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                        CloseTab(_activeTabIndex);
                    e.Handled = true;
                    break;
                case Key.Tab:
                    if (_tabs.Count > 1)
                    {
                        var next = (_activeTabIndex + 1) % _tabs.Count;
                        SwitchToTab(next);
                    }
                    e.Handled = true;
                    break;
                case Key.L:
                    UrlBox.Focus();
                    UrlBox.SelectAll();
                    e.Handled = true;
                    break;
                case Key.D:
                    // Quick Download keyboard shortcut removed (feature is always-on)
                    break;
                case Key.H:
                    _ = CreateTab("ycb://history");
                    e.Handled = true;
                    break;
                case Key.J:
                    _ = CreateTab("ycb://downloads");
                    e.Handled = true;
                    break;
                case Key.OemPlus:
                case Key.Add:
                    ZoomIn_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomOut_Click(sender, e);
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.F5)
        {
            Refresh_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }
    
    private void OpenNewWindow()
    {
        ErrorReporter.Track("NewWin", new() { ["incognito"] = false });
        var newWindow = new MainWindow(false);
        newWindow.Show();
    }
    
    private void OpenIncognitoWindow()
    {
        ErrorReporter.Track("NewWin", new() { ["incognito"] = true });
        var incognitoWindow = new MainWindow(true);
        incognitoWindow.Show();
    }
    
    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        var hwnd = new WindowInteropHelper(this).Handle;

        if (_isFullscreen)
        {
            _savedWindowState = WindowState;
            _savedLeft = Left; _savedTop = Top; _savedWidth = Width; _savedHeight = Height;

            // Hide browser UI
            TabStrip.Visibility = Visibility.Collapsed;
            Toolbar.Visibility  = Visibility.Collapsed;
            MainGrid.RowDefinitions[0].Height = new GridLength(0);
            MainGrid.RowDefinitions[1].Height = new GridLength(0);

            // Capture exact monitor rect in physical pixels BEFORE style changes
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref mi);
            int fsX = mi.rcMonitor.Left,  fsY = mi.rcMonitor.Top;
            int fsW = mi.rcMonitor.Right  - mi.rcMonitor.Left;
            int fsH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

            // Strip chrome and borders
            WindowChrome.SetWindowChrome(this, null);
            if (_manuallyMaximized) _manuallyMaximized = false;
            ResizeMode  = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                SetWindowPos(hwnd, HWND_TOPMOST, fsX, fsY, fsW, fsH, SWP_FRAMECHANGED);
            }));
        }
        else
        {
            // Show browser UI
            TabStrip.Visibility = Visibility.Visible;
            Toolbar.Visibility  = Visibility.Visible;
            MainGrid.RowDefinitions[0].Height = new GridLength(36);
            MainGrid.RowDefinitions[1].Height = new GridLength(46);

            // Restore chrome
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight         = 0,
                ResizeBorderThickness = new Thickness(5),
                GlassFrameThickness   = new Thickness(-1),
                CornerRadius          = new CornerRadius(0)
            });
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode  = ResizeMode.CanResize;

            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_FRAMECHANGED | 0x0001 | 0x0002);

            if (_savedWindowState == WindowState.Maximized || _manuallyMaximized)
            {
                ManualMaximize();
            }
            else
            {
                Left = _savedLeft; Top = _savedTop;
                Width = _savedWidth; Height = _savedHeight;
            }
        }
    }
    
    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch { }
        
        _isDarkMode = _settings.DarkMode;
        _searchEngine = _settings.SearchEngine ?? "google";
        ErrorReporter.IsEnabled = _settings.TelemetryEnabled;
        _adBlockDisabledSites = new HashSet<string>(
            _settings.AdBlockerDisabledSites ?? [], StringComparer.OrdinalIgnoreCase);
        _historyClearedAt = _settings.HistoryClearedAt ?? DateTime.MinValue;
    }
    
    private void SaveSettings()
    {
        try
        {
            _settings.DarkMode = _isDarkMode;
            // Only save actual websites — no ycb:// internal pages.
            // Also exclude the startup tab if "Start fresh" was chosen and the user hasn't
            // navigated it away from its original URL (avoids a re-prompt next session).
            _settings.LastTabs = _tabs
                .Where(t => !string.IsNullOrEmpty(t.Url) &&
                            (t.Url!.StartsWith("http://") || t.Url.StartsWith("https://")) &&
                            !(t == _freshStartTab && t.Url == _freshStartTabInitialUrl))
                .Select(t => t.Url!)
                .ToList();
            // Save window bounds/state (use RestoreBounds to get normal geometry)
            try
            {
                bool isMax = _manuallyMaximized || WindowState == WindowState.Maximized;
                Rect bounds = isMax && _savedWidth > 0
                    ? new Rect(_savedLeft, _savedTop, _savedWidth, _savedHeight)
                    : RestoreBounds;
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    _settings.WindowLeft   = bounds.Left;
                    _settings.WindowTop    = bounds.Top;
                    _settings.WindowWidth  = bounds.Width;
                    _settings.WindowHeight = bounds.Height;
                }
                _settings.WindowState = isMax ? "Maximized" : "Normal";
            }
            catch { }
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }

    private void ApplyWindowPositionFromSettings()
    {
        try
        {
            if (_settings.WindowWidth.HasValue && _settings.WindowHeight.HasValue)
            {
                var work = SystemParameters.WorkArea;
                double w = _settings.WindowWidth.Value;
                double h = _settings.WindowHeight.Value;
                double left = _settings.WindowLeft ?? (work.Left + (work.Width - w) / 2);
                double top = _settings.WindowTop ?? (work.Top + (work.Height - h) / 2);
                // Clamp to work area
                if (left + w > work.Right) left = work.Right - w;
                if (top + h > work.Bottom) top = work.Bottom - h;
                if (left < work.Left) left = work.Left;
                if (top < work.Top) top = work.Top;
                Width = Math.Max(300, Math.Min(w, work.Width));
                Height = Math.Max(200, Math.Min(h, work.Height));
                Left = left;
                Top = top;
            }
            if (_settings.WindowState == "Maximized")
            {
                Dispatcher.InvokeAsync(() => ManualMaximize(), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        catch { }
    }

    private bool IsRestorableUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (url.StartsWith("http://") || url.StartsWith("https://")) return true;
        if (url.StartsWith("ycb://"))
        {
            var page = url.Substring("ycb://".Length).Split(new[] {'/', '?'}, StringSplitOptions.RemoveEmptyEntries)[0];
            var disallowed = new[] { "settings", "guide", "passwords" };
            return !disallowed.Contains(page);
        }
        return false;
    }

    private async System.Threading.Tasks.Task CreateTab(string url = "ycb://newtab")
    {
        // Handle internal URLs
        var displayUrl = url;
        if (url.StartsWith("ycb://"))
        {
            displayUrl = url;
        }
        
        var webView = new WebView2();
        webView.Visibility = Visibility.Collapsed;
        
        // Create tab button with Chrome-like structure
        var tabButton = CreateTabButton(_tabs.Count);

        // Extract tab UI element references for compact-mode management
        Image? tabFavicon = null;
        Button? tabCloseBtn = null;
        TextBlock? tabTitle = null;
        if (tabButton.Content is Grid _contentGrid)
        {
            tabFavicon = _contentGrid.Children.OfType<Image>().FirstOrDefault();
            tabCloseBtn = _contentGrid.Children.OfType<Button>().FirstOrDefault();
            tabTitle = _contentGrid.Children.OfType<TextBlock>().FirstOrDefault();
        }

        TabsPanel.Children.Add(tabButton);
        WebViewContainer.Children.Add(webView);
        
        // Recompute tab widths now that count changed
        Dispatcher.InvokeAsync(UpdateTabWidths, System.Windows.Threading.DispatcherPriority.Loaded);
        
        var tab = new BrowserTab
        {
            WebView = webView,
            TabButton = tabButton,
            Url = url,
            Title = "New Tab",
            TabFavicon = tabFavicon,
            TabCloseBtn = tabCloseBtn,
            TabTitle = tabTitle,
        };
        _tabs.Add(tab);
        ErrorReporter.Track("TabOpen", new() { ["tabs"] = _tabs.Count });
        
        // Initialize WebView2 with appropriate data folder — reuse shared environment
        var dataFolder = _isIncognito ? _incognitoUserDataFolder : _userDataFolder;
        if (_isIncognito)
        {
            _incognitoWebViewEnvironment ??= await CreateWebViewEnvironment(dataFolder);
            await webView.EnsureCoreWebView2Async(_incognitoWebViewEnvironment);
        }
        else
        {
            _webViewEnvironment ??= await CreateWebViewEnvironment(dataFolder);
            await webView.EnsureCoreWebView2Async(_webViewEnvironment);
        }
        
        // Set default background color based on theme
        webView.DefaultBackgroundColor = _isDarkMode 
            ? System.Drawing.Color.FromArgb(255, 32, 33, 36)  // #202124
            : System.Drawing.Color.FromArgb(255, 255, 255, 255);  // white

        // Register network-level ad blocker (checks _settings.AdBlockerEnabled at request time)
        SetupAdBlockerNetwork(webView);

        // Inject early-running tracker nullifier + ad blocker script at document creation
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(GetAdBlockerEarlyScript());
        
        // Setup event handlers
        SetupWebViewEvents(webView, _tabs.Count - 1);
        
        // Navigate to URL
        if (url.StartsWith("ycb://"))
        {
            NavigateToInternalPage(webView, url);
        }
        else
        {
            webView.CoreWebView2.Navigate(url);
        }
        
        // Switch to new tab
        SwitchToTab(_tabs.Count - 1);
        
        // Focus URL bar for new tabs
        if (url == "https://www.google.com" || url.StartsWith("ycb://newtab"))
        {
            UrlBox.Focus();
            UrlBox.SelectAll();
        }
    }

    private void UpdateTabWidths()
    {
        if (_tabs.Count == 0) return;
        // WinControls = 3 × 46px = 138px (fixed by WinBtnStyle).
        // NewTabBtn = 26px + 4px margin = 30px. DragArea minimum = 60px.
        // TabStrip spans the full window width reliably once rendered.
        const double kWinCtrl  = 138; // always fixed
        const double kNewTab   = 30;  // always fixed
        const double kDragMin  = 60;
        double stripW   = TabStrip.ActualWidth > 0 ? TabStrip.ActualWidth : ActualWidth;
        double available = Math.Max(0, stripW - kWinCtrl - kNewTab - kDragMin);
        // Hard cap the scroller width so tabs can never push the + button or window controls away
        TabsScroller.MaxWidth = Math.Max(20, available);
        // Allow tabs to shrink down to 20px (favicon-only, like Chrome)
        double tabWidth = Math.Min(220, Math.Max(20, available / _tabs.Count));

        // Three tiers matching Chrome behaviour:
        //   icon-only  : width ≤ 36 — favicon only; active tab shows X instead (centered)
        //   compact    : width ≤ 80 — favicon + no title; active tab shows X (favicon collapsed so X has room)
        //   normal     : width  > 80 — favicon + title + X on active
        bool iconOnly = tabWidth <= 36;
        bool compact  = !iconOnly && tabWidth <= 80;

        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            tab.TabButton.Width = tabWidth;
            bool isActive = i == _activeTabIndex;

            if (tab.TabFavicon != null && tab.TabCloseBtn != null && tab.TabTitle != null)
            {
                if (iconOnly)
                {
                    // Icon-only: no padding, Stretch content so * column fills, X at right on active
                    tab.TabButton.Padding         = new Thickness(0);
                    tab.TabButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    tab.TabTitle.Visibility       = Visibility.Collapsed;
                    if (isActive)
                    {
                        // Collapse favicon (not Hidden) so it takes NO space → X sits at right with room
                        tab.TabFavicon.Visibility  = Visibility.Collapsed;
                        tab.TabCloseBtn.Visibility = Visibility.Visible;
                        tab.TabCloseBtn.Opacity    = 1;
                        // Center the close button within the tab
                        tab.TabCloseBtn.HorizontalAlignment = HorizontalAlignment.Center;
                        tab.TabCloseBtn.Margin = new Thickness(0);
                    }
                    else
                    {
                        tab.TabFavicon.Visibility  = Visibility.Visible;
                        tab.TabFavicon.HorizontalAlignment = HorizontalAlignment.Center;
                        tab.TabFavicon.Margin = new Thickness(0);
                        tab.TabCloseBtn.Visibility = Visibility.Collapsed;
                        tab.TabCloseBtn.Opacity    = 0;
                    }
                }
                else if (compact)
                {
                    // Compact: small padding, favicon (inactive) or X (active); title hidden
                    tab.TabButton.Padding         = new Thickness(6, 0, 4, 0);
                    tab.TabButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    tab.TabTitle.Visibility       = Visibility.Collapsed;
                    tab.TabCloseBtn.Visibility    = Visibility.Visible;
                    tab.TabCloseBtn.Opacity       = isActive ? 1 : 0;
                    // Collapse favicon on active (not Hidden) so X has room instead of being pushed off-screen
                    tab.TabFavicon.Visibility     = isActive ? Visibility.Collapsed : Visibility.Visible;
                    tab.TabFavicon.Margin         = new Thickness(0, 0, 6, 0);
                    tab.TabCloseBtn.Margin        = new Thickness(0);
                    tab.TabCloseBtn.HorizontalAlignment = HorizontalAlignment.Right;
                }
                else
                {
                    // Normal: full padding, favicon + title + X on active (both favicon and X visible)
                    tab.TabButton.Padding         = new Thickness(10, 0, 8, 0);
                    tab.TabButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    tab.TabFavicon.Visibility     = Visibility.Visible;
                    tab.TabFavicon.Margin         = new Thickness(0, 0, 6, 0);
                    tab.TabTitle.Visibility       = Visibility.Visible;
                    tab.TabCloseBtn.Visibility    = Visibility.Visible;
                    tab.TabCloseBtn.Opacity       = isActive ? 1 : 0;
                    tab.TabCloseBtn.Margin        = new Thickness(4, 0, 0, 0);
                    tab.TabCloseBtn.HorizontalAlignment = HorizontalAlignment.Right;
                }
            }
        }
    }

    private Button CreateTabButton(int index)
    {
        var button = new Button
        {
            Style = (Style)FindResource("TabStyle"),
            Tag = index
        };
        
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Audio icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
        // Favicon
        var favicon = new Image
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(favicon, 0);
        grid.Children.Add(favicon);
        
        // Audio icon (speaker) - hidden by default
        var audioIcon = new Canvas
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 4, 0),
            Visibility = Visibility.Collapsed,
            ToolTip = "Tab is playing audio"
        };
        // Speaker icon paths
        var speakerBody = new WpfPath
        {
            Data = Geometry.Parse("M3 9 L3 15 L7 15 L11 18 L11 6 L7 9 Z"),
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            Stretch = Stretch.Uniform,
            Width = 10,
            Height = 10
        };
        Canvas.SetLeft(speakerBody, 0);
        Canvas.SetTop(speakerBody, 2);
        audioIcon.Children.Add(speakerBody);
        var soundWave = new WpfPath
        {
            Data = Geometry.Parse("M13 8 Q15 12 13 16"),
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            StrokeThickness = 1.2,
            Fill = Brushes.Transparent,
            Width = 6,
            Height = 10
        };
        Canvas.SetLeft(soundWave, 8);
        Canvas.SetTop(soundWave, 2);
        audioIcon.Children.Add(soundWave);
        Grid.SetColumn(audioIcon, 1);
        grid.Children.Add(audioIcon);
        
        // Title
        var title = new TextBlock
        {
            Text = "New Tab",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#9aa0a6" : "#202124")!),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 2);
        grid.Children.Add(title);
        
        // Close button
        var closeBtn = new Button
        {
            Width = 16,
            Height = 16,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0),
            Tag = index,
            Opacity = 0
        };
        closeBtn.Content = new WpfPath
        {
            Data = Geometry.Parse("M1 1l8 8M9 1l-8 8"),
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9aa0a6")!),
            StrokeThickness = 1.5,
            Width = 10,
            Height = 10,
            Stretch = Stretch.Uniform
        };
        closeBtn.Click += CloseTab_Click;
        Grid.SetColumn(closeBtn, 3);
        grid.Children.Add(closeBtn);
        
        button.Content = grid;
        
        // X button is only ever visible on the ACTIVE tab — no hover reveal on inactive tabs
        button.MouseEnter += (s, e) => { /* no-op: X stays driven by active state only */ };
        button.MouseLeave += (s, e) => { /* no-op */ };
        
        button.Click += (s, e) =>
        {
            if (s is Button btn && btn.Tag is int idx)
            {
                SwitchToTab(idx);
            }
        };
        
        // Middle click to close
        button.MouseDown += (s, e) =>
        {
            if (e.MiddleButton == MouseButtonState.Pressed && s is Button btn && btn.Tag is int idx)
            {
                CloseTab(idx);
            }
        };
        
        return button;
    }
    
    private bool IsActiveTab(int index) => _activeTabIndex == index;
    
    private void SetupWebViewEvents(WebView2 webView, int tabIndex)
    {
        // Permission handling for camera, microphone, screen capture
        webView.CoreWebView2.PermissionRequested += (s, e) =>
        {
            var uri2 = new Uri(e.Uri);
            var origin2 = uri2.Host;
            var permName2 = GetPermissionName(e.PermissionKind);
            var saved = LoadSitePermissions();
            if (saved.TryGetValue(origin2, out var domainPerms) && domainPerms.TryGetValue(permName2, out var savedState))
            {
                e.State = savedState == "allow" ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                return; // already decided — don't ask again
            }
            ShowPermissionDialog(webView, e);
        };
        
        webView.GotFocus += (s, e) => SuggestPopup.IsOpen = false;
        try
        {
            webView.PreviewMouseDown += (s, e) => SuggestPopup.IsOpen = false;
            webView.MouseDown += (s, e) => SuggestPopup.IsOpen = false;
        }
        catch { /* ignore if host doesn't surface these events */ }

        webView.NavigationStarting += (s, e) =>
        {
            SuggestPopup.IsOpen = false;
            _navStartTimes[webView] = DateTime.UtcNow;
            _autofillShownForTab.Remove(webView);
            // Reset bar state for the active tab
            var navIdx = GetTabIndexForWebView(webView);
            // bar state reset handled by JS on next navigation
            var idx = GetTabIndexForWebView(webView);
            if (idx == _activeTabIndex)
            {
                UrlBox.Text = GetDisplayUrl(e.Uri);
                UpdateUrlPlaceholder();
                UpdateSecurityIcon(e.Uri);
            }

            // Silent login: intercept the support site login page,
            // cancel navigation, POST user ID, inject cookie, navigate to /support.
            if (!string.IsNullOrEmpty(e.Uri) &&
                e.Uri.Contains("ycb.tomcreations.org") &&
                (e.Uri.Contains("/auth/ycbuseridlogin") || e.Uri.Contains("/auth/login")))
            {
                // Don't cancel — let the page load so JS runs in same-origin context
            }
        };
        
        webView.NavigationCompleted += (s, e) =>
        {
            var idx = GetTabIndexForWebView(webView);
            if (idx >= 0 && idx < _tabs.Count)
            {
                
                // Apply zoom
                webView.ZoomFactor = _zoomFactor;

                // Sync bookmark star for the active tab
                if (idx == _activeTabIndex)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        UrlBox.Text = GetDisplayUrl(webView.Source?.ToString());
                        UpdateUrlPlaceholder();
                        RefreshBookmarkStar();
                    });
                }

                // Inject password detection script on real websites
                if (e.IsSuccess)
                {
                    var src2 = webView.Source?.ToString() ?? "";
                    if (!src2.StartsWith("file:///") && !string.IsNullOrEmpty(src2))
                    {
                        _ = webView.ExecuteScriptAsync(PasswordContentScript);

                        // Silent login: if on the YCB support login page, POST user ID via JS
                        // so the browser handles cookies natively (same-origin fetch)
                        if (src2.Contains("ycb.tomcreations.org") &&
                            (src2.Contains("/auth/login") || src2.Contains("/auth/ycbuseridlogin")))
                        {
                            _ = TrySilentSupportLogin(webView);
                        }
                        // Track nav success — host only, no full URL
                        var navHost = GetDomain(src2);
                        var navMs   = _navStartTimes.TryGetValue(webView, out var t0)
                                        ? (int)(DateTime.UtcNow - t0).TotalMilliseconds : -1;
                        _navStartTimes.Remove(webView);
                        ErrorReporter.Track("NavOk", new() { ["host"] = navHost, ["ms"] = navMs });

                        // Track last real page URL for download learning
                        if (!src2.StartsWith("about:") && !src2.StartsWith("ycb://"))
                            _lastRealPageUrl[webView] = src2;
                    }
                    else
                    {
                        _navStartTimes.Remove(webView);
                    }
                }
                else
                {
                    // Silently report navigation failures — no dialog shown to user
                    var failUrl  = webView.Source?.ToString();
                    var errCode  = (int)e.WebErrorStatus;
                    var errName  = e.WebErrorStatus.ToString();
                    _navStartTimes.Remove(webView);
                    App.WriteTrace($"[NAV ERROR] {errName} ({errCode}) @ {failUrl}");
                    ErrorReporter.Report(
                        errorType: "NavigationError",
                        message:   $"Navigation failed: {errName}",
                        pageUrl:   failUrl,
                        errorCode: errCode);
                }
            }
        };
        
        // Enable DevTools console for password detection (all pages)
        webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled")
            .DevToolsProtocolEventReceived += async (s2, args) => await HandleWebConsole(webView, args);
        webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");

        // Handle quickdownload:open messages from injected bars on regular pages
        webView.CoreWebView2.WebMessageReceived += async (s, msgArgs) =>
        {
            try
            {
                var msgDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(msgArgs.WebMessageAsJson);
                if (msgDict == null) return;
                if (!msgDict.TryGetValue("type", out var typeEl) || typeEl.GetString() != "quickdownload:open") return;
                if (!msgDict.TryGetValue("url", out var urlEl)) return;
                var dlUrl = urlEl.GetString();
                if (string.IsNullOrEmpty(dlUrl)) return;
                // Trigger the download via a hidden <a> click so the user stays on the search page
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                    {
                        var activeWv = _tabs[_activeTabIndex].WebView;
                        var safe = dlUrl.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "");
                        await activeWv.ExecuteScriptAsync(
                            $"(function(){{var a=document.createElement('a');a.href='{safe}';a.download='';document.body.appendChild(a);a.click();document.body.removeChild(a);}})();");
                    }
                });
            }
            catch { /* not a quickdownload message */ }
        };
        webView.CoreWebView2.SourceChanged += (s, e) =>
        {
            var src = webView.CoreWebView2.Source ?? "";
            if (string.IsNullOrEmpty(src) || src.StartsWith("file:///") || src.StartsWith("about:")) return;
            var idx = GetTabIndexForWebView(webView);
            if (idx >= 0 && idx < _tabs.Count)
                _tabs[idx].Url = src;
            if (idx == _activeTabIndex)
                Dispatcher.InvokeAsync(UpdateAdBlockButton);
        };
        
        webView.CoreWebView2.DocumentTitleChanged += (s, e) =>
        {
            var idx = GetTabIndexForWebView(webView);
            if (idx >= 0 && idx < _tabs.Count)
            {
                var title = webView.CoreWebView2.DocumentTitle;
                _tabs[idx].Title = title;
                UpdateTabTitle(idx, title);
                AddToHistory(webView.Source?.ToString(), title);
            }
        };
        
        webView.CoreWebView2.FaviconChanged += async (s, e) =>
        {
            var idx = GetTabIndexForWebView(webView);
            if (idx >= 0 && idx < _tabs.Count)
            {
                try
                {
                    var faviconUri = webView.CoreWebView2.FaviconUri;
                    if (!string.IsNullOrEmpty(faviconUri))
                    {
                        await Dispatcher.InvokeAsync(() => UpdateTabFavicon(idx, faviconUri));
                    }
                }
                catch { }
            }
        };
        
        webView.CoreWebView2.NewWindowRequested += (s, e) =>
        {
            e.Handled = true;
            _ = CreateTab(e.Uri);
        };
        
        webView.CoreWebView2.DownloadStarting += (s, e) =>
        {
            e.Handled = true;
            var dlExt0  = IoPath.GetExtension(e.ResultFilePath)?.TrimStart('.').ToLowerInvariant() ?? "";
            var dlKb0   = (int)((e.DownloadOperation.TotalBytesToReceive ?? 0) / 1024);
            ErrorReporter.Track("DlStart", new() { ["ext"] = dlExt0, ["kb"] = dlKb0 });
            var download = new DownloadItem
            {
                Url = e.DownloadOperation.Uri,
                Filename = IoPath.GetFileName(e.ResultFilePath),
                FilePath = e.ResultFilePath,
                SavePath = e.ResultFilePath,
                StartTime = DateTime.Now,
                Status = "Downloading",
                State = "downloading",
                TotalBytes = (long)(e.DownloadOperation.TotalBytesToReceive ?? 0)
            };
            ShowDownloadShelf(download);
            
            e.DownloadOperation.StateChanged += (sender, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                    {
                        download.Status = "Complete";
                        download.State = "completed";
                        download.CompletedAt = DateTime.Now;
                        download.TotalBytes = (long)(e.DownloadOperation.TotalBytesToReceive ?? 0);
                        UpdateDownloadItem(download);
                        SaveDownload(download);  // Save only when complete
                        var dlExt = IoPath.GetExtension(download.FilePath)?.TrimStart('.').ToLowerInvariant() ?? "";
                        var dlKb  = (int)(download.TotalBytes / 1024);
                        var dlDur = (int)(DateTime.Now - download.StartTime).TotalSeconds;
                        ErrorReporter.Track("DlDone", new() { ["ext"] = dlExt, ["kb"] = dlKb, ["dur"] = dlDur });
                    }
                    else if (e.DownloadOperation.State == CoreWebView2DownloadState.Interrupted)
                    {
                        download.Status = "Failed";
                        download.State = "failed";
                        download.CompletedAt = DateTime.Now;
                        UpdateDownloadItem(download);
                        var dlExtF = IoPath.GetExtension(download.FilePath)?.TrimStart('.').ToLowerInvariant() ?? "";
                        ErrorReporter.Track("DlFail", new() { ["ext"] = dlExtF });
                    }
                });
            };
        };
        
        // Audio playing indicator
        webView.CoreWebView2.IsDocumentPlayingAudioChanged += (s, e) =>
        {
            var idx = GetTabIndexForWebView(webView);
            if (idx >= 0 && idx < _tabs.Count)
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateTabAudioIcon(idx, webView.CoreWebView2.IsDocumentPlayingAudio);
                });
            }
        };

        // Silently report renderer / browser process crashes
        webView.CoreWebView2.ProcessFailed += (s, e) =>
        {
            var pageUrl  = webView.Source?.ToString();
            var details  = $"ProcessFailed: kind={e.ProcessFailedKind} reason={e.Reason} exitCode={e.ExitCode}";
            App.WriteTrace($"[PROCESS FAILED] {details} @ {pageUrl}");
            ErrorReporter.Report(
                errorType: "ProcessFailed",
                message:   details,
                pageUrl:   pageUrl,
                errorCode: e.ExitCode);
        };
    }
    
    private int GetTabIndexForWebView(WebView2 webView)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].WebView == webView) return i;
        }
        return -1;
    }
    
    private void UpdateTabTitle(int index, string title)
    {
        if (index >= 0 && index < _tabs.Count)
        {
            var button = _tabs[index].TabButton;
            if (button?.Content is Grid grid)
            {
                var titleBlock = grid.Children.OfType<TextBlock>().FirstOrDefault();
                if (titleBlock != null)
                {
                    titleBlock.Text = string.IsNullOrEmpty(title) ? "New Tab" : title;
                }
            }
        }
    }
    
    private void UpdateTabFavicon(int index, string faviconUrl)
    {
        if (index >= 0 && index < _tabs.Count)
        {
            var button = _tabs[index].TabButton;
            if (button?.Content is Grid grid)
            {
                var image = grid.Children.OfType<Image>().FirstOrDefault();
                if (image != null)
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(faviconUrl);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        image.Source = bitmap;
                    }
                    catch { }
                }
            }
        }
    }
    
    private void UpdateTabAudioIcon(int index, bool isPlaying)
    {
        if (index >= 0 && index < _tabs.Count)
        {
            var button = _tabs[index].TabButton;
            if (button?.Content is Grid grid)
            {
                var audioIcon = grid.Children.OfType<Canvas>().FirstOrDefault();
                if (audioIcon != null)
                {
                    audioIcon.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }
    
    private void UpdateSecurityIcon(string url)
    {
        try
        {
            var uri = new Uri(url);
            var fallbackColor = _isDarkMode ? "#9aa0a6" : "#5f6368";
            var color = uri.Scheme == "https" ? (_isDarkMode ? "#81c995" : "#188038") : fallbackColor;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
            SecurityShield.Stroke = brush;
            SecurityCheck.Stroke = brush;
        }
        catch
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#9aa0a6" : "#5f6368")!);
            SecurityShield.Stroke = brush;
            SecurityCheck.Stroke = brush;
        }
    }
    
    private void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        
        string inactiveStyle = _isDarkMode ? "TabStyle" : "LightTabStyle";
        string activeStyle   = _isDarkMode ? "ActiveTabStyle" : "LightActiveTabStyle";
        string inactiveTitleColor = _isDarkMode ? "#9aa0a6" : "#202124";
        string activeTitleColor   = _isDarkMode ? "#e8eaed" : "#202124";
        string inactiveIconColor  = _isDarkMode ? "#9aa0a6" : "#5f6368";
        string activeIconColor    = _isDarkMode ? "#9aa0a6" : "#5f6368";
        
        // Defensively reset every tab to inactive — prevents any stale ActiveTabStyle on other tabs
        for (int i = 0; i < _tabs.Count; i++)
        {
            var t = _tabs[i];
            if (i != index)
            {
                t.WebView.Visibility = Visibility.Collapsed;
                t.TabButton.Style    = (Style)FindResource(inactiveStyle);
                if (t.TabCloseBtn != null) t.TabCloseBtn.Opacity = 0;
                if (t.TabTitle != null)
                    t.TabTitle.Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(inactiveTitleColor)!);
                if (t.TabCloseBtn?.Content is WpfPath inactivePath)
                    inactivePath.Stroke = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(inactiveIconColor)!);
            }
        }
        
        // Activate target tab
        _activeTabIndex = index;
        _tabs[index].WebView.Visibility  = Visibility.Visible;
        _tabs[index].TabButton.Style     = (Style)FindResource(activeStyle);
        if (_tabs[index].TabCloseBtn != null) _tabs[index].TabCloseBtn.Opacity = 1;
        if (_tabs[index].TabTitle != null)
            _tabs[index].TabTitle.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(activeTitleColor)!);
        if (_tabs[index].TabCloseBtn?.Content is WpfPath activePath)
            activePath.Stroke = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(activeIconColor)!);
        
        // Update URL bar
        if (_tabs[index].WebView.Source != null)
        {
            var url = _tabs[index].WebView.Source.ToString();
            UrlBox.Text = GetDisplayUrl(url);
            UpdateUrlPlaceholder();
            UpdateSecurityIcon(url);
        }
        else
        {
            UrlBox.Text = GetDisplayUrl(_tabs[index].Url);
            UpdateUrlPlaceholder();
        }
        
        UpdateNavButtons();
        RefreshBookmarkStar();
    }
    
    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.Tag is int index)
        {
            CloseTab(index);
        }
    }
    
    private void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        if (_tabs.Count == 1)
        {
            SaveSettings();
            Close();
            return;
        }
        
        var tab = _tabs[index];
        TabsPanel.Children.Remove(tab.TabButton);
        WebViewContainer.Children.Remove(tab.WebView);
        tab.WebView.Dispose();
        _tabs.RemoveAt(index);
        ErrorReporter.Track("TabClose", new() { ["tabs"] = _tabs.Count });
        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].TabButton.Tag = i;
            if (_tabs[i].TabButton.Content is Grid grid)
            {
                var closeBtn = grid.Children.OfType<Button>().FirstOrDefault();
                if (closeBtn != null) closeBtn.Tag = i;
            }
        }
        
        // Switch to another tab
        if (_activeTabIndex >= _tabs.Count)
            _activeTabIndex = _tabs.Count - 1;
        else if (_activeTabIndex == index)
            _activeTabIndex = Math.Max(0, index - 1);
        
        SwitchToTab(_activeTabIndex);
        // Don't resize while mouse is still over the tab strip — lets user rapidly click X
        // (tabs resize when mouse leaves the strip via TabStrip.MouseLeave)
        if (_tabStripMouseOver)
            _tabsClosedWhileOver = true;
        else
            UpdateTabWidths();
    }
    
    private void UpdateNavButtons()
    {
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            var webView = _tabs[_activeTabIndex].WebView;
            if (webView.CoreWebView2 != null)
            {
                BackBtn.IsEnabled = webView.CoreWebView2.CanGoBack;
                ForwardBtn.IsEnabled = webView.CoreWebView2.CanGoForward;
            }
        }
    }
    
    private string GetSearchUrl(string query)
    {
        var q = Uri.EscapeDataString(query);
        return _searchEngine switch
        {
            "bing"       => $"https://www.bing.com/search?q={q}",
            "duckduckgo" => $"https://duckduckgo.com/?q={q}",
            "ecosia"     => $"https://www.ecosia.org/search?q={q}",
            "brave"      => $"https://search.brave.com/search?q={q}",
            "yahoo"      => $"https://search.yahoo.com/search?p={q}",
            _            => $"https://www.google.com/search?q={q}"
        };
    }

    private void Navigate(string input)
    {
        if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count) return;
        SuggestPopup.IsOpen = false;
        
        var url = input.Trim();
        
        // Handle internal URLs
        if (url.StartsWith("ycb://") || url.StartsWith("chrome://"))
        {
            NavigateToInternalPage(_tabs[_activeTabIndex].WebView, url);
            return;
        }
        
        // Check if it's a URL or search query
        if (!url.Contains(".") || url.Contains(" "))
        {
            url = GetSearchUrl(url);
        }
        else if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            url = "https://" + url;
        }
        
        _tabs[_activeTabIndex].WebView.CoreWebView2?.Navigate(url);
    }
    
    private void NavigateToInternalPage(WebView2 webView, string url)
    {
        var pageName = url.Replace("ycb://", "").Replace("chrome://", "").ToLower();
        
        // Get the path to the renderer folder
        var rendererPath = RendererPath;
        
        string htmlFile;
        switch (pageName)
        {
            case "history":
                htmlFile = IoPath.Combine(rendererPath, "history.html");
                break;
            case "support":
                htmlFile = IoPath.Combine(rendererPath, "support.html");
                break;
            case "downloads":
                htmlFile = IoPath.Combine(rendererPath, "downloads.html");
                break;
            case "settings":
                htmlFile = IoPath.Combine(rendererPath, "settings.html");
                break;
            case "passwords":
                htmlFile = IoPath.Combine(rendererPath, "passwords.html");
                break;
            case "guide":
                htmlFile = IoPath.Combine(rendererPath, "guide.html");
                break;
            case "newtab":
            case "new-tab-page":
            default:
                htmlFile = IoPath.Combine(rendererPath, "newtab.html");
                break;
        }
        
        if (File.Exists(htmlFile))
        {
            webView.CoreWebView2?.Navigate($"file:///{htmlFile.Replace("\\", "/")}");
            
            // Setup message handler for this internal page
            SetupInternalPageMessageHandler(webView, pageName);
        }
        else
        {
            // Fallback to generated HTML if file not found
            webView.CoreWebView2?.NavigateToString($"<h1>Page not found: {pageName}</h1><p>Expected at: {htmlFile}</p>");
        }
    }
    
    private void SetupInternalPageMessageHandler(WebView2 webView, string pageName)
    {
        // Remove any existing handlers first
        webView.CoreWebView2.WebMessageReceived -= InternalPage_WebMessageReceived;
        webView.CoreWebView2.WebMessageReceived += InternalPage_WebMessageReceived;
        
        // Setup console message handler for newtab/passwords pages
        webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived -= ConsoleMessage_Received;
        webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived += ConsoleMessage_Received;
        webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        
        // Inject data after page loads
        webView.NavigationCompleted += async (s, e) =>
        {
            if (!e.IsSuccess) return;
            
            try
            {
                // Inject theme for all internal pages
                var themeScript = _isDarkMode ? "" : @"
                    document.body.style.background = '#ffffff';
                    document.body.style.color = '#202124';
                    document.querySelectorAll('.card, .greeting-bar, .bookmarks-section, .bm-chip').forEach(el => {
                        el.style.background = el.style.background.replace('#202124', '#f8f9fa').replace('#2d2e30', '#ffffff').replace('#3c4043', '#f1f3f4');
                        el.style.color = el.style.color.replace('#e8eaed', '#202124').replace('#bdc1c6', '#5f6368');
                    });
                ";
                if (!_isDarkMode)
                {
                    await webView.ExecuteScriptAsync(themeScript);
                }
                
                switch (pageName)
                {
                    case "history":
                        var history = LoadHistory();
                        var historyJson = JsonSerializer.Serialize(history);
                        await webView.ExecuteScriptAsync($"window.loadHistory && window.loadHistory({historyJson})");
                        break;
                        
                    case "downloads":
                        var downloads = LoadDownloads();
                        var downloadsJson = JsonSerializer.Serialize(downloads);
                        await webView.ExecuteScriptAsync($"window.setDownloadHistory && window.setDownloadHistory({downloadsJson})");
                        break;
                        
                    case "settings":
                        // Inject Copilot info and default browser status
                        var copilotInfo = GetCopilotInfo();
                        var infoJson = JsonSerializer.Serialize(copilotInfo);
                        await webView.ExecuteScriptAsync($"window.setCopilotInfo && window.setCopilotInfo({infoJson})");
                        
                        // Check and inject default browser status
                        var isDefault = CheckIsDefaultBrowser();
                        await webView.ExecuteScriptAsync($@"
                            (function() {{
                                var status = document.getElementById('default-status');
                                var btn = document.getElementById('btn-set-default');
                                if (status && btn) {{
                                    if ({(isDefault ? "true" : "false")}) {{
                                        status.textContent = 'YCB is already your default browser';
                                        status.className = 'row-desc success';
                                        btn.textContent = 'Already default';
                                        btn.disabled = true;
                                    }}
                                }}
                                // Update user info
                                var userEl = document.getElementById('copilot-user');
                                if (userEl && {infoJson}.authenticated) {{
                                    userEl.textContent = '@' + {infoJson}.username + ' (signed in)';
                                    userEl.className = 'row-desc success';
                                }}
                            }})();
                        ");
                        
                        // Inject current settings so the page shows persisted values
                        var settingsData = new {
                            bookmarks_bar = _settings.BookmarksBarVisible ? "on" : "off",
                            search_engine = _settings.SearchEngine ?? "google",
                            startup_mode = _settings.StartupMode ?? "newtab",
                            ycb_model = _settings.YcbModel ?? "gpt-5-mini",
                            incognito_ai_enabled = (_settings.IncognitoAIEnabled ?? false).ToString().ToLower(),
                            browser_theme = _settings.DarkMode ? "dark" : "light",
                            telemetry_enabled = _settings.TelemetryEnabled.ToString().ToLower(),
                            user_id = ErrorReporter.UserId,
                            ai_enabled = _aiEnabled ? "on" : "off",
                            ad_blocker_enabled = _settings.AdBlockerEnabled ? "on" : "off",
                            home_page = _settings.HomePage ?? "ycb://newtab"
                        };
                        var settingsDataJson = JsonSerializer.Serialize(settingsData);
                        await webView.ExecuteScriptAsync($"window.loadSettings && window.loadSettings({settingsDataJson})");
                        
                        // Directly inject user ID into the About section element
                        var safeUid = JsonSerializer.Serialize(ErrorReporter.UserId);
                        await webView.ExecuteScriptAsync($@"
                            (function() {{
                                var el = document.getElementById('about-user-id');
                                if (!el) return;
                                el.textContent = {safeUid};
                                el.onclick = function() {{
                                    navigator.clipboard.writeText({safeUid}).then(function() {{
                                        el.textContent = 'Copied!';
                                        el.style.color = 'var(--green, #81c995)';
                                        setTimeout(function() {{ el.textContent = {safeUid}; el.style.color = ''; }}, 1500);
                                    }});
                                }};
                            }})();
                        ");
                        break;
                        
                    case "newtab":
                    case "new-tab-page":
                        break;
                        
                    case "passwords":
                        // Inject passwords
                        var passwords = LoadPasswordsDecrypted();
                        var passwordsJson = JsonSerializer.Serialize(passwords);
                        await webView.ExecuteScriptAsync($"window.setPasswords && window.setPasswords({passwordsJson})");
                        break;

                    case "guide":
                        if (!_aiEnabled)
                        {
                            await webView.ExecuteScriptAsync(@"
                                (function() {
                                    var el = document.getElementById('ai-setup-section');
                                    if (el) el.style.display = 'none';
                                })();
                            ");
                        }
                        break;

                    default:
                        // Inject ad blocker on real web pages
                        if (_settings.AdBlockerEnabled)
                        {
                            var src = webView.Source?.ToString() ?? "";
                            if (src.StartsWith("http://") || src.StartsWith("https://"))
                                await webView.ExecuteScriptAsync(GetAdBlockerScript());
                        }
                        break;
                }
            }
            catch { }
        };
    }
    
    private async void ConsoleMessage_Received(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(e.ParameterObjectAsJson);
            if (!json.TryGetProperty("args", out var args) || args.GetArrayLength() == 0) return;
            
            var firstArg = args[0];
            if (!firstArg.TryGetProperty("value", out var valueElement)) return;
            
            var message = valueElement.GetString();
            if (string.IsNullOrEmpty(message)) return;
            
            // Handle bookmark messages from newtab
            // Handle password messages
            if (message.StartsWith("__passwords__:"))
            {
                var parts = message.Substring(14).Split(new[] { ':' }, 2);
                var action = parts[0];
                var data = parts.Length > 1 ? parts[1] : "";
                
                switch (action)
                {
                    case "GET_ALL":
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                            {
                                var webView = _tabs[_activeTabIndex].WebView;
                                var passwords = LoadPasswordsDecrypted();
                                var passwordsJson = JsonSerializer.Serialize(passwords);
                                await webView.ExecuteScriptAsync($"window.setPasswords && window.setPasswords({passwordsJson})");
                            }
                        });
                        break;
                        
                    case "ADD_MANUAL":
                        try
                        {
                            var manualData = JsonSerializer.Deserialize<Dictionary<string, string>>(data);
                            if (manualData != null)
                            {
                                var manualUrl  = manualData.GetValueOrDefault("url", "").Trim();
                                var manualUser = manualData.GetValueOrDefault("username", "").Trim();
                                var manualPass = manualData.GetValueOrDefault("password", "").Trim();
                                if (!string.IsNullOrEmpty(manualUrl) && !string.IsNullOrEmpty(manualPass))
                                {
                                    if (!manualUrl.StartsWith("http://") && !manualUrl.StartsWith("https://"))
                                        manualUrl = "https://" + manualUrl;
                                    SavePassword(manualUrl, manualUser, manualPass);
                                    await Dispatcher.InvokeAsync(async () =>
                                    {
                                        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                                        {
                                            var wv = _tabs[_activeTabIndex].WebView;
                                            var pws = LoadPasswordsDecrypted();
                                            var pwsJson = JsonSerializer.Serialize(pws);
                                            await wv.ExecuteScriptAsync($"window.setPasswords && window.setPasswords({pwsJson})");
                                        }
                                    });
                                }
                            }
                        }
                        catch { }
                        break;
                        
                    case "DELETE":
                        DeletePassword(data);
                        break;
                        
                    case "CLEAR_ALL":
                        ClearPasswords();
                        break;
                }
            }
            // Handle newtab ready message
            else if (message == "__newtab__:ready")
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                    {
                        var webView = _tabs[_activeTabIndex].WebView;
                        var bookmarks = LoadBookmarks();
                        var bookmarksJson = JsonSerializer.Serialize(bookmarks);
                        await webView.ExecuteScriptAsync($"window.setBookmarks && window.setBookmarks({bookmarksJson})");
                    }
                });
            }
            // Handle browser/default commands from settings
            else if (message.StartsWith("__browser__:"))
            {
                var jsonData = message.Substring(12);
                var browserMsg = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonData);
                if (browserMsg != null && browserMsg.TryGetValue("type", out var msgType))
                {
                    switch (msgType)
                    {
                        case "setDefault":
                            await Dispatcher.InvokeAsync(() => SetAsDefaultBrowser());
                            break;
                    }
                }
            }
            // Handle settings changes
            else if (message.StartsWith("__settings__:SET:"))
            {
                var settingData = message.Substring(17);
                var parts = settingData.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    var key = parts[0];
                    var value = parts[1];
                    await Dispatcher.InvokeAsync(() => ApplySettingChange(key, value));
                }
            }
        }
        catch { }
    }
    
    private async void InternalPage_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // Handle plain string messages (e.g. from guide.html)
            var rawMsg = e.TryGetWebMessageAsString();
            if (rawMsg == "__guide__:DISMISS")
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _settings.HasSeenGuide = true;
                    SaveSettings();
                });
                return;
            }

            // Parse the message — use rawMsg first (postMessage sends strings; WebMessageAsJson double-encodes them)
            Dictionary<string, JsonElement>? message = null;
            if (!string.IsNullOrEmpty(rawMsg) && rawMsg.TrimStart().StartsWith("{"))
            {
                try { message = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawMsg); } catch { }
            }
            // Fallback to WebMessageAsJson (for postMessage(object) calls)
            if (message == null)
            {
                try { message = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(e.WebMessageAsJson); } catch { }
            }
            if (message == null || !message.TryGetValue("type", out var typeElement)) return;
            
            var type = typeElement.GetString();
            
            switch (type)
            {
                case "history:open":
                    if (message.TryGetValue("url", out var urlElement))
                    {
                        var url = urlElement.GetString();
                        if (!string.IsNullOrEmpty(url))
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                                    _tabs[_activeTabIndex].WebView?.CoreWebView2?.Navigate(url);
                            });
                        }
                    }
                    break;
                    
                case "history:clear":
                    ClearHistory();
                    if (sender is CoreWebView2 clearWv)
                        await clearWv.ExecuteScriptAsync("window.loadHistory && window.loadHistory([])");
                    break;
                    
                case "history:getAll":
                    if (sender is CoreWebView2 wv)
                    {
                        var history = LoadHistory();
                        var json = JsonSerializer.Serialize(history);
                        await wv.ExecuteScriptAsync($"window.loadHistory && window.loadHistory({json})");
                    }
                    break;
                    
                case "downloads:getHistory":
                    if (sender is CoreWebView2 dwv)
                    {
                        var downloads = LoadDownloads();
                        var json = JsonSerializer.Serialize(downloads);
                        await dwv.ExecuteScriptAsync($"window.setDownloadHistory && window.setDownloadHistory({json})");
                    }
                    break;
                    
                case "downloads:clearHistory":
                    ClearDownloads();
                    break;
                    
                case "support:getUserId":
                    if (sender is CoreWebView2 ugvw)
                    {
                        var uid = JsonSerializer.Serialize(ErrorReporter.UserId ?? "unknown");
                        await ugvw.ExecuteScriptAsync($"window.setUserId && window.setUserId({uid})");
                    }
                    break;

                case "support:create":
                    if (sender is CoreWebView2 scvw)
                    {
                        var subject = message.TryGetValue("subject", out var sj) ? sj.GetString() ?? "" : "";
                        var msg     = message.TryGetValue("message", out var mg) ? mg.GetString() ?? "" : "";
                        var userId  = ErrorReporter.UserId ?? "unknown";
                        try
                        {
                            using var http = new System.Net.Http.HttpClient();
                            http.Timeout = TimeSpan.FromSeconds(10);
                            var payload = JsonSerializer.Serialize(new { userId, subject, message = msg });
                            var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                            var resp = await http.PostAsync("https://ycb.tomcreations.org/Support/Ticket/", content);
                            var statusCode = (int)resp.StatusCode;
                            if (resp.IsSuccessStatusCode)
                            {
                                var json = await resp.Content.ReadAsStringAsync();
                                await scvw.ExecuteScriptAsync($"window.onTicketCreated && window.onTicketCreated({json}, {statusCode})");
                            }
                            else
                            {
                                await scvw.ExecuteScriptAsync($"window.onTicketError && window.onTicketError({statusCode})");
                            }
                        }
                        catch (Exception ex)
                        {
                            await scvw.ExecuteScriptAsync($"window.onTicketError && window.onTicketError(0, {JsonSerializer.Serialize(ex.Message)})");
                        }
                    }
                    break;

                case "support:reply":
                    if (sender is CoreWebView2 srvw)
                    {
                        var ticketId = message.TryGetValue("ticketId", out var tid) ? tid.GetString() ?? "" : "";
                        var replyMsg = message.TryGetValue("message",  out var rm)  ? rm.GetString()  ?? "" : "";
                        var userId   = ErrorReporter.UserId ?? "unknown";
                        try
                        {
                            using var http = new System.Net.Http.HttpClient();
                            http.Timeout = TimeSpan.FromSeconds(10);
                            var payload = JsonSerializer.Serialize(new { userId, message = replyMsg });
                            var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                            var resp = await http.PostAsync($"https://ycb.tomcreations.org/Support/Ticket/{ticketId}/Reply/", content);
                            var statusCode = (int)resp.StatusCode;
                            await srvw.ExecuteScriptAsync($"window.onReplyResult && window.onReplyResult({(resp.IsSuccessStatusCode ? "true" : "false")}, {statusCode})");
                        }
                        catch
                        {
                            await srvw.ExecuteScriptAsync("window.onReplyResult && window.onReplyResult(false, 0)");
                        }
                    }
                    break;

                case "support:poll":
                    if (sender is CoreWebView2 spvw)
                    {
                        var ticketId = message.TryGetValue("ticketId", out var ptid) ? ptid.GetString() ?? "" : "";
                        try
                        {
                            using var http = new System.Net.Http.HttpClient();
                            http.Timeout = TimeSpan.FromSeconds(10);
                            var resp = await http.GetAsync($"https://ycb.tomcreations.org/Support/Ticket/{ticketId}/");
                            var statusCode = (int)resp.StatusCode;
                            if (resp.IsSuccessStatusCode)
                            {
                                var json = await resp.Content.ReadAsStringAsync();
                                await spvw.ExecuteScriptAsync($"window.onPollResult && window.onPollResult({json}, {statusCode})");
                            }
                            else
                            {
                                await spvw.ExecuteScriptAsync($"window.onPollResult && window.onPollResult(null, {statusCode})");
                            }
                        }
                        catch
                        {
                            await spvw.ExecuteScriptAsync("window.onPollResult && window.onPollResult(null, 0)");
                        }
                    }
                    break;

                case "settings:getUserId":
                    if (sender is CoreWebView2 swv)
                    {
                        var uid = JsonSerializer.Serialize(ErrorReporter.UserId);
                        await swv.ExecuteScriptAsync($@"
                            (function() {{
                                var el = document.getElementById('about-user-id');
                                if (!el) return;
                                var id = {uid};
                                el.textContent = id;
                                el.onclick = function() {{
                                    navigator.clipboard.writeText(id).then(function() {{
                                        el.textContent = 'Copied!';
                                        el.style.color = '#81c995';
                                        setTimeout(function() {{ el.textContent = id; el.style.color = ''; }}, 1500);
                                    }});
                                }};
                            }})();
                        ");
                    }
                    break;

                case "setDefault":
                    await Dispatcher.InvokeAsync(() => SetAsDefaultBrowser());
                    break;

                case "downloads:openFile":
                    if (message.TryGetValue("path", out var pathElement))
                    {
                        var path = pathElement.GetString();
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        }
                    }
                    break;
                    
                case "downloads:showInFolder":
                    if (message.TryGetValue("path", out var folderPathElement))
                    {
                        var path = folderPathElement.GetString();
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            Process.Start("explorer.exe", $"/select,\"{path}\"");
                        }
                    }
                    break;
            }
        }
        catch { }
    }
    
    private object GetCopilotInfo()
    {
        try
        {
            var configPath = IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (config != null && config.TryGetValue("logged_in_users", out var users))
                {
                    var userList = users.Deserialize<List<Dictionary<string, string>>>();
                    if (userList?.Count > 0 && userList[0].TryGetValue("login", out var login))
                    {
                        return new { authenticated = true, username = login, cliPath = "copilot" };
                    }
                }
            }
        }
        catch { }
        return new { authenticated = false, username = "", cliPath = "copilot" };
    }
    
    // ── Win32 native drag ─────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int WM_SYSCOMMAND   = 0x0112;
    private const int SC_MINIMIZE      = 0xF020;
    private const int HTCAPTION = 2;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int GWL_STYLE      = -16;
    private const int WS_CAPTION     = 0x00C00000;
    private const int WS_THICKFRAME  = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int SW_MINIMIZE    = 6;
    private const int SW_MAXIMIZE    = 3;
    private const int SW_RESTORE     = 9;

    // OnSourceInitialized: SingleBorderWindow already has WS_CAPTION/THICKFRAME
    // DWM animates minimize/maximize/restore natively via ShowWindow P/Invoke
    protected override void OnSourceInitialized(EventArgs e)
    {
        // Add our hook BEFORE base — hooks are called in addition order (FIFO).
        // If we add after base, WPF's ChromeWorker hook runs first and sets handled=true,
        // meaning our hook never runs for messages like WM_NCCALCSIZE.
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source.AddHook(WndProc);

        base.OnSourceInitialized(e);

        // Intercept F11 at the thread message level — fires even when WebView2 has focus.
        // WPF's KeyDown never fires for keys consumed by WebView2 (Win32 child HWND).
        ComponentDispatcher.ThreadPreprocessMessage += (ref MSG msg, ref bool handled) =>
        {
            const int WM_KEYDOWN = 0x0100;
            const int VK_F11    = 0x7A;
            if (!handled && msg.message == WM_KEYDOWN && (int)msg.wParam == VK_F11)
            {
                ToggleFullscreen();
                handled = true;
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
    private const int WM_GETMINMAXINFO         = 0x0024;
    private const int SC_MAXIMIZE              = 0xF030;
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND        = 1;
    private const int DWMWCP_DEFAULT           = 0;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_FRAMECHANGED  = 0x0020;
    private const uint SWP_NOSIZE        = 0x0001;
    private const uint SWP_NOMOVE        = 0x0002;
    private const uint SWP_NOACTIVATE    = 0x0010;
    private static readonly IntPtr HWND_TOPMOST    = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST  = new IntPtr(-2);

    private void SetCornerPreference(int pref)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        else if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MAXIMIZE)
        {
            // Aero snap / Win+Up / system maximize — set DONOTROUND before Windows extends the window
            SetCornerPreference(DWMWCP_DONOTROUND);
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi     = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref mi);
            var work = mi.rcWork;
            var mon  = mi.rcMonitor;
            mmi.ptMaxPosition.x = Math.Abs(work.Left - mon.Left);
            mmi.ptMaxPosition.y = Math.Abs(work.Top  - mon.Top);
            mmi.ptMaxSize.x     = Math.Abs(work.Right  - work.Left);
            mmi.ptMaxSize.y     = Math.Abs(work.Bottom - work.Top);
            mmi.ptMinTrackSize.x = 400;
            mmi.ptMinTrackSize.y = 300;
        }
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private void BeginNativeDrag()
    {
        ReleaseCapture();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
    }

    // Event handlers
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Maximize_Click(sender, e);
        }
        else
        {
            // Native drag handles maximized-restore-and-drag automatically
            BeginNativeDrag();
        }
    }
    
    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        // ShowWindow goes straight to Win32 — DWM plays native swoop to taskbar
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        ShowWindow(hwnd, SW_MINIMIZE);
    }
    
    private void ManualMaximize()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref mi);
        var src = PresentationSource.FromVisual(this);
        double scaleX = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double scaleY = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;
        double toL = mi.rcWork.Left                      * scaleX;
        double toT = mi.rcWork.Top                       * scaleY;
        double toW = (mi.rcWork.Right  - mi.rcWork.Left) * scaleX;
        double toH = (mi.rcWork.Bottom - mi.rcWork.Top)  * scaleY;
        _manuallyMaximized = true;
        ShowRestoreIcon();
        AnimateBounds(Left, Top, Width, Height, toL, toT, toW, toH);
    }

    private void AnimateBounds(double fL, double fT, double fW, double fH,
                                double tL, double tT, double tW, double tH)
    {
        var dur  = new Duration(TimeSpan.FromMilliseconds(160));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        void Anim(DependencyProperty dp, double from, double to) =>
            BeginAnimation(dp, new DoubleAnimation(from, to, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        Anim(Window.LeftProperty,            fL, tL);
        Anim(Window.TopProperty,             fT, tT);
        Anim(FrameworkElement.WidthProperty,  fW, tW);
        Anim(FrameworkElement.HeightProperty, fH, tH);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
        timer.Tick += (s, _) =>
        {
            timer.Stop();
            BeginAnimation(Window.LeftProperty, null);
            BeginAnimation(Window.TopProperty, null);
            BeginAnimation(FrameworkElement.WidthProperty, null);
            BeginAnimation(FrameworkElement.HeightProperty, null);
            Left = tL; Top = tT; Width = tW; Height = tH;
        };
        timer.Start();
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (!_manuallyMaximized)
        {
            _savedLeft = Left; _savedTop = Top;
            _savedWidth = Width; _savedHeight = Height;
            _savedWindowState = WindowState;
            ManualMaximize();
        }
        else
        {
            _manuallyMaximized = false;
            ShowMaximizeIcon();
            if (_savedWidth > 0 && _savedHeight > 0)
                AnimateBounds(Left, Top, Width, Height, _savedLeft, _savedTop, _savedWidth, _savedHeight);
        }
    }
    
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        Close();
    }
    
    private async void AddTab_Click(object sender, RoutedEventArgs e)
    {
        await CreateTab(_settings.HomePage ?? "ycb://newtab");
    }
    
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].WebView.CoreWebView2?.GoBack();
        }
    }
    
    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].WebView.CoreWebView2?.GoForward();
        }
    }
    
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].WebView.CoreWebView2?.Reload();
        }
    }
    
    // ── P/Invoke: enumerate audio input (microphone) devices via winmm ──
    [DllImport("winmm.dll")] private static extern int waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int waveInGetDevCaps(int id, ref WAVEINCAPS2 c, int sz);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WAVEINCAPS2
    {
        public ushort wMid, wPid; public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint dwFormats; public ushort wChannels, wReserved1;
    }
    private static List<string> GetMicrophoneDevices()
    {
        var list = new List<string>();
        try { int n = waveInGetNumDevs(); for (int i = 0; i < n; i++) { var c = new WAVEINCAPS2(); if (waveInGetDevCaps(i, ref c, Marshal.SizeOf(c)) == 0) list.Add(c.szPname); } } catch { }
        if (list.Count == 0) list.Add("Default microphone");
        return list;
    }
    private static List<string> GetCameraDevices() => new() { "Default camera" };

    private static string GetPermissionName(CoreWebView2PermissionKind k) => k switch
    {
        CoreWebView2PermissionKind.Camera       => "camera",
        CoreWebView2PermissionKind.Microphone   => "microphone",
        CoreWebView2PermissionKind.Geolocation  => "location",
        CoreWebView2PermissionKind.Notifications => "notifications",
        CoreWebView2PermissionKind.ClipboardRead => "clipboard",
        _ => k.ToString().ToLower()
    };

    private Dictionary<string, Dictionary<string, string>> LoadSitePermissions()
    {
        try { if (File.Exists(_permissionsPath)) return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(_permissionsPath)) ?? new(); } catch { }
        return new();
    }
    private void SaveSitePermission(string origin, string perm, string state)
    {
        try { var all = LoadSitePermissions(); if (!all.ContainsKey(origin)) all[origin] = new(); all[origin][perm] = state; File.WriteAllText(_permissionsPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }
    private void RemoveSitePermission(string origin, string perm)
    {
        try { var all = LoadSitePermissions(); if (all.TryGetValue(origin, out var d)) d.Remove(perm); File.WriteAllText(_permissionsPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    private void ShowPermissionDialog(WebView2 webView, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.Handled = true;
        var deferral = e.GetDeferral();

        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var kind    = e.PermissionKind;
                var uri     = new Uri(e.Uri);
                var origin  = uri.Host;
                var permKey = GetPermissionName(kind);

                bool hasPicker = kind == CoreWebView2PermissionKind.Camera || kind == CoreWebView2PermissionKind.Microphone;
                var devices = hasPicker
                    ? (kind == CoreWebView2PermissionKind.Microphone ? GetMicrophoneDevices() : GetCameraDevices())
                    : new List<string>();

                string subtitle = kind switch
                {
                    CoreWebView2PermissionKind.Microphone   => $"Use available microphones ({devices.Count})",
                    CoreWebView2PermissionKind.Camera       => $"Use available cameras ({devices.Count})",
                    CoreWebView2PermissionKind.Geolocation  => "Know your location",
                    CoreWebView2PermissionKind.Notifications => "Show notifications",
                    CoreWebView2PermissionKind.ClipboardRead => "Read your clipboard",
                    _ => $"Access {permKey}"
                };

                string iconData = kind switch
                {
                    CoreWebView2PermissionKind.Camera       => "M15 8v8H3V8h2l1-2h6l1 2h2zm-6 6a3 3 0 100-6 3 3 0 000 6z",
                    CoreWebView2PermissionKind.Microphone   => "M12 14a3 3 0 003-3V5a3 3 0 00-6 0v6a3 3 0 003 3zm5-3a5 5 0 01-10 0H5a7 7 0 0014 0h-2zm-5 5v-3",
                    CoreWebView2PermissionKind.Geolocation  => "M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5a2.5 2.5 0 010-5 2.5 2.5 0 010 5z",
                    CoreWebView2PermissionKind.Notifications => "M12 22c1.1 0 2-.9 2-2h-4c0 1.1.9 2 2 2zm6-6v-5c0-3.07-1.64-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5S10.5 3.17 10.5 4v.68C7.63 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z",
                    _ => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"
                };

                var accent  = (Color)ColorConverter.ConvertFromString("#8ab4f8")!;
                var bgDark  = (Color)ColorConverter.ConvertFromString("#292a2d")!;
                var bgDeep  = (Color)ColorConverter.ConvertFromString("#202124")!;
                var border1 = (Color)ColorConverter.ConvertFromString("#3c4043")!;
                var textPri = (Color)ColorConverter.ConvertFromString("#e8eaed")!;
                var textSub = (Color)ColorConverter.ConvertFromString("#9aa0a6")!;

                var popup = new Window
                {
                    WindowStyle = WindowStyle.None, AllowsTransparency = true,
                    Background = Brushes.Transparent, ShowInTaskbar = false,
                    Topmost = true, Owner = this,
                    Width = 320, SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };
                var pt = SecurityIconCanvas.PointToScreen(new Point(0, SecurityIconCanvas.ActualHeight));
                popup.Left = pt.X - 20; popup.Top = pt.Y + 6;

                var rootBorder = new Border
                {
                    Background = new SolidColorBrush(bgDark), CornerRadius = new CornerRadius(12),
                    BorderBrush = new SolidColorBrush(border1), BorderThickness = new Thickness(1),
                    Margin = new Thickness(8),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
                };

                var outerStack = new StackPanel();

                // Title row
                var titleGrid = new Grid { Margin = new Thickness(16, 14, 12, 10) };
                titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                titleGrid.Children.Add(new TextBlock { Text = $"{origin} wants to", Foreground = new SolidColorBrush(textPri), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var xBtn = new Button { Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(textSub), FontSize = 13, Width = 26, Height = 26, Cursor = Cursors.Hand, Padding = new Thickness(0) };
                Grid.SetColumn(xBtn, 1); titleGrid.Children.Add(xBtn);
                outerStack.Children.Add(titleGrid);

                // Subtitle row
                var subRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 14) };
                subRow.Children.Add(new WpfPath { Data = Geometry.Parse(iconData), Fill = new SolidColorBrush(textSub), Width = 16, Height = 16, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                subRow.Children.Add(new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(textPri), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
                outerStack.Children.Add(subRow);

                // Device picker
                if (hasPicker && devices.Count > 0)
                {
                    var pickerBorder = new Border
                    {
                        Background = new SolidColorBrush(bgDeep), CornerRadius = new CornerRadius(8),
                        BorderBrush = new SolidColorBrush(border1), BorderThickness = new Thickness(1),
                        Margin = new Thickness(14, 0, 14, 14), Padding = new Thickness(12, 10, 12, 10)
                    };
                    var pickerStack = new StackPanel();

                    // Icon + toggle row
                    var iconToggleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
                    iconToggleRow.Children.Add(new WpfPath { Data = Geometry.Parse(iconData), Fill = new SolidColorBrush(accent), Width = 18, Height = 18, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                    // Toggle switch (visual, always ON)
                    var toggleGrid = new Grid { Width = 36, Height = 20, VerticalAlignment = VerticalAlignment.Center };
                    toggleGrid.Children.Add(new Border { Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(10) });
                    toggleGrid.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
                    iconToggleRow.Children.Add(toggleGrid);
                    pickerStack.Children.Add(iconToggleRow);

                    var combo = new ComboBox { FontSize = 13, Height = 32, Foreground = new SolidColorBrush(textPri), Background = new SolidColorBrush(bgDark), BorderBrush = new SolidColorBrush(border1) };
                    foreach (var d in devices) combo.Items.Add(d);
                    combo.SelectedIndex = 0;
                    pickerStack.Children.Add(combo);

                    pickerBorder.Child = pickerStack;
                    outerStack.Children.Add(pickerBorder);
                }

                // Separator
                outerStack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(border1) });

                // Helper: create a pill button
                Border MakePill(string label) {
                    var b = new Border
                    {
                        CornerRadius = new CornerRadius(20), Margin = new Thickness(14, 6, 14, 6),
                        Padding = new Thickness(0, 11, 0, 11), Cursor = Cursors.Hand,
                        Background = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)),
                        BorderThickness = new Thickness(1)
                    };
                    b.Child = new TextBlock { Text = label, Foreground = new SolidColorBrush(accent), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
                    b.MouseEnter  += (s, _) => b.Background = new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B));
                    b.MouseLeave  += (s, _) => b.Background = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B));
                    return b;
                }

                var allowAlways = MakePill("Allow while visiting the site");
                var allowOnce   = MakePill("Allow this time");
                var neverAllow  = MakePill("Never allow");
                // Give "Never allow" a red tint
                neverAllow.Background = new SolidColorBrush(Color.FromArgb(25, 0xf2, 0x8b, 0x82));
                neverAllow.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0xf2, 0x8b, 0x82));
                ((TextBlock)neverAllow.Child).Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f28b82")!);
                neverAllow.MouseEnter += (s, _) => neverAllow.Background = new SolidColorBrush(Color.FromArgb(55, 0xf2, 0x8b, 0x82));
                neverAllow.MouseLeave += (s, _) => neverAllow.Background = new SolidColorBrush(Color.FromArgb(25, 0xf2, 0x8b, 0x82));
                neverAllow.Margin = new Thickness(14, 6, 14, 14);

                outerStack.Children.Add(allowAlways);
                outerStack.Children.Add(allowOnce);
                outerStack.Children.Add(neverAllow);

                rootBorder.Child = outerStack;
                popup.Content = rootBorder;

                void CloseWith(CoreWebView2PermissionState state, bool save)
                {
                    e.State = state;
                    if (save) SaveSitePermission(origin, permKey, state == CoreWebView2PermissionState.Allow ? "allow" : "block");
                    deferral.Complete();
                    popup.Close();
                }

                allowAlways.MouseLeftButtonDown += (s, _) => CloseWith(CoreWebView2PermissionState.Allow, true);
                allowOnce.MouseLeftButtonDown   += (s, _) => CloseWith(CoreWebView2PermissionState.Allow, false);
                neverAllow.MouseLeftButtonDown  += (s, _) => CloseWith(CoreWebView2PermissionState.Deny,  true);
                xBtn.Click += (s, _) => { e.State = CoreWebView2PermissionState.Default; deferral.Complete(); popup.Close(); };

                popup.Deactivated += (s, _) => { if (popup.IsVisible) { e.State = CoreWebView2PermissionState.Default; deferral.Complete(); popup.Close(); } };
                popup.Show();
                ForcePopupOnTop(popup);
                TrackPopupPosition(popup, () => { var pt = SecurityIconCanvas.PointToScreen(new Point(0, SecurityIconCanvas.ActualHeight)); return (pt.X - 20, pt.Y + 6); });
            }
            catch
            {
                e.State = CoreWebView2PermissionState.Default;
                deferral.Complete();
            }
        });
    }

    private void SecurityIcon_Click(object sender, MouseButtonEventArgs e) => OpenSiteInfoForActiveTab();

    private void OpenSiteInfoForActiveTab()
    {
        if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count) return;
        var webView = _tabs[_activeTabIndex].WebView;
        var url = webView.Source?.ToString() ?? "";
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http")) return;
        try { ShowSiteInfoPanel(webView, new Uri(url)); } catch { }
    }

    private void ShowSiteInfoPanel(WebView2 webView, Uri uri)
    {
        var origin  = uri.Host;
        var isHttps = uri.Scheme == "https";

        var accent  = (Color)ColorConverter.ConvertFromString("#8ab4f8")!;
        var bgDark  = (Color)ColorConverter.ConvertFromString("#292a2d")!;
        var bgDeep  = (Color)ColorConverter.ConvertFromString("#202124")!;
        var border1 = (Color)ColorConverter.ConvertFromString("#3c4043")!;
        var textPri = (Color)ColorConverter.ConvertFromString("#e8eaed")!;
        var textSub = (Color)ColorConverter.ConvertFromString("#9aa0a6")!;

        var popup = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, ShowInTaskbar = false,
            Topmost = true, Owner = this,
            Width = 300, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        var pt = SecurityIconCanvas.PointToScreen(new Point(0, SecurityIconCanvas.ActualHeight));
        popup.Left = pt.X - 20; popup.Top = pt.Y + 6;

        var rootBorder = new Border
        {
            Background = new SolidColorBrush(bgDark), CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(border1), BorderThickness = new Thickness(1),
            Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
        };
        var outerStack = new StackPanel();

        // Title row: lock + origin + X
        var titleGrid = new Grid { Margin = new Thickness(14, 14, 12, 4) };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lockIcon = new WpfPath
        {
            Data = Geometry.Parse(isHttps ? "M7 11V7a5 5 0 0110 0v4M5 11h14a2 2 0 012 2v7a2 2 0 01-2 2H5a2 2 0 01-2-2v-7a2 2 0 012-2z" : "M17 11V7a5 5 0 00-9.9-1M5 11h14a2 2 0 012 2v7a2 2 0 01-2 2H5a2 2 0 01-2-2v-7a2 2 0 012-2z"),
            Stroke = new SolidColorBrush(isHttps ? (Color)ColorConverter.ConvertFromString("#81c995")! : (Color)ColorConverter.ConvertFromString("#f28b82")!),
            StrokeThickness = 1.5, Fill = Brushes.Transparent,
            Width = 16, Height = 16, Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lockIcon, 0); titleGrid.Children.Add(lockIcon);
        var originText = new TextBlock { Text = origin, Foreground = new SolidColorBrush(textPri), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(originText, 1); titleGrid.Children.Add(originText);
        var xBtn = new Button { Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(textSub), FontSize = 13, Width = 26, Height = 26, Cursor = Cursors.Hand, Padding = new Thickness(0) };
        xBtn.Click += (s, _) => popup.Close();
        Grid.SetColumn(xBtn, 2); titleGrid.Children.Add(xBtn);
        outerStack.Children.Add(titleGrid);

        // Connection status
        outerStack.Children.Add(new TextBlock
        {
            Text = isHttps ? "Connection is secure" : "Connection is not secure",
            Foreground = new SolidColorBrush(isHttps ? (Color)ColorConverter.ConvertFromString("#81c995")! : (Color)ColorConverter.ConvertFromString("#f28b82")!),
            FontSize = 12, Margin = new Thickness(14, 2, 14, 12)
        });

        outerStack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(border1) });

        // Permissions section
        outerStack.Children.Add(new TextBlock { Text = "Permissions", Foreground = new SolidColorBrush(textSub), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 10, 14, 6) });

        var savedPerms = LoadSitePermissions();
        savedPerms.TryGetValue(origin, out var domainPerms);
        domainPerms ??= new();

        (string key, string label, string iconPath)[] permTypes =
        {
            ("camera",        "Camera",        "M15 8v8H3V8h2l1-2h6l1 2h2zm-6 6a3 3 0 100-6 3 3 0 000 6z"),
            ("microphone",    "Microphone",    "M12 14a3 3 0 003-3V5a3 3 0 00-6 0v6a3 3 0 003 3zm5-3a5 5 0 01-10 0H5a7 7 0 0014 0h-2zm-5 5v-3"),
            ("location",      "Location",      "M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5a2.5 2.5 0 010-5 2.5 2.5 0 010 5z"),
            ("notifications", "Notifications", "M12 22c1.1 0 2-.9 2-2h-4c0 1.1.9 2 2 2zm6-6v-5c0-3.07-1.64-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5S10.5 3.17 10.5 4v.68C7.63 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z"),
            ("clipboard",     "Clipboard",     "M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2M9 2h6a1 1 0 010 2H9a1 1 0 010-2z")
        };

        foreach (var (key, label, iconPath) in permTypes)
        {
            var rowGrid = new Grid { Margin = new Thickness(14, 4, 14, 4) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new WpfPath { Data = Geometry.Parse(iconPath), Fill = new SolidColorBrush(textSub), Width = 14, Height = 14, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(icon, 0); rowGrid.Children.Add(icon);
            var lbl = new TextBlock { Text = label, Foreground = new SolidColorBrush(textPri), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 1); rowGrid.Children.Add(lbl);

            var currentState = domainPerms.TryGetValue(key, out var s) ? s : "ask";
            var combo = new ComboBox { FontSize = 12, Height = 28, MinWidth = 80, VerticalAlignment = VerticalAlignment.Center };
            combo.Items.Add("Allow"); combo.Items.Add("Block"); combo.Items.Add("Ask (default)");
            combo.SelectedIndex = currentState == "allow" ? 0 : currentState == "block" ? 1 : 2;
            var capturedKey = key;
            combo.SelectionChanged += (s, _) =>
            {
                switch (combo.SelectedIndex)
                {
                    case 0: SaveSitePermission(origin, capturedKey, "allow"); break;
                    case 1: SaveSitePermission(origin, capturedKey, "block"); break;
                    case 2: RemoveSitePermission(origin, capturedKey); break;
                }
            };
            Grid.SetColumn(combo, 2); rowGrid.Children.Add(combo);
            outerStack.Children.Add(rowGrid);
        }

        outerStack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(border1), Margin = new Thickness(0, 8, 0, 0) });

        // Clear cookies button
        var clearBtn = new Border
        {
            CornerRadius = new CornerRadius(8), Margin = new Thickness(14, 8, 14, 14),
            Padding = new Thickness(0, 10, 0, 10), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(20, 0xf2, 0x8b, 0x82)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0xf2, 0x8b, 0x82)),
            BorderThickness = new Thickness(1)
        };
        clearBtn.Child = new TextBlock { Text = "Clear cookies and site data", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f28b82")!), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
        clearBtn.MouseEnter += (s, _) => clearBtn.Background = new SolidColorBrush(Color.FromArgb(50, 0xf2, 0x8b, 0x82));
        clearBtn.MouseLeave += (s, _) => clearBtn.Background = new SolidColorBrush(Color.FromArgb(20, 0xf2, 0x8b, 0x82));
        clearBtn.MouseLeftButtonDown += async (s, _) =>
        {
            try
            {
                // Delete cookies for this domain
                var cookieManager = webView.CoreWebView2.CookieManager;
                var cookies = await cookieManager.GetCookiesAsync($"{uri.Scheme}://{origin}");
                foreach (var ck in cookies) cookieManager.DeleteCookie(ck);
                // Clear cache (global, best we can do in WebView2)
                await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);
                popup.Close();
                webView.Reload();
            }
            catch { popup.Close(); }
        };
        outerStack.Children.Add(clearBtn);

        rootBorder.Child = outerStack;
        popup.Content = rootBorder;
        popup.Deactivated += (s, _) => { if (popup.IsVisible) popup.Close(); };
        popup.Show();
        ForcePopupOnTop(popup);
        TrackPopupPosition(popup, () => { var pt = SecurityIconCanvas.PointToScreen(new Point(0, SecurityIconCanvas.ActualHeight)); return (pt.X - 20, pt.Y + 6); });
    }
    
    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // If a suggestion is selected, navigate to it
            if (SuggestPopup.IsOpen && SuggestionsList.SelectedItem is OmniSuggestion sel)
            {
                NavigateSuggestion(sel);
                e.Handled = true;
                return;
            }
            Navigate(UrlBox.Text);
            SuggestPopup.IsOpen = false;
            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                _tabs[_activeTabIndex].WebView.Focus();
        }
    }

    private void Window_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!SuggestPopup.IsOpen) return;
        if (!OmniboxBorder.IsMouseOver)
            SuggestPopup.IsOpen = false;
    }

    private void UrlBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Mark user as actively editing for keys that modify text
        if (e.Key == Key.Back || e.Key == Key.Delete)
            _userEditingUrl = true;

        if (!SuggestPopup.IsOpen) return;
        if (e.Key == Key.Down)
        {
            var count = SuggestionsList.Items.Count;
            if (count == 0) return;
            SuggestionsList.SelectedIndex = Math.Min((SuggestionsList.SelectedIndex + 1), count - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            SuggestionsList.SelectedIndex = Math.Max(SuggestionsList.SelectedIndex - 1, -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuggestPopup.IsOpen = false;
            e.Handled = true;
        }
    }
    
    private void UrlBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        _userEditingUrl = true;
    }

    private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUrlPlaceholder();
        var text = UrlBox.Text;
        // Only style as URL when user is actively typing
        if (UrlBox.IsFocused)
        {
            var looksLikeUrl = !string.IsNullOrWhiteSpace(text) &&
                               !text.Contains(' ') &&
                               (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                (text.Contains('.') && !text.StartsWith("ycb://")));
            if (looksLikeUrl)
            {
                UrlBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#8ab4f8" : "#1558d6")!);
                UrlBox.TextDecorations = TextDecorations.Underline;
            }
            else
            {
                UrlBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!);
                UrlBox.TextDecorations = null;
            }
        }
        if (!string.IsNullOrWhiteSpace(text) && _userEditingUrl)
            _ = UpdateSuggestionsAsync(text);
        else
            SuggestPopup.IsOpen = false;
    }
    
    private void UrlBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _userEditingUrl = false;
        OmniboxBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#3c4043" : "#ffffff")!);
        UrlPlaceholder.Visibility = Visibility.Collapsed;
        // Reset URL styling so editing starts clean
        UrlBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!);
        UrlBox.TextDecorations = null;
        
        // Show actual URL when focused (for editing), but never expose file:// internal paths
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            var actualUrl = _tabs[_activeTabIndex].WebView?.Source?.ToString() ?? _tabs[_activeTabIndex].Url;
            var displayUrl = GetDisplayUrl(actualUrl);
            if (!string.IsNullOrEmpty(displayUrl))
            {
                UrlBox.Text = displayUrl;
            }
        }
        UrlBox.SelectAll();
    }
    
    private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _userEditingUrl = false;
        // Don't process lost focus when focus moved to BookmarkBtn — it causes star icon flicker
        if (BookmarkBtn.IsKeyboardFocused || BookmarkBtn.IsFocused) return;
        // Don't close suggestions if focus moved into the suggestions list
        if (SuggestionsList.IsKeyboardFocusWithin) return;
        SuggestPopup.IsOpen = false;

        OmniboxBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#292b2f" : "#f1f3f4")!);
        // Clear URL typing style
        UrlBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!);
        UrlBox.TextDecorations = null;
        
        // Show display URL when losing focus
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            var actualUrl = _tabs[_activeTabIndex].WebView?.Source?.ToString() ?? _tabs[_activeTabIndex].Url;
            UrlBox.Text = GetDisplayUrl(actualUrl);
        }
        UpdateUrlPlaceholder();
    }

    private async Task UpdateSuggestionsAsync(string query)
    {
        _suggestCts?.Cancel();
        _suggestCts = new CancellationTokenSource();
        var cts = _suggestCts;

        if (string.IsNullOrWhiteSpace(query))
        {
            SuggestPopup.IsOpen = false;
            return;
        }

        try
        {
            await Task.Delay(150, cts.Token);
            if (cts.IsCancellationRequested) return;

            var suggestions = new List<OmniSuggestion>();

            // History matches (top 3, most recent first)
            var history = LoadHistory();
            var historyMatches = history
                .Where(h => h.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             h.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(h => h.Timestamp)
                .Take(3)
                .Select(h => new OmniSuggestion
                {
                    Primary = h.Url,
                    Secondary = h.Title,
                    NavigateUrl = h.Url,
                    IsHistory = true
                });
            suggestions.AddRange(historyMatches);

            // Google search suggestions (up to 5)
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(3);
                var encoded = Uri.EscapeDataString(query);
                var json = await http.GetStringAsync(
                    $"https://suggestqueries.google.com/complete/search?client=firefox&q={encoded}", cts.Token);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                foreach (var s in doc.RootElement[1].EnumerateArray().Take(5))
                {
                    var text = s.GetString() ?? "";
                    if (string.IsNullOrEmpty(text)) continue;
                    suggestions.Add(new OmniSuggestion
                    {
                        Primary = text,
                        NavigateUrl = GetSearchUrl(text),
                        IsHistory = false
                    });
                }
            }
            catch { /* ignore network failures */ }

            if (cts.IsCancellationRequested) return;

            SuggestionsList.ItemsSource = suggestions;
            SuggestionsList.SelectedIndex = -1;
            SuggestPopup.IsOpen = suggestions.Count > 0;

            if (suggestions.Count > 0)
            {
                // Start or reset a short timer to close suggestions when focus moves away (handles clicks into WebView HWND)
                if (_suggestCloseTimer == null)
                {
                    _suggestCloseTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(150)
                    };
                    _suggestCloseTimer.Tick += (ts, te) =>
                    {
                        try
                        {
                            if (!UrlBox.IsKeyboardFocused && !SuggestionsList.IsKeyboardFocusWithin && !OmniboxBorder.IsMouseOver && !BookmarkBtn.IsKeyboardFocused && !BookmarkBtn.IsMouseOver)
                            {
                                SuggestPopup.IsOpen = false;
                                _suggestCloseTimer?.Stop();
                            }
                        }
                        catch { }
                    };
                }
                _suggestCloseTimer.Stop();
                _suggestCloseTimer.Start();
            }
            else
            {
                _suggestCloseTimer?.Stop();
            }
        }
        catch (TaskCanceledException) { }
    }

    private void NavigateSuggestion(OmniSuggestion s)
    {
        SuggestPopup.IsOpen = false;
        Navigate(s.NavigateUrl);
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
            _tabs[_activeTabIndex].WebView.Focus();
    }

    private void SuggestionsList_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe &&
            fe.DataContext is OmniSuggestion s)
        {
            NavigateSuggestion(s);
            e.Handled = true;
        }
    }

    private void SuggestionsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SuggestionsList.SelectedItem is OmniSuggestion s)
        {
            NavigateSuggestion(s);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuggestPopup.IsOpen = false;
            UrlBox.Focus();
            e.Handled = true;
        }
    }

    private void BookmarkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count) return;

        var tab = _tabs[_activeTabIndex];
        var url = tab.WebView?.Source?.ToString() ?? tab.Url ?? "";

        // Never bookmark system / internal pages
        if (string.IsNullOrEmpty(url) || url.StartsWith("ycb://") || url.StartsWith("file:///"))
        {
            BookmarkBtn.ToolTip = "Can't bookmark this page";
            return;
        }

        BookmarkBtn.ToolTip = "Bookmark this tab";
        var bookmarks = LoadBookmarks();
        var existing = bookmarks.FindIndex(b => b.Url == url);

        if (existing >= 0)
        {
            // Already bookmarked — remove it
            RemoveBookmark(existing);
            UpdateBookmarkStar(false);
            UpdateBookmarksBar();
        }
        else
        {
            // Add new bookmark
            var title = tab.Title ?? url;
            AddBookmark(url, title);
            UpdateBookmarkStar(true);
            UpdateBookmarksBar();
        }

        // Return focus to the WebView so the page doesn't flicker
        tab.WebView?.Focus();
    }

    /// <summary>Updates the star icon to filled (bookmarked) or hollow (not bookmarked).</summary>
    private void UpdateBookmarkStar(bool isBookmarked)
    {
        if (BookmarkStarPath == null) return;
        if (isBookmarked)
        {
            BookmarkStarPath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#fabd05")!);
            BookmarkStarPath.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#fabd05")!);
        }
        else
        {
            BookmarkStarPath.Fill = Brushes.Transparent;
            BookmarkStarPath.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#9aa0a6" : "#5f6368")!);
        }
    }

    /// <summary>Called whenever the active tab URL changes — syncs the star icon state.</summary>
    private void RefreshBookmarkStar()
    {
        if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count) { UpdateBookmarkStar(false); return; }
        var url = _tabs[_activeTabIndex].WebView?.Source?.ToString() ?? _tabs[_activeTabIndex].Url ?? "";
        bool isInternal = string.IsNullOrEmpty(url) || url.StartsWith("ycb://") || url.StartsWith("file:///");

        if (isInternal)
        {
            UpdateBookmarkStar(false);
            BookmarkBtn.IsEnabled = false;
            BookmarkBtn.Opacity = 0.4;
            return;
        }
        BookmarkBtn.IsEnabled = true;
        BookmarkBtn.Opacity = 1.0;
        var bookmarks = LoadBookmarks();
        UpdateBookmarkStar(bookmarks.Any(b => b.Url == url));
    }
    
    private string GetDisplayUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        
        // For all internal ycb:// pages, return empty so the placeholder shows the page name
        if (url.StartsWith("ycb://")) return "";
        
        // Also hide file:// paths for internal renderer pages — return empty string
        if (url.StartsWith("file:///") &&
            (url.Contains("/renderer/") || url.Contains("\\renderer\\")))
        {
            return "";
        }
        
        return url;
    }
    
    private string GetSystemPageName(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.StartsWith("ycb://"))
        {
            return url switch
            {
                "ycb://newtab"    => "",
                "ycb://settings"  => "Settings",
                "ycb://history"   => "History",
                "ycb://downloads" => "Downloads",
                "ycb://passwords" => "Password Manager",
                "ycb://bookmarks" => "Bookmarks",
                "ycb://guide"     => "Help & Guide",
                _ => ""
            };
        }
        if (url.StartsWith("file:///"))
        {
            if (url.Contains("newtab.html"))    return "";
            if (url.Contains("settings.html"))  return "Settings";
            if (url.Contains("history.html"))   return "History";
            if (url.Contains("downloads.html")) return "Downloads";
            if (url.Contains("passwords.html")) return "Password Manager";
            if (url.Contains("bookmarks.html")) return "Bookmarks";
            if (url.Contains("guide.html"))     return "Help & Guide";
        }
        return "";
    }
    
    private void UpdateUrlPlaceholder()
    {
        // On a system page, show the page name as the placeholder
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            var currentUrl = _tabs[_activeTabIndex].WebView?.Source?.ToString()
                             ?? _tabs[_activeTabIndex].Url ?? "";
            var pageName = GetSystemPageName(currentUrl);
            if (!string.IsNullOrEmpty(pageName))
            {
                UrlPlaceholder.Text = pageName;
                UrlPlaceholder.Visibility = string.IsNullOrEmpty(UrlBox.Text) && !UrlBox.IsFocused ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
        }

        // Normal page — show search engine prompt
        var searchText = _searchEngine switch
        {
            "bing"       => "Search with Bing",
            "duckduckgo" => "Search with DuckDuckGo",
            "ecosia"     => "Search with Ecosia",
            "brave"      => "Search with Brave",
            "yahoo"      => "Search with Yahoo",
            _            => "Search with Google"
        };
        UrlPlaceholder.Text = searchText;
        UrlPlaceholder.Visibility = string.IsNullOrEmpty(UrlBox.Text) && !UrlBox.IsFocused ? Visibility.Visible : Visibility.Collapsed;
    }
    
    private void AttachImage_Click(object sender, RoutedEventArgs e)
    {
        ImagePickerPopup.IsOpen = !ImagePickerPopup.IsOpen;
    }

    private async void TakeScreenshot_Click(object sender, RoutedEventArgs e)
    {
        ImagePickerPopup.IsOpen = false;
        if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count) return;
        var webView = _tabs[_activeTabIndex].WebView;
        if (webView?.CoreWebView2 == null) return;

        var path = IoPath.Combine(IoPath.GetTempPath(), $"ycb_screenshot_{DateTime.Now:yyyyMMddHHmmss}.png");
        using (var stream = new FileStream(path, FileMode.Create))
        {
            await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }

        _attachedImagePath = path;
        ImageAttachName.Text = "📸 Screenshot";
        ImageAttachIndicator.Visibility = Visibility.Visible;
    }

    private void OpenGallery_Click(object sender, RoutedEventArgs e)
    {
        ImagePickerPopup.IsOpen = false;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _attachedImagePath = dlg.FileName;
            ImageAttachName.Text = "📎 " + IoPath.GetFileName(dlg.FileName);
            ImageAttachIndicator.Visibility = Visibility.Visible;
        }
    }

    private void RemoveAttachedImage_Click(object sender, RoutedEventArgs e)
    {
        _attachedImagePath = null;
        ImageAttachIndicator.Visibility = Visibility.Collapsed;
    }

    private void Copilot_Click(object sender, RoutedEventArgs e)
    {
        // Block Copilot in incognito mode unless setting enabled
        if (_isIncognito && !(_settings.IncognitoAIEnabled ?? false))
        {
            MessageBox.Show("AI/Copilot is disabled in Incognito mode.\nYou can enable it in Settings > Privacy > Allow AI in Incognito.", 
                "Incognito Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        _copilotVisible = !_copilotVisible;
        CopilotSidebar.Visibility = _copilotVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = _copilotVisible ? new GridLength(340) : new GridLength(0);
    }
    
    private void CloseCopilot_Click(object sender, RoutedEventArgs e)
    {
        _copilotVisible = false;
        CopilotSidebar.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
    }
    
    private void CopilotInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendCopilot_Click(sender, e);
        }
    }
    
    private async void SendCopilot_Click(object sender, RoutedEventArgs e)
    {
        var message = CopilotInput.Text.Trim();
        if (string.IsNullOrEmpty(message) && _attachedImagePath == null) return;
        
        var displayMessage = message;
        if (_attachedImagePath != null)
            displayMessage = (string.IsNullOrEmpty(message) ? "" : message + "\n") + "📎 " + System.IO.Path.GetFileName(_attachedImagePath);
        
        AddCopilotMessage(string.IsNullOrEmpty(displayMessage) ? "📎 Image attached" : displayMessage, true);
        _chatHistory.Add(new ChatMessage { Role = "user", Content = displayMessage });
        CopilotInput.Text = "";
        CopilotInput.IsEnabled = false;
        
        var imagePath = _attachedImagePath;
        _attachedImagePath = null;
        ImageAttachIndicator.Visibility = Visibility.Collapsed;
        
        // Get current URL
        var currentUrl = "";
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            currentUrl = _tabs[_activeTabIndex].WebView?.Source?.ToString() ?? "";
        }
        
        // Find copilot.exe
        var copilotExe = FindCopilotExe();
        if (string.IsNullOrEmpty(copilotExe))
        {
            AddCopilotMessage("Copilot CLI not found. Please install GitHub Copilot CLI.", false);
            CopilotInput.IsEnabled = true;
            return;
        }
        
        // Build prompt with context-aware URL handling
        var prompt = "You are a helpful browser assistant built into YCB Browser. Be concise and helpful. ";
        if (!string.IsNullOrEmpty(currentUrl) && currentUrl != "about:blank")
        {
            string pageContext;
            if (currentUrl.StartsWith("ycb://") || currentUrl.Contains("ycb://"))
            {
                // Map internal pages to friendly names
                if (currentUrl.Contains("settings")) pageContext = "the YCB Settings page";
                else if (currentUrl.Contains("history")) pageContext = "the YCB History page";
                else if (currentUrl.Contains("downloads")) pageContext = "the YCB Downloads page";
                else if (currentUrl.Contains("passwords")) pageContext = "the YCB Passwords page";
                else if (currentUrl.Contains("newtab")) pageContext = "the YCB New Tab page";
                else if (currentUrl.Contains("guide")) pageContext = "the YCB Guide page";
                else if (currentUrl.Contains("support")) pageContext = "the YCB Support page";
                else pageContext = "a YCB Browser internal page";
                prompt += $"The user is on {pageContext}. ";
            }
            else if (currentUrl.StartsWith("file:///"))
            {
                // Silently suppress raw file paths — shouldn't reach here but just in case
            }
            else
            {
                // Regular web page — show domain only to avoid exposing full private URLs
                try
                {
                    var host = new Uri(currentUrl).Host;
                    prompt += $"The user is on {host}. ";
                }
                catch
                {
                    // ignore bad URLs
                }
            }
        }
        prompt += "IMPORTANT: If the user asks you to open, navigate to, or visit a website, include [OPEN_URL: https://example.com] in your response (with the full URL).\n\n";
        
        // Add recent history
        var recent = _chatHistory.TakeLast(6).ToList();
        if (recent.Count > 0)
        {
            prompt += "Previous conversation:\n";
            foreach (var h in recent)
            {
                var role = h.Role == "user" ? "User" : "Assistant";
                prompt += $"{role}: {h.Content}\n";
            }
            prompt += "\n";
        }
        
        if (imagePath != null)
        {
            var userText = string.IsNullOrEmpty(message) ? "Describe everything you see in this image." : message;
            var imagePrompt = $"Please view this image: {imagePath}\nThe above line is ONLY the image file path — do not mention the path in your response, it is just so you can load the image. Do not describe or narrate what actions you are taking. Just directly answer: {userText}";
            
            var responseBorder2 = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#24263a" : "#f1f3f6")!),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(13, 10, 13, 10),
                Margin = new Thickness(0, 5, 40, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 280,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
                BorderThickness = new Thickness(2, 0, 0, 0),
                Opacity = 0.7
            };
            _currentResponseBlock = new TextBlock
            {
                Text = "Thinking...",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            };
            responseBorder2.Child = _currentResponseBlock;
            MessagesPanel.Children.Add(responseBorder2);
            CopilotMessages.ScrollToEnd();

            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = copilotExe,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                si.ArgumentList.Add("-p");
                si.ArgumentList.Add(imagePrompt);
                si.ArgumentList.Add("--model");
                si.ArgumentList.Add(_settings.YcbModel ?? "gpt-5-mini");
                si.ArgumentList.Add("-s");
                si.ArgumentList.Add("--no-ask-user");
                si.ArgumentList.Add("--allow-all-paths");
                _copilotProcess = new Process { StartInfo = si };
                _copilotProcess.EnableRaisingEvents = true;
                _copilotProcess.Start();
                using var ms1 = new System.IO.MemoryStream();
                await _copilotProcess.StandardOutput.BaseStream.CopyToAsync(ms1);
                await _copilotProcess.WaitForExitAsync();
                var frText = System.Text.Encoding.UTF8.GetString(ms1.ToArray()).Trim();
                Dispatcher.Invoke(() =>
                {
                    if (_currentResponseBlock != null)
                        _currentResponseBlock.Text = string.IsNullOrWhiteSpace(frText) ? "No response." : frText;
                    if (_currentResponseBlock?.Parent is Border b2)
                    {
                        b2.CornerRadius = new CornerRadius(14, 14, 14, 3);
                        b2.BorderThickness = new Thickness(0);
                        b2.Opacity = 1.0;
                    }
                    CopilotInput.IsEnabled = true;
                    if (!string.IsNullOrWhiteSpace(frText))
                        _chatHistory.Add(new ChatMessage { Role = "assistant", Content = frText });
                    _copilotProcess = null;
                    CopilotMessages.ScrollToEnd();
                });
            }
            catch (Exception ex)
            {
                _currentResponseBlock!.Text = $"Error: {ex.Message}";
                CopilotInput.IsEnabled = true;
            }
            return;
        }
        
        prompt += $"User: {message}";
        
        // Create response message placeholder
        var responseBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#24263a" : "#f1f3f6")!),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(13, 10, 13, 10),
            Margin = new Thickness(0, 5, 40, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 280,
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Opacity = 0.7
        };
        
        _currentResponseBlock = new TextBlock
        {
            Text = "Thinking...",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };
        responseBorder.Child = _currentResponseBlock;
        MessagesPanel.Children.Add(responseBorder);
        CopilotMessages.ScrollToEnd();
        
        // Start copilot process
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = copilotExe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(prompt);
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(_settings.YcbModel ?? "gpt-5-mini");
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add("--no-ask-user");
            
            _copilotProcess = new Process { StartInfo = startInfo };
            _copilotProcess.EnableRaisingEvents = true;
            _copilotProcess.Start();
            using var ms2 = new System.IO.MemoryStream();
            await _copilotProcess.StandardOutput.BaseStream.CopyToAsync(ms2);
            await _copilotProcess.WaitForExitAsync();
            var response = System.Text.Encoding.UTF8.GetString(ms2.ToArray()).Trim();

            Dispatcher.Invoke(() =>
            {
                CopilotInput.IsEnabled = true;
                var finalText = string.IsNullOrWhiteSpace(response) ? "No response." : response;
                if (_currentResponseBlock?.Parent is Border rb)
                {
                    rb.CornerRadius = new CornerRadius(14, 14, 14, 3);
                    rb.BorderThickness = new Thickness(0);
                    rb.Opacity = 1.0;
                    rb.Child = BuildAssistantMessageUI(finalText);
                }
                _currentResponseBlock = null;
                if (!string.IsNullOrWhiteSpace(response))
                {
                    _chatHistory.Add(new ChatMessage { Role = "assistant", Content = response });
                    var urlMatches = System.Text.RegularExpressions.Regex.Matches(response, @"\[OPEN_URL:\s*(https?://[^\]]+)\]");
                    foreach (System.Text.RegularExpressions.Match match in urlMatches)
                    {
                        var urlToOpen = match.Groups[1].Value.Trim();
                        _ = CreateTab(urlToOpen);
                    }
                }
                _copilotProcess = null;
                CopilotMessages.ScrollToEnd();
            });
        }
        catch (Exception ex)
        {
            _currentResponseBlock!.Text = $"Failed to start Copilot: {ex.Message}";
            _currentResponseBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f28b82")!);
            CopilotInput.IsEnabled = true;
        }
    }

    private async Task SendImageToVision(string imagePath, string userMessage)
    {
        // Show placeholder
        var responseBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#24263a" : "#f1f3f6")!),
            CornerRadius = new CornerRadius(14, 14, 14, 3),
            Padding = new Thickness(13, 10, 13, 10),
            Margin = new Thickness(0, 5, 40, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 280
        };
        _currentResponseBlock = new TextBlock
        {
            Text = "Analysing image...",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };
        responseBorder.Child = _currentResponseBlock;
        MessagesPanel.Children.Add(responseBorder);
        CopilotMessages.ScrollToEnd();

        try
        {
            // Get GitHub token via gh CLI (same login used by Copilot)
            var tokenProc = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = "gh", Arguments = "auth token",
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
            }};
            tokenProc.Start();
            var token = (await tokenProc.StandardOutput.ReadToEndAsync()).Trim();
            tokenProc.WaitForExit(3000);

            if (string.IsNullOrEmpty(token))
            {
                _currentResponseBlock.Text = "Not logged in to GitHub. Run 'gh auth login' and try again.";
                _currentResponseBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f28b82")!);
                CopilotInput.IsEnabled = true;
                return;
            }

            // Base64-encode the image
            var bytes = await File.ReadAllBytesAsync(imagePath);
            var b64 = Convert.ToBase64String(bytes);
            var mime = IoPath.GetExtension(imagePath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif"  => "image/gif",
                ".webp" => "image/webp",
                _       => "image/png"
            };

            // Build JSON request
            var body = JsonSerializer.Serialize(new
            {
                model = "gpt-4o",
                messages = new object[]
                {
                    new { role = "system", content = "You are a helpful browser assistant built into YCB." },
                    new { role = "user", content = new object[]
                        {
                            new { type = "image_url", image_url = new { url = $"data:{mime};base64,{b64}" } },
                            new { type = "text", text = userMessage }
                        }
                    }
                }
            });

            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var resp = await http.PostAsync(
                "https://models.inference.ai.azure.com/chat/completions",
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            var json = await resp.Content.ReadAsStringAsync();

            string reply;
            try
            {
                using var doc = JsonDocument.Parse(json);
                reply = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "No response.";
            }
            catch { reply = $"Error {(int)resp.StatusCode}: {json}"; }

            _currentResponseBlock.Text = reply;
            _chatHistory.Add(new ChatMessage { Role = "assistant", Content = reply });
        }
        catch (Exception ex)
        {
            _currentResponseBlock.Text = $"Vision error: {ex.Message}";
            _currentResponseBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f28b82")!);
        }
        finally { CopilotInput.IsEnabled = true; }
    }

    // The copilot CLI outputs UTF-8 bytes, but .NET reads them as the system default
    // encoding (CP1252 on Windows), producing mojibake like â€" instead of —.
    // Fix: re-encode the mis-decoded string back to CP1252 bytes, then decode as UTF-8.
    private static string FixCopilotEncoding(string raw)
    {
        try
        {
            var cp1252 = System.Text.Encoding.GetEncoding(1252);
            var bytes = cp1252.GetBytes(raw);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch { return raw; }
    }

    private void AdBlock_Click(object sender, RoutedEventArgs e)
    {
        var tab = _tabs.ElementAtOrDefault(_activeTabIndex);
        if (!Uri.TryCreate(tab?.Url ?? "", UriKind.Absolute, out var uri)) return;
        var host = uri.Host;
        if (_adBlockDisabledSites.Contains(host))
            _adBlockDisabledSites.Remove(host);
        else
            _adBlockDisabledSites.Add(host);
        _settings.AdBlockerDisabledSites = _adBlockDisabledSites.ToList();
        SaveSettings();
        UpdateAdBlockButton();
    }

    private void UpdateAdBlockButton()
    {
        var tab = _tabs.ElementAtOrDefault(_activeTabIndex);
        var url = tab?.Url ?? "";
        var isHttp = url.StartsWith("http://") || url.StartsWith("https://");
        AdBlockBtn.Visibility = (_settings.AdBlockerEnabled && isHttp) ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.AdBlockerEnabled || !isHttp) return;

        Uri.TryCreate(url, UriKind.Absolute, out var uri);
        var host = uri?.Host ?? "";
        var siteOn = !_adBlockDisabledSites.Contains(host);
        var color = siteOn ? "#81c995" : "#9aa0a6";
        var brush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        AdBlockShieldBody.Stroke = brush;
        AdBlockShieldMark.Stroke = brush;
        AdBlockShieldMark.Data = System.Windows.Media.Geometry.Parse(
            siteOn ? "M6 9 L8 11 L12 6.5" : "M6.5 6.5 L11.5 11.5 M11.5 6.5 L6.5 11.5");
        AdBlockBtn.ToolTip = siteOn
            ? "Ad blocker: ON for this site (click to disable)"
            : "Ad blocker: OFF for this site (click to re-enable)";
    }

    private static readonly string[] _adBlockDomains =
    [
        // Google Ads
        "*://googlesyndication.com/*", "*://*.googlesyndication.com/*",
        "*://doubleclick.net/*",       "*://*.doubleclick.net/*",
        "*://googleadservices.com/*",  "*://*.googleadservices.com/*",
        "*://pagead2.googlesyndication.com/*",
        "*://adservice.google.com/*",  "*://*.adservice.google.*/*",
        "*://googletagmanager.com/*",  "*://*.googletagmanager.com/*",
        "*://googletagservices.com/*", "*://*.googletagservices.com/*",
        // Ad networks
        "*://adnxs.com/*",             "*://*.adnxs.com/*",
        "*://amazon-adsystem.com/*",   "*://*.amazon-adsystem.com/*",
        "*://media.net/*",             "*://*.media.net/*",
        "*://pubmatic.com/*",          "*://*.pubmatic.com/*",
        "*://openx.net/*",             "*://*.openx.net/*",
        "*://rubiconproject.com/*",    "*://*.rubiconproject.com/*",
        "*://casalemedia.com/*",       "*://*.casalemedia.com/*",
        "*://adsrvr.org/*",            "*://*.adsrvr.org/*",
        "*://moatads.com/*",           "*://*.moatads.com/*",
        "*://yieldmo.com/*",           "*://*.yieldmo.com/*",
        "*://criteo.com/*",            "*://*.criteo.com/*",
        "*://taboola.com/*",           "*://*.taboola.com/*",
        "*://outbrain.com/*",          "*://*.outbrain.com/*",
        "*://revcontent.com/*",        "*://*.revcontent.com/*",
        "*://lijit.com/*",             "*://*.lijit.com/*",
        "*://advertising.com/*",       "*://*.advertising.com/*",
        "*://adtech.com/*",            "*://*.adtech.com/*",
        "*://bidswitch.net/*",         "*://*.bidswitch.net/*",
        "*://contextweb.com/*",        "*://*.contextweb.com/*",
        "*://sharethrough.com/*",      "*://*.sharethrough.com/*",
        "*://triplelift.com/*",        "*://*.triplelift.com/*",
        "*://33across.com/*",          "*://*.33across.com/*",
        "*://sovrn.com/*",             "*://*.sovrn.com/*",
        "*://smartadserver.com/*",     "*://*.smartadserver.com/*",
        "*://teads.tv/*",              "*://*.teads.tv/*",
        "*://spotxchange.com/*",       "*://*.spotxchange.com/*",
        "*://spotx.tv/*",              "*://*.spotx.tv/*",
        "*://undertone.com/*",         "*://*.undertone.com/*",
        "*://adroll.com/*",            "*://*.adroll.com/*",
        "*://perfectmarket.com/*",     "*://*.perfectmarket.com/*",
        "*://mediavine.com/*",         "*://*.mediavine.com/*",
        // Trackers & analytics
        "*://google-analytics.com/*",  "*://*.google-analytics.com/*",
        "*://analytics.google.com/*",  "*://click.googleanalytics.com/*",
        "*://ssl.google-analytics.com/*",
        "*://connect.facebook.net/*",  "*://www.facebook.com/tr/*",
        "*://hotjar.com/*",            "*://*.hotjar.com/*",
        "*://hotjar.io/*",             "*://*.hotjar.io/*",
        "*://mouseflow.com/*",         "*://*.mouseflow.com/*",
        "*://mixpanel.com/*",          "*://*.mixpanel.com/*",
        "*://segment.com/*",           "*://*.segment.io/*",
        "*://amplitude.com/*",         "*://*.amplitude.com/*",
        "*://clarity.ms/*",            "*://*.clarity.ms/*",
        "*://scorecardresearch.com/*", "*://*.scorecardresearch.com/*",
        "*://comscore.com/*",          "*://*.comscore.com/*",
        "*://krxd.net/*",              "*://*.krxd.net/*",
        "*://chartbeat.com/*",         "*://*.chartbeat.com/*",
        "*://quantserve.com/*",        "*://*.quantserve.com/*",
        "*://everesttech.net/*",       "*://*.everesttech.net/*",
        "*://statcounter.com/*",       "*://*.statcounter.com/*",
        "*://mc.yandex.ru/*",          "*://metrika.yandex.ru/*",
        "*://newrelic.com/*",          "*://*.newrelic.com/*",
        "*://nr-data.net/*",           "*://*.nr-data.net/*",
        "*://heap.io/*",               "*://*.heapanalytics.com/*",
        "*://intercom.io/*",           "*://*.intercom.com/*",
        "*://crazyegg.com/*",          "*://*.crazyegg.com/*",
        "*://luckyorange.com/*",       "*://*.luckyorange.com/*",
        "*://inspectlet.com/*",        "*://*.inspectlet.com/*",
        "*://clicky.com/*",            "*://*.clicky.com/*",
        "*://woopra.com/*",            "*://*.woopra.com/*",
        // Error/session trackers
        "*://sentry.io/*",             "*://*.sentry.io/*",
        "*://browser.sentry-cdn.com/*","*://js.sentry-cdn.com/*",
        "*://bugsnag.com/*",           "*://*.bugsnag.com/*",
        "*://bugsnag-builds.s3.amazonaws.com/*",
        "*://d2wy8f7a9ursnm.cloudfront.net/*",
        "*://logrocket.com/*",         "*://*.logrocket.com/*",
        "*://fullstory.com/*",         "*://*.fullstory.com/*",
        "*://datadoghq.com/*",         "*://*.datadoghq.com/*",
        "*://datadog-browser-agent.com/*",
        "*://js-agent.newrelic.com/*", "*://bam.nr-data.net/*",
        "*://cdn.rollbar.com/*",       "*://*.rollbar.com/*",
        "*://raygun.com/*",            "*://*.raygun.com/*",
        "*://az416426.vo.msecnd.net/*",
        // Turtlecute adblock test domains — exact subdomains
        // Ads
        "*://adtago.s3.amazonaws.com/*", "*://analyticsengine.s3.amazonaws.com/*",
        "*://analytics.s3.amazonaws.com/*", "*://advice-ads.s3.amazonaws.com/*",
        "*://adcolony.com/*", "*://*.adcolony.com/*",
        "*://ads30.adcolony.com/*", "*://adc3-launch.adcolony.com/*",
        "*://events3alt.adcolony.com/*", "*://wd.adcolony.com/*",
        "*://pagead2.googlesyndication.com/*", "*://afs.googlesyndication.com/*",
        "*://adservice.google.com/*", "*://pagead2.googleadservices.com/*",
        "*://stats.g.doubleclick.net/*", "*://ad.doubleclick.net/*",
        "*://static.doubleclick.net/*", "*://m.doubleclick.net/*",
        "*://mediavisor.doubleclick.net/*",
        "*://static.media.net/*", "*://adservetx.media.net/*",
        // Analytics — exact turtlecute subdomains
        "*://freshmarketer.com/*", "*://*.freshmarketer.com/*",
        "*://claritybt.freshmarketer.com/*", "*://fwtracks.freshmarketer.com/*",
        "*://*.luckyorange.net/*", "*://stats.wp.com/*",
        "*://api.luckyorange.com/*", "*://realtime.luckyorange.com/*",
        "*://cdn.luckyorange.com/*", "*://w1.luckyorange.com/*",
        "*://upload.luckyorange.net/*", "*://cs.luckyorange.net/*",
        "*://settings.luckyorange.net/*",
        "*://adm.hotjar.com/*", "*://identify.hotjar.com/*",
        "*://insights.hotjar.com/*", "*://script.hotjar.com/*",
        "*://surveys.hotjar.com/*", "*://careers.hotjar.com/*",
        "*://events.hotjar.io/*",
        "*://cdn.mouseflow.com/*", "*://o2.mouseflow.com/*",
        "*://gtm.mouseflow.com/*", "*://api.mouseflow.com/*",
        "*://tools.mouseflow.com/*", "*://cdn-test.mouseflow.com/*",
        "*://analytics.google.com/*", "*://click.googleanalytics.com/*",
        "*://ssl.google-analytics.com/*",
        "*://app.getsentry.com/*", "*://browser.sentry-cdn.com/*",
        "*://notify.bugsnag.com/*", "*://sessions.bugsnag.com/*",
        "*://api.bugsnag.com/*", "*://app.bugsnag.com/*",
        // FreshWorks full suite
        "*://freshworks.com/*", "*://*.freshworks.com/*",
        "*://freshdesk.com/*",  "*://*.freshdesk.com/*",
        "*://freshchat.com/*",  "*://*.freshchat.com/*",
        "*://wchat.freshchat.com/*", "*://api.freshchat.com/*",
        // Social trackers
        "*://static.ads-twitter.com/*", "*://ads-api.twitter.com/*",
        "*://*.ads-twitter.com/*",
        "*://ads.linkedin.com/*", "*://analytics.pointdrive.linkedin.com/*",
        "*://ads.pinterest.com/*", "*://log.pinterest.com/*", "*://trk.pinterest.com/*",
        "*://events.reddit.com/*", "*://events.redditmedia.com/*",
        "*://ads.youtube.com/*",
        "*://ads-api.tiktok.com/*", "*://analytics.tiktok.com/*",
        "*://ads-sg.tiktok.com/*", "*://analytics-sg.tiktok.com/*",
        "*://business-api.tiktok.com/*", "*://ads.tiktok.com/*",
        "*://log.byteoversea.com/*",
        // Yahoo / Yandex / Unity
        "*://ads.yahoo.com/*", "*://analytics.yahoo.com/*", "*://geo.yahoo.com/*", "*://udcm.yahoo.com/*",
        "*://analytics.query.yahoo.com/*", "*://partnerads.ysm.yahoo.com/*",
        "*://log.fc.yahoo.com/*", "*://gemini.yahoo.com/*", "*://adtech.yahooinc.com/*",
        "*://extmaps-api.yandex.net/*", "*://appmetrica.yandex.ru/*",
        "*://adfstat.yandex.ru/*", "*://offerwall.yandex.net/*", "*://adfox.yandex.ru/*",
        "*://auction.unityads.unity3d.com/*", "*://webview.unityads.unity3d.com/*",
        "*://config.unityads.unity3d.com/*", "*://adserver.unityads.unity3d.com/*",
        // OEM trackers
        "*://iot-eu-logser.realme.com/*", "*://iot-logser.realme.com/*",
        "*://bdapi-ads.realmemobile.com/*", "*://bdapi-in-ads.realmemobile.com/*",
        "*://api.ad.xiaomi.com/*", "*://data.mistat.xiaomi.com/*",
        "*://data.mistat.india.xiaomi.com/*", "*://data.mistat.rus.xiaomi.com/*",
        "*://sdkconfig.ad.xiaomi.com/*", "*://sdkconfig.ad.intl.xiaomi.com/*",
        "*://tracking.rus.miui.com/*",
        "*://adsfs.oppomobile.com/*", "*://adx.ads.oppomobile.com/*",
        "*://ck.ads.oppomobile.com/*", "*://data.ads.oppomobile.com/*",
        "*://metrics.data.hicloud.com/*", "*://metrics2.data.hicloud.com/*",
        "*://grs.hicloud.com/*", "*://logservice.hicloud.com/*",
        "*://logservice1.hicloud.com/*", "*://logbak.hicloud.com/*",
        "*://click.oneplus.cn/*", "*://open.oneplus.net/*",
        "*://samsungads.com/*", "*://smetrics.samsung.com/*",
        "*://nmetrics.samsung.com/*", "*://samsung-com.112.2o7.net/*",
        "*://analytics-api.samsunghealthcn.com/*",
        "*://iadsdk.apple.com/*", "*://metrics.icloud.com/*",
        "*://metrics.mzstatic.com/*", "*://api-adservices.apple.com/*",
        "*://books-analytics-events.apple.com/*",
        "*://weather-analytics-events.apple.com/*",
        "*://notes-analytics-events.apple.com/*",
        // Additional high-value ad/tracking domains
        "*://bat.bing.com/*",          "*://c.bing.com/*",
        "*://adclick.g.doubleclick.net/*",
        "*://www.googleadservices.com/*",
        "*://tpc.googlesyndication.com/*",
        "*://surveymonkey.com/*", "*://*.surveymonkey.com/*",
        "*://zopim.com/*", "*://*.zopim.com/*",
        "*://snap.licdn.com/*", "*://platform.linkedin.com/analytics/*",
        "*://ct.pinterest.com/*",
        "*://alb.reddit.com/*",
        "*://pixel.reddit.com/*",
        "*://www.redditstatic.com/ads/*",
        "*://t.co/i/*",
        "*://jwpltx.com/*", "*://*.jwpltx.com/*",
        "*://jwpsrv.com/*", "*://*.jwpsrv.com/*",
        "*://freewheel.tv/*", "*://*.freewheel.tv/*",
        "*://cdn.flashtalking.com/*",
        "*://go.sonobi.com/*",
        "*://c2.taboola.com/*", "*://trc.taboola.com/*",
        "*://syndication.twitter.com/i/*",
        "*://t.myvisualiq.net/*",
        "*://secure.insightexpressai.com/*",
        "*://cdn.optimizely.com/*", "*://*.optimizely.com/*",
        "*://d.turn.com/*", "*://rpm.turn.com/*",
        "*://*.demdex.net/*",
        "*://*.bluekai.com/*",
        "*://*.exelator.com/*",
        "*://addthis.com/*", "*://*.addthis.com/*",
        "*://sharethis.com/*", "*://*.sharethis.com/*",
        "*://stickyadstv.com/*", "*://*.stickyadstv.com/*",
        "*://*.serving-sys.com/*",
        "*://ads.rubiconproject.com/*",
        "*://eb2.3lift.com/*",
        "*://sync.mathtag.com/*", "*://*.mathtag.com/*",
        "*://ads.yap.yahoo.com/*",
        "*://udc.yahoo.com/*",
        "*://munchkin.marketo.net/*", "*://munchkin.marketo.com/*",
        "*://js.hs-analytics.net/*", "*://js.hsforms.net/*", "*://js.hscta.net/*",
        "*://js.hubspot.com/*",  "*://*.hubspot.com/analytics/*",
        "*://pardot.com/*", "*://*.pardot.com/*",
        "*://marketo.com/*", "*://*.marketo.com/*",
        "*://eloqua.com/*", "*://*.eloqua.com/*",
        "*://bat.r.msn.com/*",
        "*://*.adsafeprotected.com/*",
        "*://*.doubleverify.com/*",
        "*://*.integral-assets.com/*",
        "*://*.adsafe.net/*",
        "*://ib.adnxs.com/*",
        "*://secure.adnxs.com/*",
        "*://aax.amazon-adsystem.com/*",
        "*://fls-na.amazon-adsystem.com/*",
        "*://c.amazon-adsystem.com/*",
        "*://*.demdex.net/*",
        // Missing domains from test results (87 accessible domains now blocked)
        "*://aan.amazon.com/*",
        "*://static.criteo.net/*",
        "*://mgid.com/*", "*://cdn.mgid.com/*", "*://servicer.mgid.com/*",
        "*://bingads.microsoft.com/*",
        "*://ads.microsoft.com/*",
        "*://liftoff.io/*",
        "*://cdn.indexexchange.com/*",
        "*://smartyads.com/*",
        "*://ad.gt/*",
        "*://eb2.3lift.com/*", "*://tlx.3lift.com/*", "*://apex.go.sonobi.com/*",
        "*://cdn.kargo.com/*",
        "*://sync.kargo.com/*",
        "*://pangleglobal.com/*",
        "*://s.youtube.com/*", "*://redirector.googlevideo.com/*", "*://youtubei.googleapis.com/*",
        "*://graph.facebook.com/*", "*://tr.facebook.com/*",
        "*://sc-analytics.appspot.com/*",
        "*://d.reddit.com/*",
        "*://ads-api.x.com/*", "*://ads.x.com/*",
        "*://pixel.quora.com/*",
        "*://px.srvcs.tumblr.com/*",
        "*://ads.vk.com/*", "*://vk.com/rtrg/*",
        "*://ad.mail.ru/*", "*://top-fwz1.mail.ru/*",
        "*://xp.apple.com/*",
        "*://ads.huawei.com/*",
        "*://data.mistat.india.xiaomi.com/*", "*://data.mistat.rus.xiaomi.com/*", "*://tracking.miui.com/*",
        "*://ngfts.lge.com/*",
        "*://smartclip.net/*",
        "*://vortex.data.microsoft.com/*",
        "*://device-metrics-us.amazon.com/*", "*://device-metrics-us-2.amazon.com/*", "*://mads-eu.amazon.com/*",
        "*://ads.roku.com/*",
        "*://app-measurement.com/*", "*://firebase-settings.crashlytics.com/*",
        "*://sdk.privacy-center.org/*",
        "*://app.usercentrics.eu/*",
        "*://shareasale-analytics.com/*",
        "*://impact.com/*", "*://d.impactradius-event.com/*", "*://api.impact.com/*",
        "*://www.awin1.com/*", "*://zenaps.com/*",
        "*://partnerstack.com/*", "*://api.partnerstack.com/*",
        "*://api.refersion.com/*",
        "*://cdn.dynamicyield.com/*",
        "*://track.hubspot.com/*",
        "*://trackcmp.net/*",
        "*://js.driftt.com/*",
        "*://imasdk.googleapis.com/*", "*://dai.google.com/*",
        "*://ssl.p.jwpcdn.com/*",
        "*://mssl.fwmrm.net/*",
        "*://tremorhub.com/*", "*://ads.tremorhub.com/*",
        "*://fpjs.io/*", "*://api.fpjs.io/*",
        "*://onetag-sys.com/*",
        "*://id5-sync.com/*",
        "*://thetradedesk.com/*",
        "*://prod.uidapi.com/*",
        "*://bnc.lt/*",
        "*://wzrkt.com/*",
        "*://clevertap-prod.com/*",
        "*://crypto-loot.org/*",
        "*://popads.net/*", "*://popcash.net/*", "*://onclickads.net/*", "*://popmyads.com/*", "*://trafficjunky.net/*", "*://juicyads.com/*",
        "*://greatis.com/*",
        "*://init.supersonicads.com/*",
        "*://api.fyber.com/*",
        "*://ironSource.mobi/*",
        "*://outcome-ssp.supersonicads.com/*",
        "*://cdn.cookielaw.org/*",
        "*://analytics.adobe.io/*",

        // ── Affiliate Networks ──────────────────────────────────────────
        "*://impactradius.com/*",      "*://*.impactradius.com/*",
        "*://impact.com/*",            "*://*.impact.com/*",
        "*://shareasale.com/*",        "*://*.shareasale.com/*",
        "*://cj.com/*",                "*://*.cj.com/*",
        "*://commission-junction.com/*","*://*.commission-junction.com/*",
        "*://dpbolvw.net/*",           "*://*.dpbolvw.net/*",
        "*://jdoqocy.com/*",           "*://*.jdoqocy.com/*",
        "*://kqzyfj.com/*",            "*://*.kqzyfj.com/*",
        "*://qksrv.net/*",             "*://*.qksrv.net/*",
        "*://tkqlhce.com/*",           "*://*.tkqlhce.com/*",
        "*://anrdoezrs.net/*",         "*://*.anrdoezrs.net/*",
        "*://awin.com/*",              "*://*.awin.com/*",
        "*://awin1.com/*",             "*://*.awin1.com/*",
        "*://zanox.com/*",             "*://*.zanox.com/*",
        "*://zanox-affiliate.de/*",    "*://*.zanox-affiliate.de/*",
        "*://tradedoubler.com/*",      "*://*.tradedoubler.com/*",
        "*://viglink.com/*",           "*://*.viglink.com/*",
        "*://skimlinks.com/*",         "*://*.skimlinks.com/*",
        "*://skimresources.com/*",     "*://*.skimresources.com/*",
        "*://go.skimresources.com/*",
        "*://pepperjam.com/*",         "*://*.pepperjam.com/*",
        "*://pjtra.com/*",             "*://pjatr.com/*",
        "*://avantlink.com/*",         "*://*.avantlink.com/*",
        "*://maxbounty.com/*",         "*://*.maxbounty.com/*",
        "*://partnerize.com/*",        "*://*.partnerize.com/*",
        "*://conversant.com/*",        "*://*.conversant.com/*",
        "*://conversantmedia.com/*",   "*://*.conversantmedia.com/*",
        "*://flexoffers.com/*",        "*://*.flexoffers.com/*",
        "*://webgains.com/*",          "*://*.webgains.com/*",
        "*://commissionfactory.com/*", "*://*.commissionfactory.com/*",
        "*://tune.com/*",              "*://*.tune.com/*",
        "*://hasoffers.com/*",         "*://*.hasoffers.com/*",
        "*://everflow.io/*",           "*://*.everflow.io/*",
        "*://affise.com/*",            "*://*.affise.com/*",
        "*://linkconnector.com/*",     "*://*.linkconnector.com/*",
        "*://cake.com/*",              "*://*.cake.com/*",
        "*://rakuten.com/*",           "*://*.rakuten.com/*",
        "*://linksynergy.com/*",       "*://*.linksynergy.com/*",
        "*://phpadsnew.com/*",         "*://*.phpadsnew.com/*",
        "*://affiliatefuture.com/*",   "*://*.affiliatefuture.com/*",
        "*://performancehorizon.com/*","*://*.performancehorizon.com/*",
        "*://offervault.com/*",        "*://*.offervault.com/*",
        "*://clickbooth.com/*",        "*://*.clickbooth.com/*",
        "*://clickbank.com/*",         "*://*.clickbank.com/*",
        "*://clkmon.com/*",            "*://*.clkmon.com/*",
        "*://clkrev.com/*",            "*://*.clkrev.com/*",
        "*://go2cloud.org/*",          "*://*.go2cloud.org/*",
        "*://trk.vindicosuite.com/*",
        "*://affiliatewindow.com/*",   "*://*.affiliatewindow.com/*",
        "*://2mdn.net/*",              "*://*.2mdn.net/*",
        "*://clicky.com/*",            "*://*.clicky.com/*",
        "*://refer.viglink.com/*",     "*://api.viglink.com/*",

        // ── Video Ads ───────────────────────────────────────────────────
        "*://jwplayer.com/*",          "*://*.jwplayer.com/*",
        "*://brightcove.com/*",        "*://*.brightcove.com/*",
        "*://springserve.com/*",       "*://*.springserve.com/*",
        "*://videoamp.com/*",          "*://*.videoamp.com/*",
        "*://unrulymedia.com/*",       "*://*.unrulymedia.com/*",
        "*://tremormedia.com/*",       "*://*.tremormedia.com/*",
        "*://tremorvideo.com/*",       "*://*.tremorvideo.com/*",
        "*://innovid.com/*",           "*://*.innovid.com/*",
        "*://vindico.com/*",           "*://*.vindico.com/*",
        "*://yume.com/*",              "*://*.yume.com/*",
        "*://extreme-reach.com/*",     "*://*.extreme-reach.com/*",
        "*://vidazoo.com/*",           "*://*.vidazoo.com/*",
        "*://connatix.com/*",          "*://*.connatix.com/*",
        "*://loopme.com/*",            "*://*.loopme.com/*",
        "*://gumgum.com/*",            "*://*.gumgum.com/*",
        "*://primis.tech/*",           "*://*.primis.tech/*",
        "*://adtelligent.com/*",       "*://*.adtelligent.com/*",
        "*://magnite.com/*",           "*://*.magnite.com/*",
        "*://springads.com/*",         "*://*.springads.com/*",
        "*://scanscout.com/*",         "*://*.scanscout.com/*",
        "*://adap.tv/*",               "*://*.adap.tv/*",
        "*://liverail.com/*",          "*://*.liverail.com/*",
        "*://videohub.tv/*",           "*://*.videohub.tv/*",
        "*://streamrail.com/*",        "*://*.streamrail.com/*",
        "*://aniview.com/*",           "*://*.aniview.com/*",

        // ── Consent Management Platforms ────────────────────────────────
        "*://onetrust.com/*",          "*://*.onetrust.com/*",
        "*://cookielaw.org/*",         "*://*.cookielaw.org/*",
        "*://cdn.cookielaw.org/*",
        "*://cookiebot.com/*",         "*://*.cookiebot.com/*",
        "*://consent.cookiebot.com/*",
        "*://trustarc.com/*",          "*://*.trustarc.com/*",
        "*://consent.trustarc.com/*",
        "*://truste.com/*",            "*://*.truste.com/*",
        "*://consensu.org/*",          "*://*.consensu.org/*",
        "*://quantcast.mgr.consensu.org/*",
        "*://consentmanager.net/*",    "*://*.consentmanager.net/*",
        "*://didomi.io/*",             "*://*.didomi.io/*",
        "*://sdk.privacy-center.org/*",
        "*://usercentrics.com/*",      "*://*.usercentrics.com/*",
        "*://iubenda.com/*",           "*://*.iubenda.com/*",
        "*://cookiefirst.com/*",       "*://*.cookiefirst.com/*",
        "*://osano.com/*",             "*://*.osano.com/*",
        "*://sourcepoint.com/*",       "*://*.sourcepoint.com/*",
        "*://evidon.com/*",            "*://*.evidon.com/*",
        "*://crownpeak.com/*",         "*://*.crownpeak.com/*",
        "*://cookie-script.com/*",     "*://*.cookie-script.com/*",
        "*://cookiehub.com/*",         "*://*.cookiehub.com/*",
        "*://termly.io/*",             "*://*.termly.io/*",
        "*://cookieyes.com/*",         "*://*.cookieyes.com/*",
        "*://complianz.io/*",          "*://*.complianz.io/*",
        "*://secureprivacy.ai/*",      "*://*.secureprivacy.ai/*",

        // ── Tracking & Fingerprinting ────────────────────────────────────
        "*://fingerprint.com/*",       "*://*.fingerprint.com/*",
        "*://fingerprintjs.com/*",     "*://*.fingerprintjs.com/*",
        "*://fpjscdn.net/*",           "*://*.fpjscdn.net/*",
        "*://cdn.fingerprint.com/*",
        "*://maxmind.com/*",           "*://*.maxmind.com/*",
        "*://threatmetrix.com/*",      "*://*.threatmetrix.com/*",
        "*://iovation.com/*",          "*://*.iovation.com/*",
        "*://sessioncam.com/*",        "*://*.sessioncam.com/*",
        "*://clicktale.com/*",         "*://*.clicktale.com/*",
        "*://contentsquare.com/*",     "*://*.contentsquare.com/*",
        "*://dynatrace.com/*",         "*://*.dynatrace.com/*",
        "*://sift.com/*",              "*://*.sift.com/*",
        "*://siftscience.com/*",       "*://*.siftscience.com/*",
        "*://perimeterx.com/*",        "*://*.perimeterx.com/*",
        "*://px-cdn.net/*",            "*://*.px-cdn.net/*",
        "*://px-cloud.net/*",          "*://*.px-cloud.net/*",
        "*://imperva.com/*",           "*://*.imperva.com/*",
        "*://distilnetworks.com/*",    "*://*.distilnetworks.com/*",
        "*://human.security/*",        "*://*.human.security/*",
        "*://tiqcdn.com/*",            "*://*.tiqcdn.com/*",
        "*://tags.tiqcdn.com/*",
        "*://tns-counter.ru/*",        "*://*.tns-counter.ru/*",
        "*://ipqualityscore.com/*",    "*://*.ipqualityscore.com/*",
        "*://deviceatlas.com/*",       "*://*.deviceatlas.com/*",
        "*://51degrees.com/*",         "*://*.51degrees.com/*",
        "*://forensiq.com/*",          "*://*.forensiq.com/*",
        "*://fraudlogix.com/*",        "*://*.fraudlogix.com/*",
        "*://tmx.com/*",               "*://*.tmx.com/*",
        "*://visitoridentification.net/*","*://*.visitoridentification.net/*",
        "*://augur.io/*",              "*://*.augur.io/*",
        "*://botd.fpjscdn.net/*",
        "*://liveramp.com/*",          "*://*.liveramp.com/*",
        "*://rlcdn.com/*",             "*://*.rlcdn.com/*",
        "*://agkn.com/*",              "*://*.agkn.com/*",
        "*://rapleaf.com/*",           "*://*.rapleaf.com/*",
        "*://neustar.biz/*",           "*://*.neustar.biz/*",
        "*://tapad.com/*",             "*://*.tapad.com/*",
        "*://drawbridge.com/*",        "*://*.drawbridge.com/*",
        "*://cross-pixel.com/*",       "*://*.cross-pixel.com/*",
        "*://totient.co/*",            "*://*.totient.co/*",

        // ── Cryptominers & Malware ──────────────────────────────────────
        "*://coinhive.com/*",          "*://*.coinhive.com/*",
        "*://coin-hive.com/*",         "*://*.coin-hive.com/*",
        "*://cryptoloot.pro/*",        "*://*.cryptoloot.pro/*",
        "*://minero.cc/*",             "*://*.minero.cc/*",
        "*://jsecoin.com/*",           "*://*.jsecoin.com/*",
        "*://monerominer.rocks/*",     "*://*.monerominer.rocks/*",
        "*://webmr.eu/*",              "*://*.webmr.eu/*",
        "*://coinimp.com/*",           "*://*.coinimp.com/*",
        "*://papoto.com/*",            "*://*.papoto.com/*",
        "*://cryptonight.pro/*",       "*://*.cryptonight.pro/*",
        "*://afminer.com/*",           "*://*.afminer.com/*",
        "*://coinerra.com/*",          "*://*.coinerra.com/*",
        "*://minerpool.net/*",         "*://*.minerpool.net/*",
        "*://nbminer.com/*",           "*://*.nbminer.com/*",
        "*://crypto-loot.com/*",       "*://*.crypto-loot.com/*",
        "*://deepminer.com/*",         "*://*.deepminer.com/*",
        "*://monero-miner.com/*",      "*://*.monero-miner.com/*",
        "*://xmrpool.net/*",           "*://*.xmrpool.net/*",
        "*://reasedoper.pw/*",         "*://*.reasedoper.pw/*",

        // ── Email Tracking ──────────────────────────────────────────────
        "*://opens.mailchimp.com/*",   "*://list-manage.com/*",
        "*://tracking.sendgrid.net/*", "*://*.sendgrid.net/*",
        "*://mailgun.org/*",           "*://*.mailgun.org/*",
        "*://sparkpostmail.com/*",     "*://*.sparkpostmail.com/*",
        "*://sailthru.com/*",          "*://*.sailthru.com/*",
        "*://litmus.com/*",            "*://*.litmus.com/*",
        "*://vero.co/*",               "*://*.vero.co/*",
        "*://customer.io/*",           "*://*.customer.io/*",
        "*://klaviyo.com/*",           "*://*.klaviyo.com/*",
        "*://drip.com/*",              "*://*.drip.com/*",
        "*://convertkit.com/*",        "*://*.convertkit.com/*",
        "*://activecampaign.com/*",    "*://*.activecampaign.com/*",
        "*://constantcontact.com/*",   "*://*.constantcontact.com/*",
        "*://mailjet.com/*",           "*://*.mailjet.com/*",
        "*://emailtracking.io/*",      "*://*.emailtracking.io/*",
        "*://trk.email/*",             "*://*.trk.email/*",
        "*://stripo.email/*",          "*://*.stripo.email/*",

        // ── A/B Testing ─────────────────────────────────────────────────
        "*://abtasty.com/*",           "*://*.abtasty.com/*",
        "*://vwo.com/*",               "*://*.vwo.com/*",
        "*://convert.com/*",           "*://*.convert.com/*",
        "*://kameleoon.com/*",         "*://*.kameleoon.com/*",
        "*://unbounce.com/*",          "*://*.unbounce.com/*",
        "*://qubit.com/*",             "*://*.qubit.com/*",
        "*://conductrics.com/*",       "*://*.conductrics.com/*",
        "*://monetate.net/*",          "*://*.monetate.net/*",
        "*://richrelevance.com/*",     "*://*.richrelevance.com/*",

        // ── More Social Trackers ────────────────────────────────────────
        "*://platform.twitter.com/widgets/*",
        "*://snap.com/*",              "*://*.sc-static.net/*",
        "*://tr.snapchat.com/*",
        "*://static.xx.fbcdn.net/rsrc.php/v3/ads/*",
        "*://graph.facebook.com/*/activities/*",
        "*://twitter.com/i/adsct/*",
        "*://t.co/i/adsct/*",
        "*://linkedin.com/li/track/*",
        "*://sherpany.com/*",          "*://*.sherpany.com/*",
        "*://social-analytics.io/*",   "*://*.social-analytics.io/*",
        "*://socialsignin.com/*",      "*://*.socialsignin.com/*",

        // ── More Ad Networks ────────────────────────────────────────────
        "*://indexww.com/*",           "*://*.indexww.com/*",
        "*://lkqd.net/*",              "*://*.lkqd.net/*",
        "*://districtm.io/*",          "*://*.districtm.io/*",
        "*://districtm.ca/*",          "*://*.districtm.ca/*",
        "*://epom.com/*",              "*://*.epom.com/*",
        "*://admob.com/*",             "*://*.admob.com/*",
        "*://inmobi.com/*",            "*://*.inmobi.com/*",
        "*://mopub.com/*",             "*://*.mopub.com/*",
        "*://applovin.com/*",          "*://*.applovin.com/*",
        "*://ironsource.com/*",        "*://*.ironsource.com/*",
        "*://vungle.com/*",            "*://*.vungle.com/*",
        "*://chartboost.com/*",        "*://*.chartboost.com/*",
        "*://startapp.com/*",          "*://*.startapp.com/*",
        "*://ogury.com/*",             "*://*.ogury.com/*",
        "*://propellerads.com/*",      "*://*.propellerads.com/*",
        "*://trafficjunky.net/*",      "*://*.trafficjunky.net/*",
        "*://exoclick.com/*",          "*://*.exoclick.com/*",
        "*://juicyads.com/*",          "*://*.juicyads.com/*",
        "*://hilltopads.net/*",        "*://*.hilltopads.net/*",
        "*://popads.net/*",            "*://*.popads.net/*",
        "*://popcash.net/*",           "*://*.popcash.net/*",
        "*://admaven.com/*",           "*://*.admaven.com/*",
        "*://yieldlove.com/*",         "*://*.yieldlove.com/*",
        "*://adnium.com/*",            "*://*.adnium.com/*",
        "*://trafficstars.com/*",      "*://*.trafficstars.com/*",
        "*://seedtag.com/*",           "*://*.seedtag.com/*",
        "*://zemanta.com/*",           "*://*.zemanta.com/*",
        "*://adform.net/*",            "*://*.adform.net/*",
        "*://adform.com/*",            "*://*.adform.com/*",
        "*://nextperf.com/*",          "*://*.nextperf.com/*",
        "*://smartclip.net/*",         "*://*.smartclip.net/*",
        "*://appier.com/*",            "*://*.appier.com/*",
        "*://adjust.com/*",            "*://*.adjust.com/*",
        "*://appsflyer.com/*",         "*://*.appsflyer.com/*",
        "*://kochava.com/*",           "*://*.kochava.com/*",
        "*://branch.io/*",             "*://*.branch.io/*",
        "*://singular.net/*",          "*://*.singular.net/*",
        "*://tenjin.io/*",             "*://*.tenjin.io/*",
        "*://attributionapp.com/*",    "*://*.attributionapp.com/*",
        "*://moat.com/*",              "*://*.moat.com/*",
        "*://adalyser.com/*",          "*://*.adalyser.com/*",
        "*://integral-assets.com/*",   "*://*.integral-assets.com/*",
        "*://gwiq.com/*",              "*://*.gwiq.com/*",
        "*://adsymptotic.com/*",       "*://*.adsymptotic.com/*",
        "*://nrich.ai/*",              "*://*.nrich.ai/*",

        // ── More OEM Vendors ────────────────────────────────────────────
        // Vivo
        "*://analytics.vivo.com.cn/*", "*://sa.vivo.com.cn/*",
        "*://tracking.vivo.com/*",     "*://log.vivo.com.cn/*",
        "*://push.vivo.com.cn/*",      "*://adv.vivo.com.cn/*",
        "*://cm.vivo.com.cn/*",        "*://analytics-sg.vivo.com/*",
        // LG
        "*://lganalytics.com/*",       "*://*.lganalytics.com/*",
        "*://lge.com/analytics*",      "*://tracking.lge.com/*",
        "*://ads.lge.com/*",           "*://stats.lge.com/*",
        "*://lgtvsdp.com/*",           "*://*.lgtvsdp.com/*",
        "*://lgsmartad.com/*",         "*://*.lgsmartad.com/*",
        "*://smartshare.lgappstv.com/*","*://ibis.lgappstv.com/*",
        // Motorola
        "*://analytics.motorola.com/*","*://tracking.motorola.com/*",
        "*://moto-analytics.com/*",    "*://*.moto-analytics.com/*",
        "*://motorola-analytics.com/*","*://*.motog.motorola.com/ads/*",
        // Sony
        "*://sony-analytics.com/*",    "*://*.sony-analytics.com/*",
        "*://analyticsservices.sony.com/*",
        "*://ad.sonyentertainmentnetwork.com/*",
        "*://ps-metrics.sonyentertainmentnetwork.com/*",
        "*://tele.sonyentertainmentnetwork.com/*",
        // Lenovo
        "*://analytics.lenovo.com/*",  "*://track.lenovo.com/*",
        "*://collector.lenovo.com/*",  "*://adv.lenovo.com/*",
        "*://ads.lenovo.com/*",
        // ASUS
        "*://analytics.asus.com/*",    "*://tracking.asus.com/*",
        "*://metrics.asus.com/*",      "*://ads.asus.com/*",
        "*://splashads.asus.com/*",    "*://asus-splashads.com/*",
        // Nokia / HMD Global
        "*://analytics.hmdglobal.com/*","*://track.hmdglobal.com/*",
        "*://nokia-analytics.com/*",   "*://*.nokia-analytics.com/*",
        // HTC
        "*://analytics.htc.com/*",     "*://tracking.htc.com/*",
        "*://ads.htc.com/*",           "*://htcmetrics.com/*",
        // Wiko
        "*://analytics.wikozone.com/*","*://tracking.wiko.com/*",
        // TCL
        "*://analytics.tcl.com/*",     "*://track.tcl.com/*",
        "*://ad.tcl.com/*",

        // ── More Social Trackers (subdomains & variants) ─────────────────
        // Facebook/Instagram
        "*://connect.facebook.net/*",
        "*://web.facebook.com/tr*",
        "*://graph.instagram.com/*",
        "*://i.instagram.com/*",
        "*://pixel.facebook.com/*",
        // Twitter/X
        "*://analytics.twitter.com/*",
        "*://t.co/i/*",
        "*://platform.twitter.com/*",
        "*://cdn.syndication.twimg.com/*",
        "*://syndication.twitter.com/*",
        // LinkedIn
        "*://snap.licdn.com/*",
        "*://px.ads.linkedin.com/*",
        "*://dc.ads.linkedin.com/*",
        "*://platform.linkedin.com/*",
        // Snapchat
        "*://tr.snapchat.com/*",
        "*://sc-static.net/*",         "*://*.sc-static.net/*",
        "*://snapads.com/*",           "*://*.snapads.com/*",
        "*://businesshelp.snapchat.com/ads*",
        // Pinterest more
        "*://analytics.pinterest.com/*",
        "*://widgets.pinterest.com/analytics*",
        // YouTube
        "*://s.youtube.com/api/stats/ads*",
        "*://www.youtube.com/pagead*",
        // Twitch
        "*://spade.twitch.tv/*",       "*://ads.twitch.tv/*",
        "*://static.ads.twitch.tv/*",
        // Discord (tracking)
        "*://discordapp.com/api/science*",
        "*://discord.com/api/science*",

        // ── More Cryptominers & Malware ──────────────────────────────────
        "*://minergate.com/*",         "*://*.minergate.com/*",
        "*://nicehash.com/*",          "*://*.nicehash.com/*",
        "*://2giga.link/*",            "*://*.2giga.link/*",
        "*://hashfor.cash/*",          "*://*.hashfor.cash/*",
        "*://coin-have.com/*",         "*://*.coin-have.com/*",
        "*://cryptobara.com/*",        "*://*.cryptobara.com/*",
        "*://xmrpool.eu/*",            "*://*.xmrpool.eu/*",
        "*://supportxmr.com/*",        "*://*.supportxmr.com/*",
        "*://monerocean.stream/*",     "*://*.monerocean.stream/*",
        "*://hashvault.pro/*",         "*://*.hashvault.pro/*",
        "*://xmrig.com/*",             "*://*.xmrig.com/*",
        "*://3aliansso.com/*",         "*://*.3aliansso.com/*",
        "*://coinblind.com/*",         "*://*.coinblind.com/*",
        "*://gridcash.net/*",          "*://*.gridcash.net/*",
        "*://miner.rocks/*",           "*://*.miner.rocks/*",
        "*://lmodr.biz/*",             "*://*.lmodr.biz/*",
        "*://listat.biz/*",            "*://*.listat.biz/*",
        "*://scriptzol.xyz/*",         "*://*.scriptzol.xyz/*",
        "*://cfts.pw/*",               "*://*.cfts.pw/*",

        // ── More Video Ads ───────────────────────────────────────────────
        "*://ooyala.com/*",            "*://*.ooyala.com/*",
        "*://brightroll.com/*",        "*://*.brightroll.com/*",
        "*://beachfront.com/*",        "*://*.beachfront.com/*",
        "*://verve.com/*",             "*://*.verve.com/*",
        "*://rhythmone.com/*",         "*://*.rhythmone.com/*",
        "*://360yield.com/*",          "*://*.360yield.com/*",
        "*://undertone.com/*",         "*://*.undertone.com/*",
        "*://yieldmo.com/*",           "*://*.yieldmo.com/*",
        "*://xumo.tv/*",               "*://*.xumo.tv/*",
        "*://appads.com/*",            "*://*.appads.com/*",
        "*://videologygroup.com/*",    "*://*.videologygroup.com/*",
        "*://playwire.com/*",          "*://*.playwire.com/*",
        "*://synacor.com/*",           "*://*.synacor.com/*",
        "*://freewheel.tv/*",          "*://*.freewheel.tv/*",
        "*://stickyadstv.com/*",       "*://*.stickyadstv.com/*",

        // ── More Tracking & Fingerprinting ───────────────────────────────
        "*://forter.com/*",            "*://*.forter.com/*",
        "*://riskiq.com/*",            "*://*.riskiq.com/*",
        "*://inauth.com/*",            "*://*.inauth.com/*",
        "*://accertify.com/*",         "*://*.accertify.com/*",
        "*://kount.com/*",             "*://*.kount.com/*",
        "*://signifyd.com/*",          "*://*.signifyd.com/*",
        "*://bounceexchange.com/*",    "*://*.bounceexchange.com/*",
        "*://wunderkind.co/*",         "*://*.wunderkind.co/*",
        "*://semasio.net/*",           "*://*.semasio.net/*",
        "*://eyeota.com/*",            "*://*.eyeota.com/*",
        "*://weborama.com/*",          "*://*.weborama.com/*",
        "*://pippio.com/*",            "*://*.pippio.com/*",
        "*://nexac.com/*",             "*://*.nexac.com/*",
        "*://netmng.com/*",            "*://*.netmng.com/*",
        "*://audienceinsights.net/*",  "*://*.audienceinsights.net/*",
        "*://creativecdn.com/*",       "*://*.creativecdn.com/*",
        "*://4dex.io/*",               "*://*.4dex.io/*",
        "*://bfmio.com/*",             "*://*.bfmio.com/*",
        "*://zergnet.com/*",           "*://*.zergnet.com/*",
        "*://tremorhub.com/*",         "*://*.tremorhub.com/*",
        "*://openx.com/*",             "*://*.openx.com/*",
        "*://permutive.com/*",         "*://*.permutive.com/*",

        // ── More Consent Management ──────────────────────────────────────
        "*://privacymanager.io/*",     "*://*.privacymanager.io/*",
        "*://traffective.com/*",       "*://*.traffective.com/*",
        "*://cookieinformation.com/*", "*://*.cookieinformation.com/*",
        "*://borlabs-cookie.de/*",     "*://*.borlabs-cookie.de/*",
        "*://consentframework.com/*",  "*://*.consentframework.com/*",
        "*://uniconsent.com/*",        "*://*.uniconsent.com/*",
        "*://cdn.privacy-mgmt.com/*",

        // ── More Affiliate Networks ──────────────────────────────────────
        "*://go2speed.org/*",          "*://*.go2speed.org/*",
        "*://financeads.net/*",        "*://*.financeads.net/*",
        "*://affilinet.com/*",         "*://*.affilinet.com/*",
        "*://belboon.com/*",           "*://*.belboon.com/*",
        "*://adcell.de/*",             "*://*.adcell.de/*",
        "*://tradetracker.com/*",      "*://*.tradetracker.com/*",
        "*://admitad.com/*",           "*://*.admitad.com/*",
        "*://cityads.com/*",           "*://*.cityads.com/*",
        "*://leadbit.com/*",           "*://*.leadbit.com/*",
        "*://marketgid.com/*",         "*://*.marketgid.com/*",
        "*://cpalead.com/*",           "*://*.cpalead.com/*",
        "*://cpaway.com/*",            "*://*.cpaway.com/*",
        "*://offerwall.io/*",          "*://*.offerwall.io/*",
        "*://avangate.com/*",          "*://*.avangate.com/*",
        "*://2checkout.com/*",         "*://*.2checkout.com/*",

        // ── More Email Tracking ──────────────────────────────────────────
        "*://postmarkapp.com/*",       "*://*.postmarkapp.com/*",
        "*://mandrillapp.com/*",       "*://*.mandrillapp.com/*",
        "*://campaignmonitor.com/*",   "*://*.campaignmonitor.com/*",
        "*://createsend.com/*",        "*://*.createsend.com/*",
        "*://aweber.com/*",            "*://*.aweber.com/*",
        "*://infusionsoft.com/*",      "*://*.infusionsoft.com/*",
        "*://keap.com/*",              "*://*.keap.com/*",
        "*://mailer-analytics.net/*",  "*://*.mailer-analytics.net/*",
        "*://emailtracker.website/*",  "*://*.emailtracker.website/*",
        "*://whoreadme.com/*",         "*://*.whoreadme.com/*",
        "*://bananatag.com/*",         "*://*.bananatag.com/*",
        "*://getnotify.com/*",         "*://*.getnotify.com/*",
        "*://yesware.com/*",           "*://*.yesware.com/*",

        // ── More A/B Testing ─────────────────────────────────────────────
        "*://launchdarkly.com/*",      "*://*.launchdarkly.com/*",
        "*://split.io/*",              "*://*.split.io/*",
        "*://statsig.com/*",           "*://*.statsig.com/*",
        "*://growthbook.io/*",         "*://*.growthbook.io/*",
        "*://flagship.io/*",           "*://*.flagship.io/*",
        "*://apptimize.com/*",         "*://*.apptimize.com/*",
        
        // ── AGGRESSIVE CATCH-ALL PATTERNS FOR REMAINING ACCESSIBLE DOMAINS ──
        // These patterns specifically target domains from test results that were accessible
        "*://aan.amazon.com/*",
        "*://static.criteo.net/*",
        "*://cdn.mgid.com/*",          "*://servicer.mgid.com/*",
        "*://bingads.microsoft.com/*",
        "*://liftoff.io/*",
        "*://cdn.indexexchange.com/*",
        "*://smartyads.com/*",         "*://ad.gt/*",
        "*://tlx.3lift.com/*",         "*://apex.go.sonobi.com/*",
        "*://sync.kargo.com/*",
        "*://pangleglobal.com/*",
        "*://redirector.googlevideo.com/*",
        "*://youtubei.googleapis.com/*",
        "*://analytics.adobe.io/*",
        "*://fpjs.io/*",               "*://api.fpjs.io/*",
        "*://onetag-sys.com/*",
        "*://id5-sync.com/*",
        "*://prod.uidapi.com/*",
        "*://bnc.lt/*",
        "*://graph.facebook.com/*",    "*://tr.facebook.com/*",
        "*://sc-analytics.appspot.com/*",
        "*://d.reddit.com/*",
        "*://pixel.quora.com/*",
        "*://px.srvcs.tumblr.com/*",
        "*://ads.vk.com/*",            "*://vk.com/rtrg*",
        "*://ad.mail.ru/*",            "*://top-fwz1.mail.ru/*",
        "*://xp.apple.com/*",
        "*://ads.huawei.com/*",
        "*://data.mistat.india.xiaomi.com/*",
        "*://data.mistat.rus.xiaomi.com/*",
        "*://tracking.miui.com/*",
        "*://ngfts.lge.com/*",
        "*://smartclip.net/*",
        "*://vortex.data.microsoft.com/*",
        "*://device-metrics-us.amazon.com/*",
        "*://device-metrics-us-2.amazon.com/*",
        "*://mads-eu.amazon.com/*",
        "*://ads.roku.com/*",
        "*://app-measurement.com/*",
        "*://firebase-settings.crashlytics.com/*",
        "*://sdk.privacy-center.org/*",
        "*://app.usercentrics.eu/*",
        "*://shareasale-analytics.com/*",
        "*://d.impactradius-event.com/*",
        "*://api.impact.com/*",
        "*://www.awin1.com/*",
        "*://zenaps.com/*",
        "*://partnerstack.com/*",      "*://api.partnerstack.com/*",
        "*://api.refersion.com/*",
        "*://cdn.dynamicyield.com/*",
        "*://track.hubspot.com/*",
        "*://trackcmp.net/*",
        "*://js.driftt.com/*",
        "*://imasdk.googleapis.com/*",
        "*://dai.google.com/*",
        "*://ssl.p.jwpcdn.com/*",
        "*://mssl.fwmrm.net/*",
        "*://ads.tremorhub.com/*",
        "*://init.supersonicads.com/*",
        "*://api.fyber.com/*",
        "*://ironSource.mobi/*",       "*://ironsource.mobi/*",
        "*://outcome-ssp.supersonicads.com/*",
        "*://crypto-loot.org/*",
        "*://popads.net/*",            "*://popcash.net/*",
        "*://onclickads.net/*",
        "*://popmyads.com/*",
        "*://trafficjunky.net/*",
        "*://juicyads.com/*",
        "*://greatis.com/*",

        // ── d3ward adblock test — ALL tested domains ────────────────────
        "*://adtago.s3.amazonaws.com/*",
        "*://analyticsengine.s3.amazonaws.com/*",
        "*://analytics.s3.amazonaws.com/*",
        "*://advice-ads.s3.amazonaws.com/*",
        "*://widget.privy.com/*",
        "*://c.amazon-adsystem.com/*",
        "*://s.amazon-adsystem.com/*",
        "*://an.facebook.com/*",
        "*://pixel.facebook.com/*",
        "*://staticxx.facebook.com/*",
        "*://www.facebook.com/tr*",
        "*://www.facebook.com/tr/*",
        "*://pixel.quantcount.com/*",
        "*://pixel.quantserve.com/*",
        "*://segment.quantserve.com/*",
        "*://rules.quantcount.com/*",
        "*://pixel.adsafeprotected.com/*",
        "*://static.adsafeprotected.com/*",
        "*://fw.adsafeprotected.com/*",
        "*://data.adsafeprotected.com/*",
        "*://dt.adsafeprotected.com/*",
        "*://cdn.doubleverify.com/*",
        "*://rtb.doubleverify.com/*",
        "*://pixel.doubleverify.com/*",
        "*://tps.doubleverify.com/*",
        "*://cdn3.doubleverify.com/*",
        "*://cdn.krxd.net/*",
        "*://beacon.krxd.net/*",
        "*://consumer.krxd.net/*",
        "*://usermatch.krxd.net/*",
        "*://apiservices.krxd.net/*",
        "*://pixel.everesttech.net/*",
        "*://dsum-sec.casalemedia.com/*",
        "*://ssum-sec.casalemedia.com/*",
        "*://ssum.casalemedia.com/*",
        "*://pixel.rubiconproject.com/*",
        "*://fastlane.rubiconproject.com/*",
        "*://optimized-by.rubiconproject.com/*",
        "*://prebid-server.rubiconproject.com/*",
        "*://token.rubiconproject.com/*",
        "*://geo.moatads.com/*",
        "*://px.moatads.com/*",
        "*://js.moatads.com/*",
        "*://mb.moatads.com/*",
        "*://pixel.moatads.com/*",
        "*://s.pubmine.com/*",
        "*://ad.turn.com/*",
        "*://d.turn.com/*",
        "*://r.turn.com/*",
        "*://rpm.turn.com/*",
        "*://cm.g.doubleclick.net/*",
        "*://simage2.pubmatic.com/*",
        "*://image2.pubmatic.com/*",
        "*://image4.pubmatic.com/*",
        "*://image6.pubmatic.com/*",
        "*://hbopenbid.pubmatic.com/*",
        "*://ads.pubmatic.com/*",
        "*://t.pubmatic.com/*",
        "*://ow.pubmatic.com/*",
        "*://us-u.openx.net/*",
        "*://uk-ads.openx.net/*",
        "*://rtb.openx.net/*",
        "*://u.openx.net/*",
        "*://usermatch.openx.com/*",
        "*://delivery.adnuntius.com/*",
        "*://data.adnuntius.com/*",
        "*://adnuntius.com/*",          "*://*.adnuntius.com/*",
        "*://ads.bridgewell.com/*",     "*://*.bridgewell.com/*",
        "*://tg1.clevertap-prod.com/*",
        "*://wzrkt.com/*",              "*://*.wzrkt.com/*",
        "*://cdn.concert.io/*",         "*://concert.io/*",
        "*://bam-cell.nr-data.net/*",
        "*://securepubads.g.doubleclick.net/*",
        "*://eus.rubiconproject.com/*",
        "*://idsync.rlcdn.com/*",
        "*://p.adsymptotic.com/*",
        "*://static.cloudflareinsights.com/*",
        "*://cdn.speedcurve.com/*",
        "*://cdn.segment.com/*",
        "*://api.segment.io/*",
        "*://cdn.heapanalytics.com/*",
        "*://heapanalytics.com/*",
        "*://cdn-3.convertexperiments.com/*",
        "*://cdn.mxpnl.com/*",
        "*://api-js.mixpanel.com/*",
        "*://decide.mixpanel.com/*",
        "*://bat.bing.com/*",
        "*://c.bing.com/*",
        "*://bat.r.msn.com/*",
        "*://a.clarity.ms/*",
        "*://c.clarity.ms/*",
        "*://d.clarity.ms/*",
        "*://js.monitor.azure.com/*",
        "*://cdn.cookielaw.org/*",
        "*://geolocation.onetrust.com/*",
        "*://privacyportal.onetrust.com/*",
        "*://optanon.blob.core.windows.net/*",
        "*://consent.cookiebot.com/*",
        "*://consentcdn.cookiebot.com/*",
        "*://cdn.privacy-mgmt.com/*",
        "*://wrapper.sp-prod.net/*",
        "*://sourcepoint.mgr.consensu.org/*",
        "*://quantcast.mgr.consensu.org/*",
        "*://cmpv2.mgr.consensu.org/*",
        "*://vendorlist.consensu.org/*",

        // ── More d3ward domains (additional categories) ──────────────────
        "*://s.pinimg.com/*",
        "*://ct.pinterest.com/*",
        "*://widgets.pinterest.com/*",
        "*://log.pinterest.com/*",
        "*://trk.pinterest.com/*",
        "*://assets.pinterest.com/*",
        "*://api.amplitude.com/*",
        "*://cdn.amplitude.com/*",
        "*://api2.amplitude.com/*",
        "*://static.hotjar.com/*",
        "*://vars.hotjar.com/*",
        "*://vc.hotjar.io/*",
        "*://in.hotjar.com/*",
        "*://ws.hotjar.com/*",
        "*://t.clarity.ms/*",

        // ── Broader wildcard patterns ──────────────────────────────────
        "*://*.adnxs.com/*",
        "*://*.adsrvr.org/*",
        "*://*.bidswitch.net/*",
        "*://*.casalemedia.com/*",
        "*://*.demdex.net/*",
        "*://*.doubleclick.net/*",
        "*://*.everesttech.net/*",
        "*://*.krxd.net/*",
        "*://*.moatads.com/*",
        "*://*.openx.net/*",
        "*://*.pubmatic.com/*",
        "*://*.quantserve.com/*",
        "*://*.rubiconproject.com/*",
        "*://*.scorecardresearch.com/*",
        "*://*.serving-sys.com/*",
        "*://*.turn.com/*",
        "*://*.2mdn.net/*",
        "*://*.adsafeprotected.com/*",
        "*://*.doubleverify.com/*",
        "*://*.mathtag.com/*",
        "*://*.nr-data.net/*",
        "*://*.sentry-cdn.com/*",
        "*://*.sentry.io/*",
        "*://*.amplitude.com/*",
        "*://*.clarity.ms/*",
        "*://*.segment.io/*",
        "*://*.segment.com/*",
        "*://*.mixpanel.com/*",
        "*://*.heapanalytics.com/*",
    ];

    private string GetAdBlockerEarlyScript()
    {
        if (!_settings.AdBlockerEnabled) return "void 0;";
        var jsPath = IoPath.Combine(RendererPath, "adblocker_early.js");
        try
        {
            if (File.Exists(jsPath)) return File.ReadAllText(jsPath);
        }
        catch { }
        return "(function(){ /* adblocker script unavailable */ })();\n";
    }


    private Task<CoreWebView2Environment> CreateWebViewEnvironment(string dataFolder)
    {
        var options = new CoreWebView2EnvironmentOptions();
        return CoreWebView2Environment.CreateAsync(null, dataFolder, options);
    }

    private void SetupAdBlockerNetwork(WebView2 webView)
    {
        foreach (var pattern in _adBlockDomains)
            webView.CoreWebView2.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All);

        // Path-based patterns for banner images (including fetch context so fetchMediaSize is blocked)
        var adImagePaths = new[]
        {
            "*://*/ads/*", "*://*/ad/*", "*://*/adv/*",
            "*://*/banners/*", "*://*/banner/*",
            "*://*/advertisements/*", "*://*/advertisement/*",
            "*://*/adserver/*", "*://*/adimg/*", "*://*/adimages/*",
            "*://*/sponsor/*", "*://*/sponsors/*",
            "*://*ad-banner*", "*://*banner-ad*", "*://*adBanner*",
            // Ad script files — block common ad/tracker script paths
            "*://*/pagead.js*", "*://*/pagead2.js*",
            "*://*/ads.js*", "*://*/widget/ads.js*",
            "*://*/adsbygoogle.js*", "*://*/show_ads.js*",
            "*://*/googletag.js*", "*://*/gpt.js*",
            "*://*/analytics.js*", "*://*/ga.js*", "*://*/gtag.js*",
            "*://*/pixel.js*", "*://*/fbevents.js*",
            "*://*/hotjar*.js*", "*://*/mouseflow*.js*",
            "*://*/adsense*", "*://*/doubleclick*",
        };
        foreach (var p in adImagePaths)
            webView.CoreWebView2.AddWebResourceRequestedFilter(p, CoreWebView2WebResourceContext.All);

        webView.CoreWebView2.WebResourceRequested += (s, e) =>
        {
            if (!_settings.AdBlockerEnabled) return;
            var pageUrl = webView.Source?.ToString() ?? "";
            if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) &&
                _adBlockDisabledSites.Contains(pageUri.Host)) return;
            e.Response = _webViewEnvironment!.CreateWebResourceResponse(null, 403, "Blocked", "Access-Control-Allow-Origin: *");
        };
    }

    private static string GetAdBlockerScript() => @"
(function() {
  'use strict';
  if (window.__ycbAdBlockCosmetic) return;
  window.__ycbAdBlockCosmetic = true;

  // ═══════════════════════════════════════════════════════════════════════════
  // COMPREHENSIVE AD SELECTOR LIST
  // ═══════════════════════════════════════════════════════════════════════════
  var AD_SELECTORS = [
    // Google Ads
    'ins.adsbygoogle','ins[data-ad-client]','ins[data-ad-slot]',
    '[id^=""google_ads_iframe""]','[id^=""google_ads_frame""]','[id^=""aswift_""]',
    '.adsbygoogle','.google-ad','.google-ads','.GoogleActiveViewElement',
    '[data-google-query-id]','[data-ad-unit]','[data-ad-slot]','[data-ad-client]',
    'script[src*=""googlesyndication.com""]','script[src*=""doubleclick.net""]',
    'iframe[src*=""googlesyndication.com""]','iframe[src*=""doubleclick.net""]',
    'iframe[src*=""googleadservices.com""]','iframe[src*=""adnxs.com""]',
    'iframe[src*=""amazon-adsystem.com""]','iframe[src*=""taboola.com""]',
    'iframe[src*=""outbrain.com""]','iframe[src*=""criteo.com""]',
    'iframe[src*=""ads.yahoo.com""]','iframe[src*=""yieldmo.com""]',
    // Generic ad containers
    '#ad','#ads','#advert','#advertisement',
    '#ad-container','#ad-wrapper','#ad-banner','#ad-unit','#banner-ad',
    '[id^=""ad-""]','[id^=""ads-""]','[id$=""-ad""]','[id$=""-ads""]',
    '.ad-container','.ad-wrapper','.ad-banner','.ad-unit','.ad-slot','.ad-zone',
    '.advertisement','.advertisement-block','.advert','.adverts','.sponsor-label',
    '.ad-space','.adsbox','.textads','.banner-ads','.banner_ads','.afs_ads',
    // Taboola/Outbrain/Revcontent
    'div[id^=""taboola-""]','div[id^=""outbrain-""]','.taboola','.outbrain','.OUTBRAIN',
    '[class*=""taboola""]','[class*=""outbrain""]','[class*=""revcontent""]',
    // Native ad selectors
    '[class*=""sponsored""]','[class*=""Sponsored""]',
    '[data-testid*=""ad""]','[data-ad]','[data-ads]',
    // AMP ads
    'amp-ad','amp-embed','amp-sticky-ad',
    // Video ads
    '.video-ad','.video-ads','[class*=""preroll""]','[class*=""midroll""]',
    // Flash/plugin
    'object[type*=""shockwave""]','embed[type*=""shockwave""]',
    'object[type*=""flash""]','embed[type*=""flash""]',
    'object[data*=""banner""]','embed[src*=""banner""]',
    'object[data*=""/ads/""]','embed[src*=""/ads/""]',
    // Test-specific
    '#cts_test','#ad_ctd',
    // YouTube ads
    'ytd-promoted-sparkles-web-renderer','ytd-promoted-video-renderer',
    'ytd-display-ad-renderer','ytd-companion-slot-renderer',
    'ytd-action-companion-ad-renderer','ytd-in-feed-ad-layout-renderer',
    'ytd-ad-slot-renderer','ytd-banner-promo-renderer',
    '#masthead-ad','#player-ads',
    '.ytp-ad-overlay-container','.ytp-ad-text-overlay',
    'tp-yt-paper-dialog.ytd-popup-container',
    // Social media feed ads
    '[data-testid=""placementTracking""]',
    'article[data-promoted]','[class*=""promoted-tweet""]',
    // Common newsletter/signup popups that look like ads
    '.newsletter-popup','.popup-overlay','[class*=""exit-intent""]'
  ].join(',');

  // ═══════════════════════════════════════════════════════════════════════════
  // REMOVE ADS
  // ═══════════════════════════════════════════════════════════════════════════
  var _removed = 0;

  function removeAds(root) {
    try {
      var els = root.querySelectorAll(AD_SELECTORS);
      for (var i = 0; i < els.length; i++) {
        try { els[i].remove(); _removed++; } catch(e) {}
      }
    } catch(e) {}
  }

  removeAds(document);

  // ═══════════════════════════════════════════════════════════════════════════
  // MUTATION OBSERVER — catch dynamically injected ads
  // ═══════════════════════════════════════════════════════════════════════════
  var _pendingCheck = false;
  function scheduleAdCheck() {
    if (_pendingCheck) return;
    _pendingCheck = true;
    requestAnimationFrame(function() {
      _pendingCheck = false;
      removeAds(document);
      hideAdImages(document);
    });
  }

  new MutationObserver(function(mutations) {
    var dominated = false;
    for (var i = 0; i < mutations.length; i++) {
      var added = mutations[i].addedNodes;
      for (var j = 0; j < added.length; j++) {
        var node = added[j];
        if (node.nodeType !== 1) continue;
        try {
          if (node.matches && node.matches(AD_SELECTORS)) {
            node.remove(); _removed++;
          } else if (node.querySelectorAll) {
            var inner = node.querySelectorAll(AD_SELECTORS);
            for (var k = 0; k < inner.length; k++) {
              try { inner[k].remove(); _removed++; } catch(e) {}
            }
          }
        } catch(e) { dominated = true; }
      }
    }
    if (dominated) scheduleAdCheck();
  }).observe(document.documentElement, { childList: true, subtree: true });

  // ═══════════════════════════════════════════════════════════════════════════
  // COLLAPSE AD ELEMENTS & EMPTY PARENTS
  // ═══════════════════════════════════════════════════════════════════════════
  var HIDE_CSS = ';display:none!important;visibility:hidden!important;height:0!important;min-height:0!important;overflow:hidden!important;max-height:0!important;';

  function collapseAdElement(el) {
    try {
      el.style.cssText += HIDE_CSS;
      var parent = el.parentElement;
      for (var i = 0; i < 4 && parent && parent !== document.body && parent !== document.documentElement; i++) {
        var hasVisible = false;
        for (var j = 0; j < parent.children.length; j++) {
          var c = parent.children[j];
          if (c !== el && c.offsetHeight > 0 && getComputedStyle(c).display !== 'none') {
            hasVisible = true; break;
          }
        }
        if (!hasVisible) {
          parent.style.cssText += HIDE_CSS;
          parent = parent.parentElement;
        } else break;
      }
    } catch(e) {}
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // HIDE AD IMAGES & TRACKING PIXELS
  // ═══════════════════════════════════════════════════════════════════════════
  var AD_IMG_RE = /[/?=](ads?|advert(?:isement)?|banner|bnr|sponsor|promo|adserver|adimg)[/?=.]|\/(ads?|banners?|advertisements?|sponsors?|adserver|adimages?)\//i;

  function isAdSrc(src) { return src && (AD_IMG_RE.test(src) || /\/banners?\//i.test(src)); }

  function processImage(img) {
    var src = img.src || img.getAttribute('data-src') || '';
    if (isAdSrc(src)) collapseAdElement(img);
    // Collapse 1x1 tracking pixels
    if (img.naturalWidth <= 1 && img.naturalHeight <= 1 && src && src.indexOf('http') === 0) {
      collapseAdElement(img);
    }
  }

  function processPlugin(el) {
    var src = el.data || el.src || el.getAttribute('data') || el.getAttribute('src') || '';
    if (isAdSrc(src) || /shockwave|flash/i.test(el.type || '')) collapseAdElement(el);
  }

  function hideAdImages(root) {
    try {
      root.querySelectorAll('img[src],img[data-src]').forEach(processImage);
      root.querySelectorAll('object[data],object[type],embed[src],embed[type]').forEach(processPlugin);
    } catch(e) {}
  }

  // Error handler for blocked resources
  document.addEventListener('error', function(e) {
    try {
      var t = e.target;
      if (t && (t.tagName === 'IMG' || t.tagName === 'EMBED' || t.tagName === 'OBJECT' || t.tagName === 'IFRAME')) {
        var src = t.src || t.data || t.getAttribute('data') || '';
        if (isAdSrc(src)) collapseAdElement(t);
      }
    } catch(ex) {}
  }, true);

  document.addEventListener('load', function(e) {
    try {
      var t = e.target;
      if (t && t.tagName === 'IMG') {
        var src = t.src || '';
        if (isAdSrc(src) && t.naturalWidth === 0 && t.naturalHeight === 0) collapseAdElement(t);
        if (t.naturalWidth <= 1 && t.naturalHeight <= 1 && src) collapseAdElement(t);
      }
    } catch(ex) {}
  }, true);

  hideAdImages(document);

  new MutationObserver(function(muts) {
    muts.forEach(function(m) {
      m.addedNodes.forEach(function(n) {
        if (n.nodeType === 1) {
          hideAdImages(n);
          if (n.tagName === 'IMG') processImage(n);
          if (n.tagName === 'OBJECT' || n.tagName === 'EMBED') processPlugin(n);
        }
      });
    });
  }).observe(document.documentElement, { childList: true, subtree: true });

  // ═══════════════════════════════════════════════════════════════════════════
  // STICKY / FIXED POSITION AD DETECTION (the annoying overlays)
  // ═══════════════════════════════════════════════════════════════════════════
  function removeFixedAds() {
    try {
      var allFixed = document.querySelectorAll('[style*=""position: fixed""], [style*=""position:fixed""]');
      for (var i = 0; i < allFixed.length; i++) {
        var el = allFixed[i];
        // Skip elements that are clearly navigation/headers
        if (el.tagName === 'NAV' || el.tagName === 'HEADER') continue;
        if (el.id === 'navbar' || el.id === 'header' || el.id === 'nav') continue;
        if (el.classList.contains('navbar') || el.classList.contains('header') || el.classList.contains('nav')) continue;
        // Check if it contains ad-related content
        var html = el.innerHTML || '';
        var txt = el.textContent || '';
        if (/googlesyndication|doubleclick|adsbygoogle|taboola|outbrain|sponsored/i.test(html) ||
            /close.{0,5}ad|dismiss|accept.*cookie|cookie.*accept/i.test(txt)) {
          el.remove(); _removed++;
        }
        // Check iframes inside
        var iframes = el.querySelectorAll('iframe');
        for (var j = 0; j < iframes.length; j++) {
          var iSrc = iframes[j].src || '';
          if (/googlesyndication|doubleclick|adnxs|taboola|outbrain|criteo/i.test(iSrc)) {
            el.remove(); _removed++;
            break;
          }
        }
      }
    } catch(e) {}
  }

  setTimeout(removeFixedAds, 2000);
  setTimeout(removeFixedAds, 5000);
  setTimeout(removeFixedAds, 10000);

  // ═══════════════════════════════════════════════════════════════════════════
  // ANTI-ADBLOCK DETECTION BYPASS
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    // Create a fake adsbygoogle element that passes detection checks
    var fakeAd = document.createElement('ins');
    fakeAd.className = 'adsbygoogle';
    fakeAd.style.cssText = 'display:block!important;position:absolute!important;left:-9999px!important;top:-9999px!important;width:1px!important;height:1px!important;overflow:hidden!important;';
    (document.body || document.documentElement).appendChild(fakeAd);
  } catch(e) {}

  // Override ad-detect functions that check offsetHeight
  try {
    var _origGetBCR = Element.prototype.getBoundingClientRect;
    Element.prototype.getBoundingClientRect = function() {
      var result = _origGetBCR.call(this);
      // If this is a hidden adblock detection element, return fake dimensions
      if (this.classList && (this.classList.contains('adsbygoogle') || this.classList.contains('adsbox') ||
          this.id === 'ad-test' || this.id === 'detect-adb')) {
        if (result.height === 0) {
          return { top: result.top, left: result.left, bottom: result.top + 1, right: result.left + 1,
                   width: 1, height: 1, x: result.x, y: result.y };
        }
      }
      return result;
    };
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // INJECT HIDE CSS for faster paint
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    var style = document.createElement('style');
    style.textContent =
      'object[type*=""shockwave""],object[type*=""flash""],embed[type*=""shockwave""],embed[type*=""flash""],' +
      'object[data*=""banner""],embed[src*=""banner""],object[data*=""/ads/""],embed[src*=""/ads/""],' +
      '#ad,#ads,#advert,#advertisement,#ad-container,#ad-wrapper,#ad-banner,#ad-unit,#banner-ad,' +
      '[id^=""ad-""][id*=""banner""],[class*=""ad-banner""],[class*=""banner-ad""],' +
      '.ad-container,.ad-wrapper,.ad-banner,.ad-unit,.ad-slot,' +
      'ins.adsbygoogle,amp-ad,amp-embed,amp-sticky-ad,' +
      'ytd-promoted-sparkles-web-renderer,ytd-promoted-video-renderer,' +
      'ytd-display-ad-renderer,ytd-ad-slot-renderer,#masthead-ad,#player-ads,' +
      '.ytp-ad-overlay-container,.ytp-ad-text-overlay' +
      '{display:none!important;height:0!important;min-height:0!important;' +
      'visibility:hidden!important;overflow:hidden!important;max-height:0!important;}';
    (document.head || document.documentElement).appendChild(style);
  } catch(e) {}

  // Log count
  setTimeout(function() {
    if (_removed > 0) console.log('[YCB AdBlock] Removed ' + _removed + ' ad elements');
  }, 3000);
})();
";

    private string? FindCopilotExe()
    {
        // Check common locations
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packagesDir = IoPath.Combine(programFiles, "Microsoft", "WinGet", "Packages");
        
        if (Directory.Exists(packagesDir))
        {
            foreach (var dir in Directory.GetDirectories(packagesDir))
            {
                if (IoPath.GetFileName(dir).StartsWith("GitHub.Copilot_", StringComparison.OrdinalIgnoreCase))
                {
                    var exe = IoPath.Combine(dir, "copilot.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
        }
        
        // Check if copilot is in PATH
        try
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "copilot",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            proc?.WaitForExit(3000);
            var output = proc?.StandardOutput.ReadToEnd()?.Trim();
            if (!string.IsNullOrEmpty(output) && File.Exists(output.Split('\n')[0]))
            {
                return output.Split('\n')[0].Trim();
            }
        }
        catch { }
        
        // Fallback to just "copilot" and hope it's in PATH
        return "copilot";
    }
    
    private void AddCopilotMessage(string message, bool isUser)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                isUser ? (_isDarkMode ? "#1e3a5f" : "#dbeafe") : (_isDarkMode ? "#24263a" : "#f1f3f6"))!),
            CornerRadius = new CornerRadius(isUser ? 14 : 14, isUser ? 14 : 14, isUser ? 3 : 14, isUser ? 14 : 3),
            Padding = new Thickness(13, 10, 13, 10),
            Margin = new Thickness(isUser ? 36 : 0, 5, isUser ? 0 : 36, 5),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 290
        };

        if (isUser)
        {
            bubble.Child = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    _isDarkMode ? "#cce3ff" : "#1a3a6b")!),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            };
        }
        else
        {
            bubble.Child = BuildAssistantMessageUI(message);
        }
        
        MessagesPanel.Children.Add(bubble);
        CopilotMessages.ScrollToEnd();
    }

    // Renders assistant messages with code block support (``` ... ```)
    private FrameworkElement BuildAssistantMessageUI(string text)
    {
        bool dark = _isDarkMode;
        string textColor  = dark ? "#e8eaed" : "#202124";
        string codeBg     = dark ? "#0d1117"  : "#f6f8fa";
        string codeBorder = dark ? "#30363d"  : "#d0d7de";
        string codeText   = dark ? "#e6edf3"  : "#24292f";
        string copyBg     = dark ? "#21262d"  : "#f0f3f6";
        string copyFg     = dark ? "#8b949e"  : "#57606a";
        string copyBd     = dark ? "#30363d"  : "#d0d7de";

        // Split on triple-backtick code fences
        var parts = System.Text.RegularExpressions.Regex.Split(text, @"```(?:\w+)?");
        
        // Outer container with copy-all button at bottom
        var outer = new StackPanel { Orientation = Orientation.Vertical };

        if (parts.Length <= 1)
        {
            // No code blocks — plain text with selectable TextBox
            outer.Children.Add(MakeSelectableText(text, textColor));
        }
        else
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                if (i % 2 == 0)
                {
                    var segment = parts[i].Trim('\r', '\n');
                    if (!string.IsNullOrWhiteSpace(segment))
                        outer.Children.Add(MakeSelectableText(segment, textColor));
                }
                else
                {
                    // Code block
                    var code = parts[i].Trim('\r', '\n');
                    var codeBorderEl = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(codeBg)!),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12, 8, 12, 10),
                        Margin = new Thickness(0, 6, 0, 6),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(codeBorder)!),
                        BorderThickness = new Thickness(1)
                    };
                    var codePanel = new StackPanel();
                    // Copy button row
                    var copyRow = new Grid();
                    copyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    copyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var copyBtn = new Button
                    {
                        Content = "Copy",
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(copyBg)!),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(copyFg)!),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(copyBd)!),
                        BorderThickness = new Thickness(1),
                        FontSize = 11,
                        Padding = new Thickness(8, 3, 8, 3),
                        Cursor = Cursors.Hand,
                        Tag = code
                    };
                    copyBtn.Click += (s, _) => {
                        try
                        {
                            Clipboard.SetText((string)((Button)s).Tag);
                            ((Button)s).Content = "Copied!";
                            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                            t.Tick += (_, __) => { ((Button)s).Content = "Copy"; t.Stop(); };
                            t.Start();
                        }
                        catch { }
                    };
                    Grid.SetColumn(copyBtn, 1);
                    copyRow.Children.Add(copyBtn);
                    codePanel.Children.Add(copyRow);
                    var codeText2 = new TextBox
                    {
                        Text = code,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(codeText)!),
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        FontFamily = new System.Windows.Media.FontFamily("Cascadia Code, Consolas, Courier New"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 6, 0, 0),
                        IsReadOnly = true,
                        Padding = new Thickness(0),
                        CaretBrush = System.Windows.Media.Brushes.Transparent
                    };
                    codePanel.Children.Add(codeText2);
                    codeBorderEl.Child = codePanel;
                    outer.Children.Add(codeBorderEl);
                }
            }
        }

        // "Copy message" button at bottom right
        var msgCopyGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        msgCopyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        msgCopyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var msgCopyBtn = new Button
        {
            Content = "📋 Copy",
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#9aa0a6" : "#5f6368")!),
            BorderThickness = new Thickness(0),
            FontSize = 11,
            Padding = new Thickness(6, 2, 6, 2),
            Cursor = Cursors.Hand,
            Tag = text
        };
        msgCopyBtn.Click += (s, _) => {
            try
            {
                Clipboard.SetText((string)((Button)s).Tag);
                ((Button)s).Content = "✅ Copied!";
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                t.Tick += (_, __) => { ((Button)s).Content = "📋 Copy"; t.Stop(); };
                t.Start();
            }
            catch { }
        };
        Grid.SetColumn(msgCopyBtn, 1);
        msgCopyGrid.Children.Add(msgCopyBtn);
        outer.Children.Add(msgCopyGrid);

        return outer;
    }

    private static FrameworkElement MakeSelectableText(string text, string color)
    {
        return new TextBox
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            IsReadOnly = true,
            Padding = new Thickness(0),
            CaretBrush = System.Windows.Media.Brushes.Transparent,
            Margin = new Thickness(0, 2, 0, 2)
        };
    }
    
    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = true;
    }
    
    private async void MenuNewTab_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        await CreateTab(_settings.HomePage ?? "ycb://newtab");
    }
    
    private void MenuNewWindow_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        OpenNewWindow();
    }
    
    private void MenuNewIncognitoWindow_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        OpenIncognitoWindow();
    }
    
    private void MenuBookmarks_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        RefreshBookmarksPopup();
        BookmarksPopup.IsOpen = true;
    }

    private void RefreshBookmarksPopup()
    {
        // Pre-fill name with current page title
        var title = _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count
            ? _tabs[_activeTabIndex].Title ?? ""
            : "";
        BookmarkNameBox.Text = title;

        // Populate bookmarks list
        BookmarksList.Children.Clear();
        var bookmarks = LoadBookmarks();
        if (!bookmarks.Any())
        {
            BookmarksList.Children.Add(new TextBlock
            {
                Text = "No bookmarks saved yet.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9aa0a6")!),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2)
            });
            return;
        }

        for (int i = 0; i < bookmarks.Count; i++)
        {
            var bm = bookmarks[i];
            var idx = i;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBtn = new Button
            {
                Content = new TextBlock
                {
                    Text = bm.Label ?? bm.Title ?? bm.Url,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e8eaed")!),
                    FontSize = 13
                },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 5, 4, 5),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                ToolTip = bm.Url
            };
            nameBtn.Click += (s, ev) =>
            {
                BookmarksPopup.IsOpen = false;
                if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                    _tabs[_activeTabIndex].WebView.CoreWebView2.Navigate(bm.Url);
            };
            Grid.SetColumn(nameBtn, 0);

            var delBtn = new Button
            {
                Content = new TextBlock
                {
                    Text = "✕",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9aa0a6")!),
                    FontSize = 11
                },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 5, 6, 5),
                Cursor = Cursors.Hand,
                ToolTip = "Remove bookmark"
            };
            delBtn.Click += (s, ev) =>
            {
                RemoveBookmark(idx);
                RefreshBookmarkStar();
                RefreshBookmarksPopup();
            };
            Grid.SetColumn(delBtn, 1);

            row.Children.Add(nameBtn);
            row.Children.Add(delBtn);
            BookmarksList.Children.Add(row);
        }
    }

    private void BookmarkSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count) return;
        var url = _tabs[_activeTabIndex].Url ?? "";
        if (string.IsNullOrEmpty(url) || url.StartsWith("ycb://")) return;

        var name = BookmarkNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
            name = _tabs[_activeTabIndex].Title ?? url;

        var bookmarks = LoadBookmarks();
        var existing = bookmarks.FindIndex(b => b.Url == url);
        if (existing >= 0)
            RemoveBookmark(existing);
        else
            AddBookmark(url, name);

        RefreshBookmarkStar();
        RefreshBookmarksPopup();
    }

    private async void MenuHistory_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        await CreateTab("ycb://history");
    }
    
    private async void MenuDownloads_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        await CreateTab("ycb://downloads");
    }
    
    private async void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        await CreateTab("ycb://settings");
    }
    
    private async void MenuPasswords_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        await CreateTab("ycb://passwords");
    }
    
    private void MenuSupport_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        _ = CreateTab("https://ycb.tomcreations.org/auth/login?next=/support");
    }

    // Maps a live file:// URL back to its ycb:// equivalent so internal pages survive reload
    private string GetReloadUrl(BrowserTab tab)
    {
        // Try live source first
        var live = tab.WebView.Source?.ToString();
        if (!string.IsNullOrEmpty(live) && live.StartsWith("file:///"))
        {
            if (live.Contains("settings.html"))  return "ycb://settings";
            if (live.Contains("history.html"))   return "ycb://history";
            if (live.Contains("downloads.html")) return "ycb://downloads";
            if (live.Contains("passwords.html")) return "ycb://passwords";
            if (live.Contains("guide.html"))     return "ycb://guide";
            if (live.Contains("support.html"))   return "ycb://support";
            if (live.Contains("newtab.html"))    return "ycb://newtab";
        }
        if (!string.IsNullOrEmpty(live) &&
            (live.StartsWith("http://") || live.StartsWith("https://")))
            return live;
        // Fall back to stored URL
        var stored = tab.Url ?? "";
        if (stored.StartsWith("ycb://") || stored.StartsWith("http://") || stored.StartsWith("https://"))
            return stored;
        return "ycb://newtab";
    }

    private async Task TrySilentSupportLogin(WebView2 webView)
    {
        try
        {
            var userId = ErrorReporter.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                System.Diagnostics.Debug.WriteLine("[SilentLogin] No user ID available, skipping.");
                return;
            }

            var safeId = userId.Replace("\\", "\\\\").Replace("'", "\\'");
            System.Diagnostics.Debug.WriteLine($"[SilentLogin] POSTing user ID to /auth/ycbuseridlogin");

            // Run fetch from within the page's own origin so the browser
            // handles Set-Cookie automatically — no manual cookie injection needed
            var js = $@"
(function() {{
    console.log('[YCB SilentLogin] Starting POST for user: {safeId}');
    fetch('/auth/ycbuseridlogin', {{
        method: 'POST',
        headers: {{ 'Content-Type': 'application/x-www-form-urlencoded' }},
        body: 'ycb_user_id={safeId}',
        credentials: 'include',
        redirect: 'follow'
    }}).then(function(r) {{
        console.log('[YCB SilentLogin] Response status: ' + r.status + ' url: ' + r.url);
        window.location.href = '/support';
    }}).catch(function(err) {{
        console.error('[YCB SilentLogin] Fetch error: ' + err);
        window.location.href = '/support';
    }});
}})();";

            await webView.ExecuteScriptAsync(js);
            System.Diagnostics.Debug.WriteLine("[SilentLogin] JS injected OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SilentLogin] Exception: {ex.Message}");
        }
    }

    private async void MenuGuide_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        await CreateTab("ycb://guide");
    }
    
    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        _zoomFactor = Math.Min(_zoomFactor + 0.1, 3.0);
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].WebView.ZoomFactor = _zoomFactor;
        }
    }
    
    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        _zoomFactor = Math.Max(_zoomFactor - 0.1, 0.5);
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].WebView.ZoomFactor = _zoomFactor;
        }
    }
    
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].WebView.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }
    }
    
    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        _isDarkMode = !_isDarkMode;
        ApplyTheme();
        SaveSettings();
        MenuPopup.IsOpen = false;
    }
    
    private void ApplyTheme()
    {
        var themeText = ThemeToggle.Content as TextBlock;
        string iconColor;
        
        if (_isDarkMode)
        {
            iconColor = "#9aa0a6";
            // Dark theme colors
            MainGrid.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202124")!);
            TabStrip.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202124")!);
            Toolbar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#35363a")!);
            OmniboxBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#292b2f")!);
            OmniboxBorder.BorderBrush = null;
            OmniboxBorder.BorderThickness = new Thickness(0);
            UrlBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e8eaed")!);
            UrlBox.CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!);
            UrlPlaceholder.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5f6368")!);
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202124")!);
            NewTabBtn.Style = (Style)FindResource("NewTabBtnStyle");
            if (themeText != null) themeText.Text = "Light Mode";
        }
        else
        {
            iconColor = "#5f6368";
            // Light theme colors — neutral cool gray, not lavender
            MainGrid.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#dee1e6")!);
            TabStrip.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#dee1e6")!);
            Toolbar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff")!);
            OmniboxBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f1f3f4")!);
            OmniboxBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#dfe1e5")!);
            OmniboxBorder.BorderThickness = new Thickness(1);
            UrlBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202124")!);
            UrlBox.CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202124")!);
            UrlPlaceholder.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80868b")!);
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#dee1e6")!);
            NewTabBtn.Style = (Style)FindResource("LightNewTabBtnStyle");
            if (themeText != null) themeText.Text = "Dark Mode";
        }
        
        // Recolor all toolbar icons
        var iconBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconColor)!);
        BackIcon.Stroke = iconBrush; ForwardIcon.Stroke = iconBrush;
        RefreshArc.Stroke = iconBrush; RefreshArrow.Stroke = iconBrush;
        NewTabIcon.Stroke = iconBrush;
        MinIcon.Fill = iconBrush; MaxIcon.Stroke = iconBrush; CloseIcon.Stroke = iconBrush;
        MenuDot1.Fill = iconBrush; MenuDot2.Fill = iconBrush; MenuDot3.Fill = iconBrush;
        CopilotHead.Stroke = iconBrush;
        CopilotEyeL.Fill = iconBrush; CopilotEyeR.Fill = iconBrush;
        CopilotMouth.Stroke = iconBrush; CopilotBody.Stroke = iconBrush;
        if (!BookmarkStarPath.Fill.Equals(Brushes.Transparent))
        {
            // Bookmarked — keep yellow
        }
        else
        {
            BookmarkStarPath.Stroke = iconBrush;
        }
        
        // Update all tab styles and title colors based on theme
        string inactiveStyle = _isDarkMode ? "TabStyle" : "LightTabStyle";
        string activeStyle   = _isDarkMode ? "ActiveTabStyle" : "LightActiveTabStyle";
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool isActive = i == _activeTabIndex;
            _tabs[i].TabButton.Style = (Style)FindResource(isActive ? activeStyle : inactiveStyle);
            
            if (_tabs[i].TabButton.Content is Grid grid)
            {
                var title = grid.Children.OfType<TextBlock>().FirstOrDefault();
                if (title != null)
                {
                    title.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                        isActive ? (_isDarkMode ? "#e8eaed" : "#202124") : (_isDarkMode ? "#9aa0a6" : "#202124"))!);
                }
            }
            
            // Update WebView background color
            _tabs[i].WebView.DefaultBackgroundColor = _isDarkMode 
                ? System.Drawing.Color.FromArgb(255, 32, 33, 36)  // #202124
                : System.Drawing.Color.FromArgb(255, 255, 255, 255);  // white
        }
        
        // Update suggestion popup for current theme
        OmniSuggestion.IsDark = _isDarkMode;
        OmniSuggestion.ThemePrimary   = _isDarkMode ? "#e8eaed" : "#202124";
        OmniSuggestion.ThemeSecondary = _isDarkMode ? "#9aa0a6" : "#5f6368";
        SuggestBorder.Background  = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#2d2e31" : "#ffffff")!);
        SuggestBorder.BorderBrush = _isDarkMode
            ? new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        
        // Update copilot sidebar theme
        CopilotSidebar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#1a1b26" : "#f8f9fa")!);
        CopilotSidebar.BorderBrush = _isDarkMode
            ? new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
        CopilotHeaderBorder.Background = _isDarkMode
            ? new SolidColorBrush(Color.FromArgb(5, 255, 255, 255))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff")!);
        CopilotHeaderBorder.BorderBrush = _isDarkMode
            ? new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
        CopilotTitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!);
        CopilotSubtitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#9aa0a6" : "#5f6368")!);
        CopilotInputAreaBorder.Background = _isDarkMode
            ? new SolidColorBrush(Color.FromArgb(5, 255, 255, 255))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff")!);
        CopilotInputAreaBorder.BorderBrush = _isDarkMode
            ? new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
        CopilotInputFieldBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#2a2b36" : "#f1f3f4")!);
        CopilotInputFieldBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#3d3e4a" : "#dfe1e5")!);
        CopilotInput.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!);
        // Update menu popup theme
        MenuPopupBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#2d2e31" : "#ffffff")!);
        MenuPopupBorder.BorderBrush = _isDarkMode
            ? new SolidColorBrush(Color.FromArgb(18, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        var menuFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!);
        foreach (var child in LogicalTreeHelper.GetChildren(MenuPopupBorder.Child as StackPanel ?? new StackPanel()))
        {
            if (child is Button mb) mb.Foreground = menuFg;
        }
        
        // Rebuild ItemContainerStyle so hover/selected backgrounds match theme
        var hoverColor = (Color)ColorConverter.ConvertFromString(_isDarkMode ? "#3c4043" : "#f1f3f4")!;
        var fgColor    = (Color)ColorConverter.ConvertFromString(_isDarkMode ? "#e8eaed" : "#202124")!;
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(fgColor)));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        itemStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var bdFactory = new FrameworkElementFactory(typeof(Border), "Bd");
        bdFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent), Path = new PropertyPath(Control.BackgroundProperty) });
        bdFactory.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent), Path = new PropertyPath(Control.PaddingProperty) });
        var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        bdFactory.AppendChild(cpFactory);
        var ct = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = bdFactory };
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "Bd"));
        ct.Triggers.Add(hoverTrigger);
        var selTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "Bd"));
        ct.Triggers.Add(selTrigger);
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, ct));
        SuggestionsList.ItemContainerStyle = itemStyle;
    }
    
    // Download shelf
    private void ShowDownloadShelf(DownloadItem item)
    {
        DownloadShelf.Visibility = Visibility.Visible;
        DownloadShelfRow.Height = new GridLength(72);

        var itemBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3c3d41")!),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 200,
            MaxWidth = 280,
            Tag = item.FilePath
        };

        var outer = new StackPanel { Orientation = Orientation.Vertical };

        // Top row: icon + name + status
        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconPath = new WpfPath
        {
            Data = Geometry.Parse("M4 2h8l4 4v12a2 2 0 01-2 2H4a2 2 0 01-2-2V4a2 2 0 012-2z"),
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            StrokeThickness = 1.5,
            Width = 20, Height = 24,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(iconPath, 0);
        topGrid.Children.Add(iconPath);

        var infoStack = new StackPanel();
        var nameBlock = new TextBlock
        {
            Text = item.Filename,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e8eaed")!),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var statusBlock = new TextBlock
        {
            Text = item.Status,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9aa0a6")!),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        };
        infoStack.Children.Add(nameBlock);
        infoStack.Children.Add(statusBlock);
        Grid.SetColumn(infoStack, 1);
        topGrid.Children.Add(infoStack);
        outer.Children.Add(topGrid);

        // Action buttons row (hidden until complete)
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 5, 0, 0),
            Visibility = Visibility.Collapsed,
            Tag = "actionRow"
        };

        var openBtn = MakeShelfButton("Open");
        openBtn.Click += (s, e) =>
        {
            if (File.Exists(item.FilePath))
                Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
        };

        var folderBtn = MakeShelfButton("Open folder");
        folderBtn.Click += (s, e) =>
        {
            if (File.Exists(item.FilePath))
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
        };

        btnRow.Children.Add(openBtn);
        btnRow.Children.Add(folderBtn);
        outer.Children.Add(btnRow);

        itemBorder.Child = outer;
        DownloadItems.Children.Add(itemBorder);
    }

    private static Button MakeShelfButton(string label) => new Button
    {
        Content = label,
        FontSize = 11,
        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a3a4a")!),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(8, 3, 8, 3),
        Margin = new Thickness(0, 0, 6, 0),
        Cursor = System.Windows.Input.Cursors.Hand,
        Template = CreateFlatButtonTemplate()
    };

    private static ControlTemplate CreateFlatButtonTemplate()
    {
        var tpl = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        tpl.VisualTree = border;
        return tpl;
    }

    private void UpdateDownloadItem(DownloadItem item)
    {
        foreach (Border border in DownloadItems.Children.OfType<Border>())
        {
            if (border.Tag?.ToString() != item.FilePath) continue;
            if (border.Child is not StackPanel outer) continue;

            // Update status text
            var topGrid = outer.Children.OfType<Grid>().FirstOrDefault();
            var infoStack = topGrid?.Children.OfType<StackPanel>().FirstOrDefault();
            var statusBlock = infoStack?.Children.OfType<TextBlock>().Skip(1).FirstOrDefault();
            if (statusBlock != null)
                statusBlock.Text = item.Status;

            // Show action buttons when complete
            if (item.State == "completed")
            {
                var btnRow = outer.Children.OfType<StackPanel>()
                                  .FirstOrDefault(p => p.Tag?.ToString() == "actionRow");
                if (btnRow != null)
                    btnRow.Visibility = Visibility.Visible;
            }
        }
    }
    
    private async void SeeAllDownloads_Click(object sender, RoutedEventArgs e)
    {
        await CreateTab("ycb://downloads");
    }
    
    private void CloseDownloadShelf_Click(object sender, RoutedEventArgs e)
    {
        DownloadShelf.Visibility = Visibility.Collapsed;
        DownloadShelfRow.Height = new GridLength(0);
        DownloadItems.Children.Clear();
    }
    
    // ─── PASSWORD CONTENT SCRIPT ────────────────────────────────────────
    private const string PasswordContentScript = @"
(function() {
    if (window.__ycbPwInjected) return;
    window.__ycbPwInjected = true;
    var __ycbAutoFillSent = false;

    // On form submit, capture credentials
    document.addEventListener('submit', function(e) {
        var form = e.target;
        var pwField = form.querySelector('input[type=""password""]');
        if (!pwField || !pwField.value) return;
        var q = 'input[type=""email""],input[type=""text""],input[autocomplete~=""username""],input[name*=""user"" i],input[name*=""login"" i],input[name*=""email"" i],input[id*=""user"" i],input[id*=""email"" i]';
        var userField = form.querySelector(q);
        var username = userField ? userField.value : '';
        console.log('__passwords__:SAVE_PROMPT:' + encodeURIComponent(window.location.href) + '|' + encodeURIComponent(username) + '|' + encodeURIComponent(pwField.value));
    }, true);

    // Check for password fields — only send once per page
    function checkPwFields() {
        if (!__ycbAutoFillSent && document.querySelector('input[type=""password""]')) {
            __ycbAutoFillSent = true;
            console.log('__passwords__:AUTOFILL_CHECK:' + encodeURIComponent(window.location.hostname));
        }
    }
    checkPwFields();
    setTimeout(checkPwFields, 1000);
    setTimeout(checkPwFields, 2500);
})();
";

    private static string BuildAutofillScript(string entriesJson)
    {
        return $@"
(function(entries) {{
    if (!entries || !entries.length) return;
    var pwFields = Array.from(document.querySelectorAll('input[type=""password""]'));
    if (!pwFields.length) return;
    var entry = entries[0];

    // Use native setter so React/Vue/Angular controlled inputs update correctly
    function fillField(el, value) {{
        try {{
            var proto = el.constructor && el.constructor.prototype || window.HTMLInputElement.prototype;
            var desc = Object.getOwnPropertyDescriptor(proto, 'value') ||
                       Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value');
            if (desc && desc.set) desc.set.call(el, value);
            else el.value = value;
        }} catch(err) {{ el.value = value; }}
        el.dispatchEvent(new Event('input',  {{bubbles:true, cancelable:true}}));
        el.dispatchEvent(new Event('change', {{bubbles:true, cancelable:true}}));
        el.dispatchEvent(new KeyboardEvent('keydown', {{bubbles:true}}));
        el.dispatchEvent(new KeyboardEvent('keyup',   {{bubbles:true}}));
    }}

    pwFields.forEach(function(pwField) {{
        var form = pwField.closest('form') || document;
        var q = 'input[type=""email""],input[type=""text""],input[autocomplete~=""username""],input[name*=""user"" i],input[name*=""login"" i],input[name*=""email"" i],input[id*=""user"" i],input[id*=""email"" i]';
        var uf = form.querySelector(q);
        if (uf) fillField(uf, entry.username || '');
        fillField(pwField, entry.password || '');
    }});
}})({entriesJson});
";
    }

    private async System.Threading.Tasks.Task HandleWebConsole(WebView2 webView, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (!json.RootElement.TryGetProperty("args", out var argsEl)) return;
            if (argsEl.ValueKind != JsonValueKind.Array || argsEl.GetArrayLength() == 0) return;
            if (!argsEl[0].TryGetProperty("value", out var valueEl)) return;
            var message = valueEl.GetString();
            if (message == null) return;

            if (message.StartsWith("__passwords__:SAVE_PROMPT:"))
            {
                var data = message.Substring("__passwords__:SAVE_PROMPT:".Length);
                var parts = data.Split('|');
                if (parts.Length >= 3 && !_isIncognito)
                {
                    var url  = Uri.UnescapeDataString(parts[0]);
                    var user = Uri.UnescapeDataString(parts[1]);
                    var pass = Uri.UnescapeDataString(parts[2]);
                    var domain = GetDomain(url);
                    var existing = LoadPasswords().FirstOrDefault(p => GetDomain(p.Url) == domain && p.Username == user);
                    if (existing == null)
                        await Dispatcher.InvokeAsync(() => ShowSavePasswordPopup(url, user, pass));
                }
            }
            else if (message.StartsWith("__passwords__:AUTOFILL_CHECK:"))
            {
                var domain = Uri.UnescapeDataString(message.Substring("__passwords__:AUTOFILL_CHECK:".Length));
                var passwords = LoadPasswordsDecrypted();
                var matches = passwords.Where(p => GetDomain(p.Url) == domain).ToList();
                if (matches.Any() && !_isIncognito)
                {
                    // Only show prompt once per navigation per tab
                    bool alreadyShown = _autofillShownForTab.TryGetValue(webView, out var shownDomain) && shownDomain == domain;
                    if (!alreadyShown)
                    {
                        _autofillShownForTab[webView] = domain;
                        var entriesJson = JsonSerializer.Serialize(matches);
                        var firstUser = matches[0].Username ?? "";
                        await Dispatcher.InvokeAsync(() => ShowAutofillPopup(webView, entriesJson, domain, firstUser));
                    }
                }
            }
        }
        catch { }
    }

    private void ShowSavePasswordPopup(string url, string username, string password)
    {
        var domain = GetDomain(url);
        ErrorReporter.Track("PwPrompt", new() { ["host"] = domain });
        var subtitle = string.IsNullOrEmpty(username) ? domain : $"{username} · {domain}";
        ShowPasswordPopup(
            "Save password?",
            subtitle,
            "Save",
            () =>
            {
                SavePassword(url, username, password);
            });
    }

    private void ShowAutofillPopup(WebView2 webView, string entriesJson, string domain, string username)
    {
        ErrorReporter.Track("AutofillShown", new() { ["host"] = domain });
        var subtitle = string.IsNullOrEmpty(username) ? domain : $"{username} · {domain}";
        ShowPasswordPopup(
            $"Sign in to {domain}?",
            subtitle,
            "Autofill",
            async () =>
            {
                ErrorReporter.Track("AutofillUsed", new() { ["host"] = domain });
                var script = BuildAutofillScript(entriesJson);
                try { await webView.ExecuteScriptAsync(script); } catch { }
            });
    }

    private void ShowPasswordPopup(string title, string subtitle, string confirmLabel, Action onConfirm)
    {
        var popup = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Owner = this,
            Width = 300,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        // Position below the BookmarkBtn (right side of URL bar)
        var pt = BookmarkBtn.PointToScreen(new Point(BookmarkBtn.ActualWidth / 2, BookmarkBtn.ActualHeight));
        popup.Left = pt.X - 280;
        popup.Top  = pt.Y + 6;

        var border = new Border
        {
            Background       = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#292a2d")!),
            CornerRadius     = new CornerRadius(8),
            BorderBrush      = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3c4043")!),
            BorderThickness  = new Thickness(1),
            Padding          = new Thickness(16, 14, 16, 14)
        };
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 16, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Icon + text
        var topStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var iconCanvas = new Canvas { Width = 18, Height = 18, Margin = new Thickness(0, 1, 10, 0) };
        var lockRect = new System.Windows.Shapes.Rectangle
        {
            Width = 12, Height = 8, RadiusX = 2, RadiusY = 2,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            StrokeThickness = 1.5, Fill = Brushes.Transparent
        };
        System.Windows.Controls.Canvas.SetLeft(lockRect, 3);
        System.Windows.Controls.Canvas.SetTop(lockRect, 9);
        iconCanvas.Children.Add(lockRect);
        var lockArch = new WpfPath
        {
            Data = Geometry.Parse("M5 9 C5 6 7 4.5 9 4.5 C11 4.5 13 6 13 9"),
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            StrokeThickness = 1.5, Fill = Brushes.Transparent,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 18, Height = 18, Stretch = Stretch.None
        };
        iconCanvas.Children.Add(lockArch);
        topStack.Children.Add(iconCanvas);

        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e8eaed")!),
            FontSize = 13, FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrEmpty(subtitle))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9aa0a6")!),
                FontSize = 12, Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220
            });
        }
        topStack.Children.Add(textStack);
        Grid.SetRow(topStack, 0);
        mainGrid.Children.Add(topStack);

        // Buttons
        var btnStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var dismissBtn = new Button
        {
            Content = "Not now", Background = Brushes.Transparent,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            BorderThickness = new Thickness(0), Padding = new Thickness(14, 7, 14, 7),
            Cursor = Cursors.Hand, FontSize = 13
        };

        var confirmBtn = new Button
        {
            Content = confirmLabel,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202124")!),
            BorderThickness = new Thickness(0), Padding = new Thickness(14, 7, 14, 7),
            Cursor = Cursors.Hand, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 0, 0, 0)
        };
        confirmBtn.Resources.Add(typeof(Border), new Style(typeof(Border))
        {
            Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) }
        });

        dismissBtn.Click += (s, a) => popup.Close();
        confirmBtn.Click += (s, a) => { popup.Close(); onConfirm(); };

        btnStack.Children.Add(dismissBtn);
        btnStack.Children.Add(confirmBtn);
        Grid.SetRow(btnStack, 1);
        mainGrid.Children.Add(btnStack);

        border.Child = mainGrid;
        popup.Content = border;
        popup.Deactivated += (s, a) => { try { if (popup.IsVisible) popup.Close(); } catch { } };
        popup.Show();
        ForcePopupOnTop(popup);
        TrackPopupPosition(popup, () => { var pt = BookmarkBtn.PointToScreen(new Point(BookmarkBtn.ActualWidth / 2, BookmarkBtn.ActualHeight)); return (pt.X - 280, pt.Y + 6); });
    }

    private void ShowRestorePrompt()
    {
        var count = _settings.LastTabs?.Count ?? 0;
        if (count == 0) return;

        var popup = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Owner = this,
            Width = 300,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        // Position below the toolbar, right-aligned inside the browser window
        var winPt = this.PointToScreen(new Point(this.ActualWidth, 36 + 46));
        popup.Left = winPt.X - 310;  // 300px popup + 10px margin from right edge
        popup.Top  = winPt.Y + 4;

        var border = new Border
        {
            Background      = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#292a2d")!),
            CornerRadius    = new CornerRadius(8),
            BorderBrush     = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3c4043")!),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(16, 14, 16, 14)
        };
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 16, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black
        };

        var stack = new StackPanel();

        // Icon + title row
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var iconPath = new WpfPath
        {
            Data = Geometry.Parse("M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0h6"),
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            StrokeThickness = 1.6, Fill = Brushes.Transparent,
            Width = 16, Height = 16, Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 1, 8, 0),
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        var titleBlock = new TextBlock
        {
            Text = "Restore your tabs?",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e8eaed")!),
            FontSize = 13, FontWeight = FontWeights.Medium
        };
        titleRow.Children.Add(iconPath);
        titleRow.Children.Add(titleBlock);
        stack.Children.Add(titleRow);

        var subBlock = new TextBlock
        {
            Text = $"You had {count} tab{(count == 1 ? "" : "s")} open last time",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9aa0a6")!),
            FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(subBlock);

        // List each tab by host + path
        var tabList = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var displayTabs = _settings.LastTabs!.Take(6).ToList();
        foreach (var url in displayTabs)
        {
            string display;
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.TrimEnd('/');
                display = path.Length > 1 ? uri.Host + path : uri.Host;
            }
            catch { display = url.Length > 40 ? url[..40] + "…" : url; }
            if (display.Length > 45) display = display[..45] + "…";
            tabList.Children.Add(new TextBlock
            {
                Text = "• " + display,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#bdc1c6")!),
                FontSize = 11, Margin = new Thickness(4, 1, 0, 1),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        if (count > 6)
            tabList.Children.Add(new TextBlock
            {
                Text = $"  + {count - 6} more…",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5f6368")!),
                FontSize = 11, Margin = new Thickness(4, 1, 0, 0)
            });
        stack.Children.Add(tabList);

        // Buttons
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var freshBtn = new Button
        {
            Content = "Start fresh", FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9aa0a6")!),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        freshBtn.Click += (s, e) =>
        {
            // Track the startup tab so SaveSettings() can exclude it from LastTabs —
            // prevents the restore prompt appearing next session just for the homepage.
            _freshStartTab = _tabs.Count > 0 ? _tabs[0] : null;
            _freshStartTabInitialUrl = _freshStartTab?.Url;
            _settings.LastTabs = null;
            popup.Close();
        };

        var restoreBtn = new Button
        {
            Content = "Restore", FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a2a3a")!),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ab4f8")!),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 6, 14, 6),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        restoreBtn.Click += async (s, e) =>
        {
            popup.Close();
            var tabsToRestore = _settings.LastTabs!.ToList();
            // Open restored tabs first, then close the placeholder
            foreach (var url in tabsToRestore)
                await CreateTab(url);
            // Now safe to close the placeholder (index 0) since we have other tabs open
            if (_tabs.Count > tabsToRestore.Count)
                CloseTab(0);
        };

        btnRow.Children.Add(freshBtn);
        btnRow.Children.Add(restoreBtn);
        stack.Children.Add(btnRow);

        border.Child = stack;
        popup.Content = border;
        popup.Show();
        ForcePopupOnTop(popup);
        TrackPopupPosition(popup, () => { var p = this.PointToScreen(new Point(this.ActualWidth, 36 + 46)); return (p.X - 310, p.Y + 4); });
    }

    /// <summary>Keeps <paramref name="popup"/> anchored to the same screen position relative to the
    /// main window whenever the window is moved or resized, and ensures it stays above the
    /// WebView2 content even when the main window is activated.</summary>
    private void TrackPopupPosition(Window popup, Func<(double left, double top)> computePosition)
    {
        void Reposition()
        {
            if (!popup.IsVisible) return;
            var (left, top) = computePosition();
            popup.Left = left;
            popup.Top  = top;
            ForcePopupOnTop(popup);
        }
        void OnMoveOrActivate(object? s, EventArgs e) => Reposition();
        void OnSizeChanged(object? s, SizeChangedEventArgs e) => Reposition();
        LocationChanged += OnMoveOrActivate;
        Activated       += OnMoveOrActivate;
        SizeChanged     += OnSizeChanged;
        popup.Closed    += (s, e) =>
        {
            LocationChanged -= OnMoveOrActivate;
            Activated       -= OnMoveOrActivate;
            SizeChanged     -= OnSizeChanged;
        };
    }

    /// <summary>Forces <paramref name="popup"/> to the top of the z-order without stealing focus,
    /// counteracting any airspace z-order disturbance caused by WebView2's HWND.</summary>
    private void ForcePopupOnTop(Window popup)
    {
        var handle = new WindowInteropHelper(popup).Handle;
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }

    private static string GetDomain(string url)
    {
        try { return new Uri(url).Host.ToLowerInvariant(); }
        catch { return url; }
    }
    private void AddToHistory(string? url, string? title)
    {
        if (_isIncognito) return;
        if (string.IsNullOrEmpty(url) || url.StartsWith("ycb://")) return;
        // Ignore entries fired immediately after a clear (WebView2 can fire
        // DocumentTitleChanged on all open tabs right after ClearBrowsingData)
        if ((DateTime.UtcNow - _historyClearedAt).TotalSeconds < 3) return;
        
        try
        {
            var history = LoadHistory();
            history.Insert(0, new HistoryItem { Url = url, Title = title ?? url, Timestamp = DateTime.Now });
            if (history.Count > 1000) history.RemoveAt(history.Count - 1);
            var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_historyPath, json);
        }
        catch { }
    }
    
    private List<HistoryItem> LoadHistory()
    {
        try
        {
            if (File.Exists(_historyPath))
            {
                var json = File.ReadAllText(_historyPath);
                var all = JsonSerializer.Deserialize<List<HistoryItem>>(json) ?? new List<HistoryItem>();
                // Filter out anything older than the last clear — bulletproof even if delete fails
                if (_historyClearedAt > DateTime.MinValue)
                    all = all.Where(h => h.Timestamp > _historyClearedAt).ToList();
                return all;
            }
        }
        catch { }
        return new List<HistoryItem>();
    }
    
    private void SaveDownload(DownloadItem item)
    {
        try
        {
            var downloads = LoadDownloads();
            downloads.Insert(0, item);
            var json = JsonSerializer.Serialize(downloads, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_downloadsPath, json);
        }
        catch { }
    }
    
    private List<DownloadItem> LoadDownloads()
    {
        try
        {
            if (File.Exists(_downloadsPath))
            {
                var json = File.ReadAllText(_downloadsPath);
                return JsonSerializer.Deserialize<List<DownloadItem>>(json) ?? new List<DownloadItem>();
            }
        }
        catch { }
        return new List<DownloadItem>();
    }
    
    private void ClearHistory()
    {
        _historyClearedAt = DateTime.UtcNow;
        _settings.HistoryClearedAt = _historyClearedAt;
        SaveSettings();
        // Delete and recreate as empty — leaves no old data on disk
        try
        {
            File.Delete(_historyPath);
            File.WriteAllText(_historyPath, "[]");
        }
        catch { }
    }
    
    private void ClearDownloads()
    {
        try
        {
            if (File.Exists(_downloadsPath))
            {
                File.Delete(_downloadsPath);
            }
        }
        catch { }
    }
    
    // Bookmark management
    private List<BookmarkItem> LoadBookmarks()
    {
        try
        {
            if (File.Exists(_bookmarksPath))
            {
                var json = File.ReadAllText(_bookmarksPath);
                return JsonSerializer.Deserialize<List<BookmarkItem>>(json) ?? new List<BookmarkItem>();
            }
        }
        catch { }
        return new List<BookmarkItem>();
    }
    
    private void AddBookmark(string url, string label)
    {
        if (string.IsNullOrEmpty(url)) return;
        
        try
        {
            var bookmarks = LoadBookmarks();
            bookmarks.Add(new BookmarkItem { Url = url, Label = label });
            
            var json = JsonSerializer.Serialize(bookmarks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_bookmarksPath, json);
        }
        catch { }
    }
    
    private void RemoveBookmark(int index)
    {
        try
        {
            var bookmarks = LoadBookmarks();
            if (index >= 0 && index < bookmarks.Count)
            {
                bookmarks.RemoveAt(index);
                var json = JsonSerializer.Serialize(bookmarks, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_bookmarksPath, json);
            }
        }
        catch { }
    }
    
    // Password management
    private static string EncryptPassword(string plaintext)
    {
        try
        {
            var data = Encoding.UTF8.GetBytes(plaintext);
            var enc  = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return "DPAPI:" + Convert.ToBase64String(enc);
        }
        catch { return plaintext; }
    }

    private static string DecryptPassword(string stored)
    {
        try
        {
            if (stored.StartsWith("DPAPI:"))
            {
                var data = Convert.FromBase64String(stored.Substring(6));
                var dec  = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            return stored; // legacy plaintext
        }
        catch { return stored; }
    }

    private List<PasswordItem> LoadPasswords()
    {
        try
        {
            if (File.Exists(_passwordsPath))
            {
                var json  = File.ReadAllText(_passwordsPath);
                var items = JsonSerializer.Deserialize<List<PasswordItem>>(json) ?? new List<PasswordItem>();
                bool migrated = false;
                foreach (var item in items)
                {
                    if (!item.Password.StartsWith("DPAPI:"))
                    {
                        item.Password = EncryptPassword(item.Password);
                        migrated = true;
                    }
                }
                if (migrated)
                    File.WriteAllText(_passwordsPath, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
                return items;
            }
        }
        catch { }
        return new List<PasswordItem>();
    }

    private List<PasswordItem> LoadPasswordsDecrypted()
    {
        return LoadPasswords().Select(p => new PasswordItem
        {
            Key      = p.Key,
            Url      = p.Url,
            Username = p.Username,
            Password = DecryptPassword(p.Password)
        }).ToList();
    }
    
    private void SavePassword(string url, string username, string password)
    {
        if (string.IsNullOrEmpty(url)) return;
        var domain = GetDomain(url);
        try
        {
            var passwords = LoadPasswords();
            var existing = passwords.FirstOrDefault(p => GetDomain(p.Url) == domain && p.Username == username);
            var encrypted = EncryptPassword(password);
            if (existing != null)
            {
                existing.Password = encrypted;
                existing.Url = url;
                ErrorReporter.Track("PwSaved", new() { ["host"] = domain, ["upd"] = true });
            }
            else
            {
                passwords.Add(new PasswordItem
                {
                    Key = $"{domain}_{username}",
                    Url = url,
                    Username = username,
                    Password = encrypted
                });
                ErrorReporter.Track("PwSaved", new() { ["host"] = domain, ["upd"] = false });
            }
            var json = JsonSerializer.Serialize(passwords, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_passwordsPath, json);
        }
        catch { }
    }
    
    private void DeletePassword(string key)
    {
        try
        {
            var passwords = LoadPasswords();
            passwords.RemoveAll(p => (p.Key ?? p.Url) == key);
            
            var json = JsonSerializer.Serialize(passwords, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_passwordsPath, json);
        }
        catch { }
    }
    
    private void ClearPasswords()
    {
        try
        {
            if (File.Exists(_passwordsPath))
            {
                File.Delete(_passwordsPath);
            }
        }
        catch { }
    }
    
    // Default browser registration
    private void SetAsDefaultBrowser()
    {
        // Ensure registration is up to date (updates exe path, writes Capabilities + RegisteredApplications)
        // This is done via App so the logic is in one place
        App.OpenDefaultAppsForYCB();
    }
    
    private bool CheckIsDefaultBrowser()
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            if (key != null)
            {
                var progId = key.GetValue("ProgId")?.ToString();
                return progId == "YCBUrl";
            }
        }
        catch { }
        return false;
    }
    
    private void ApplySettingChange(string key, string value)
    {
        switch (key)
        {
            case "browser_theme":
                _isDarkMode = value != "light";
                _settings.DarkMode = _isDarkMode;
                ApplyTheme();
                SaveSettings();
                break;
                
            case "font_size":
                _settings.FontSize = value;
                SaveSettings();
                // Apply font size to all tabs
                var fontSize = value switch
                {
                    "small" => 0.85,
                    "large" => 1.15,
                    "larger" => 1.3,
                    _ => 1.0
                };
                foreach (var tab in _tabs)
                {
                    tab.WebView.ZoomFactor = fontSize * _zoomFactor;
                }
                break;
                
            case "incognito_ai_enabled":
                _settings.IncognitoAIEnabled = value == "true";
                SaveSettings();
                break;
                
            case "bookmarks_bar":
                _settings.BookmarksBarVisible = value == "on";
                SaveSettings();
                UpdateBookmarksBar();
                break;
                
            case "search_engine":
                _settings.SearchEngine = value;
                _searchEngine = value;
                UpdateUrlPlaceholder();
                SaveSettings();
                break;
                
            case "startup_mode":
                _settings.StartupMode = value;
                SaveSettings();
                break;
                
            case "ycb_model":
                _settings.YcbModel = value;
                SaveSettings();
                break;

            case "telemetry_enabled":
                _settings.TelemetryEnabled = value == "true";
                ErrorReporter.IsEnabled = _settings.TelemetryEnabled;
                SaveSettings();
                break;

            case "ad_blocker_enabled":
                _settings.AdBlockerEnabled = value == "on";
                SaveSettings();
                UpdateAdBlockButton();
                break;

            case "home_page":
                _settings.HomePage = value;
                SaveSettings();
                break;

        }
    }
    
    private void UpdateBookmarksBar()
    {
        if (_settings.BookmarksBarVisible)
        {
            BookmarksBar.Visibility = Visibility.Visible;
            BookmarksBarRow.Height = new GridLength(32);
            LoadBookmarksBar();
        }
        else
        {
            BookmarksBar.Visibility = Visibility.Collapsed;
            BookmarksBarRow.Height = new GridLength(0);
        }
    }
    
    private void LoadBookmarksBar()
    {
        BookmarksBarItems.Children.Clear();
        var bookmarks = LoadBookmarks();
        
        foreach (var bookmark in bookmarks.Take(20)) // Show up to 20 bookmarks
        {
            var btn = new Button
            {
                Content = new TextBlock 
                { 
                    Text = bookmark.Title,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 120
                },
                Tag = bookmark.Url,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e8eaed")!),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = bookmark.Url
            };
            btn.Click += (s, e) =>
            {
                if (s is Button b && b.Tag is string url)
                {
                    if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                    {
                        _tabs[_activeTabIndex].WebView.CoreWebView2.Navigate(url);
                    }
                }
            };
            BookmarksBarItems.Children.Add(btn);
        }
    }
    
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        base.OnClosing(e);
    }

    private static bool IsSearchPage(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLower();
            var path = uri.AbsolutePath.ToLower();
            var q    = uri.Query;
            return (host.Contains("google.")    && path.StartsWith("/search") && q.Contains("q=")) ||
                   (host.Contains("bing.com")   && path.StartsWith("/search")) ||
                   (host.Contains("duckduckgo.com") && q.Contains("q=")) ||
                   (host.Contains("search.yahoo.com")) ||
                   (host.Contains("ecosia.org") && path.StartsWith("/search"));
        }
        catch { return false; }
    }

}

// Data classes
public class BrowserTab
{
    public WebView2 WebView { get; set; } = null!;
    public Button TabButton { get; set; } = null!;
    public string Url { get; set; } = "";
    public string Title { get; set; } = "New Tab";
    public Image? TabFavicon { get; set; }
    public Button? TabCloseBtn { get; set; }
    public TextBlock? TabTitle { get; set; }
}

public class Settings
{
    public bool DarkMode { get; set; } = true;
    public string? HomePage { get; set; } = "ycb://newtab";
    public List<string>? LastTabs { get; set; }
    public bool? IncognitoAIEnabled { get; set; } = false;
    public bool BookmarksBarVisible { get; set; } = false;
    public string SearchEngine { get; set; } = "google";
    public string FontSize { get; set; } = "medium";
    public string StartupMode { get; set; } = "newtab";
    public string YcbModel { get; set; } = "gpt-5-mini";
    public bool HasSeenGuide { get; set; } = false;
    public bool TelemetryEnabled { get; set; } = true;
    // Window position/state persistence
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public string? WindowState { get; set; }
    public bool AdBlockerEnabled { get; set; } = true;
    public List<string>? AdBlockerDisabledSites { get; set; }
    public DateTime? HistoryClearedAt { get; set; }
}

public class HistoryItem
{
    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("visitedAt")]
    public DateTime Timestamp { get; set; }
}

public class DownloadItem
{
    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("filename")]
    public string Filename { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("savePath")]
    public string SavePath { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string Status { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("state")]
    public string State { get; set; } = "downloading";
    
    [System.Text.Json.Serialization.JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }
}

public class BookmarkItem
{
    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("label")]
    public string Label { get; set; } = "";
}

public class PasswordItem
{
    [System.Text.Json.Serialization.JsonPropertyName("key")]
    public string Key { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("username")]
    public string Username { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

public class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public class OmniSuggestion
{
    public string Primary { get; set; } = "";
    public string Secondary { get; set; } = "";
    public string NavigateUrl { get; set; } = "";
    public bool IsHistory { get; set; }

    // Search magnifier icon
    private const string SearchPath = "M10.5 10.5 L14 14 M9 15 C12.3137 15 15 12.3137 15 9 C15 5.68629 12.3137 3 9 3 C5.68629 3 3 5.68629 3 9 C3 12.3137 5.68629 15 9 15 Z";
    // Clock/history icon
    private const string HistoryPath = "M8 2 C4.686 2 2 4.686 2 8 C2 11.314 4.686 14 8 14 C11.314 14 14 11.314 14 8 C14 4.686 11.314 2 8 2 Z M8 5 L8 8.5 L11 10";

    public string IconPath => IsHistory ? HistoryPath : SearchPath;
    public string IconColor => IsHistory
        ? (IsDark ? "#8ab4f8" : "#1a73e8")
        : (IsDark ? "#9aa0a6" : "#5f6368");

    // Static theme flag — updated by ApplyTheme() before populating suggestions
    public static bool IsDark { get; set; } = true;
    public static string ThemePrimary { get; set; } = "#e8eaed";
    public static string ThemeSecondary { get; set; } = "#9aa0a6";

    public string PrimaryColor => ThemePrimary;
    public string SecondaryColor => ThemeSecondary;

    public System.Windows.Visibility SecondaryVisibility =>
        string.IsNullOrEmpty(Secondary) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
}
