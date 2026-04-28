using H.NotifyIcon;
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Tray.Rendering;
using System.ComponentModel;
using System.Drawing;

namespace PerfMonitor.Tray;

public sealed class TrayIconHost : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly TrayIconRenderer _renderer = new();
    private readonly TaskbarIcon _cpuIcon = new();
    private readonly TaskbarIcon _ramIcon = new();
    private readonly TaskbarIcon _gpuIcon = new();
    private readonly TaskbarIcon _cpuTempIcon = new();

    private int _lastCpu = -1, _lastRam = -1, _lastGpu = -1, _lastCpuTemp = -1;

    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ToggleVisibilityRequested;
    /// <summary>Raised when the user clicks the "Install update" menu item. String is the URL to open.</summary>
    public event EventHandler<string>? UpdateClicked;

    // Held by the menu builder so SetUpdateAvailable can flip visibility
    // without rebuilding the entire ContextMenu.
    private System.Windows.Controls.MenuItem? _updateMenuItem;
    private System.Windows.Controls.Separator? _updateSeparator;

    public TrayIconHost(MainViewModel vm)
    {
        _vm = vm;
        _cpuIcon.ToolTipText = "CPU load";
        _ramIcon.ToolTipText = "RAM used %";
        _gpuIcon.ToolTipText = "GPU load";
        _cpuTempIcon.ToolTipText = "CPU temperature";
        var menu = BuildMenu();
        _cpuIcon.ContextMenu = menu;
        _ramIcon.ContextMenu = menu;
        _gpuIcon.ContextMenu = menu;
        _cpuTempIcon.ContextMenu = menu;
        _vm.PropertyChanged += OnVmChanged;
        UpdateAll();
    }

    private System.Windows.Controls.ContextMenu BuildMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        // Update slot at the top — collapsed by default, becomes visible
        // when SetUpdateAvailable is called. Tag holds the URL to open.
        _updateMenuItem = new System.Windows.Controls.MenuItem
        {
            Header = "Install update…",
            Visibility = System.Windows.Visibility.Collapsed
        };
        _updateMenuItem.Click += (_, _) =>
        {
            if (_updateMenuItem?.Tag is string url)
                UpdateClicked?.Invoke(this, url);
        };
        _updateSeparator = new System.Windows.Controls.Separator
        {
            Visibility = System.Windows.Visibility.Collapsed
        };

        var toggle = new System.Windows.Controls.MenuItem { Header = "Show / Hide widget" };
        toggle.Click += (_, _) => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
        var settings = new System.Windows.Controls.MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var exit = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(_updateMenuItem);
        menu.Items.Add(_updateSeparator);
        menu.Items.Add(toggle);
        menu.Items.Add(settings);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exit);
        return menu;
    }

    /// <summary>
    /// Surfaces an "Install update X.Y.Z…" item at the top of the tray
    /// context menu. Idempotent — calling repeatedly just refreshes
    /// the version label and URL. Has no effect if the menu wasn't
    /// built yet (which shouldn't happen since the ctor builds it).
    /// </summary>
    public void SetUpdateAvailable(string version, string url)
    {
        var app = System.Windows.Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            if (_updateMenuItem is null || _updateSeparator is null) return;
            _updateMenuItem.Header = $"Install update {version}…";
            _updateMenuItem.Tag = url;
            _updateMenuItem.Visibility = System.Windows.Visibility.Visible;
            _updateSeparator.Visibility = System.Windows.Visibility.Visible;
        });
    }

    public void Show()
    {
        _cpuIcon.ForceCreate();
        _ramIcon.ForceCreate();
        _gpuIcon.ForceCreate();
        if (_vm.CpuTempAvailable) _cpuTempIcon.ForceCreate();
    }

    /// <summary>
    /// Shows a Windows system toast/balloon anchored to the CPU tray icon.
    /// Falls silent if the shell rejects the notification (no icon yet, etc.).
    /// </summary>
    public void ShowNotification(string title, string message)
    {
        try
        {
            _cpuIcon.ShowNotification(
                title: title,
                message: message,
                icon: H.NotifyIcon.Core.NotificationIcon.Info);
        }
        catch { /* best-effort — never let UX noise crash the app */ }
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        var app = System.Windows.Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) return;
        // BeginInvoke is fire-and-forget — avoids blocking a background thread
        // if the UI dispatcher is mid-shutdown.
        app.Dispatcher.BeginInvoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.CpuLoadPercent):
                    UpdateIfChanged(ref _lastCpu, (int)_vm.CpuLoadPercent,
                        v => SetIcon(_cpuIcon, v, Color.FromArgb(255, 255, 138, 90)),
                        v => _cpuIcon.ToolTipText = $"CPU {v}%");
                    break;
                case nameof(MainViewModel.RamUsedGb):
                case nameof(MainViewModel.RamTotalGb):
                    var ram = _vm.RamTotalGb > 0 ? (int)(_vm.RamUsedGb / _vm.RamTotalGb * 100) : 0;
                    UpdateIfChanged(ref _lastRam, ram,
                        v => SetIcon(_ramIcon, v, Color.FromArgb(255, 90, 208, 255)),
                        v => _ramIcon.ToolTipText = $"RAM {v}% ({_vm.RamUsedGb:F1} / {_vm.RamTotalGb:F0} GB)");
                    break;
                case nameof(MainViewModel.GpuLoadPercent):
                    UpdateIfChanged(ref _lastGpu, (int)_vm.GpuLoadPercent,
                        v => SetIcon(_gpuIcon, v, Color.FromArgb(255, 165, 138, 255)),
                        v => _gpuIcon.ToolTipText = $"GPU {v}%");
                    break;
                case nameof(MainViewModel.CpuTempC):
                    if (!_vm.CpuTempAvailable) return;
                    UpdateIfChanged(ref _lastCpuTemp, (int)_vm.CpuTempC,
                        v => SetIcon(_cpuTempIcon, v, Color.FromArgb(255, 255, 77, 109)),
                        v => _cpuTempIcon.ToolTipText = $"CPU {v}°C");
                    break;
            }
        });
    }

    private static void UpdateIfChanged(ref int last, int current,
        Action<int> apply, Action<int> tooltip)
    {
        if (last == current) return;
        last = current;
        apply(current);
        tooltip(current);
    }

    private void SetIcon(TaskbarIcon icon, int value, Color accent)
    {
        using var bmp = _renderer.RenderLoadBitmap(value, accent);
        var previous = icon.Icon;
        icon.Icon = _renderer.ToIcon(bmp);
        previous?.Dispose();
    }

    private void UpdateAll()
    {
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.CpuLoadPercent)));
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.RamUsedGb)));
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.GpuLoadPercent)));
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.CpuTempC)));
    }

    public void Dispose()
    {
        _cpuIcon.Dispose();
        _ramIcon.Dispose();
        _gpuIcon.Dispose();
        _cpuTempIcon.Dispose();
        _renderer.Dispose();
    }
}
