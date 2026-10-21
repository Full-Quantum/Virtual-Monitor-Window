using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VirtualMonitorWindow;

internal sealed class MainForm : Form
{
    private static readonly HttpClient GitHubClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly Button _previewOnlyToggle = new()
    {
        AutoSize = true,
        Text = "Virtual Monitor Only Mode"
    };
    private TableLayoutPanel _layout = null!;
    private bool _previewOnly;
    private bool _vddSettingsWereVisible;
    private readonly ComboBox _displayPicker = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly PictureBox _preview = new()
    {
        BackColor = Color.FromArgb(18, 20, 25),
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        TabStop = true
    };
    private readonly NumericUpDown _displayCount = new()
    {
        Minimum = 0,
        Maximum = 16,
        Width = 64
    };
    private readonly CheckBox _interactive = new()
    {
        AutoSize = true,
        Text = "Control the virtual display with mouse and keyboard"
    };
    private readonly CheckBox _darkMode = new()
    {
        AutoSize = true,
        Text = "Dark mode"
    };
    private readonly Button _vddSettingsToggle = new()
    {
        AutoSize = true,
        Text = "Show virtual display settings"
    };
    private readonly FlowLayoutPanel _vddSettingsPanel = new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        Padding = new Padding(0, 0, 0, 6),
        Visible = false
    };
    private readonly Label _driverStatus = new()
    {
        AutoSize = true,
        Text = "Checking driver...",
        Padding = new Padding(8, 7, 8, 0)
    };
    private readonly Label _previewHint = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        Text = "Select a display. Enable control only for a virtual display."
    };
    private readonly Label _status = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Text = "Ready",
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly LinkLabel _latestVersionLink = new()
    {
        AutoSize = true,
        Text = "Latest: checking...",
        TextAlign = ContentAlignment.MiddleRight,
        Padding = new Padding(8, 0, 4, 0)
    };
    private readonly ToolTip _latestVersionToolTip = new();
    private readonly Button _installButton = new()
    {
        AutoSize = true,
        Text = "Install virtual display driver..."
    };
    private readonly Button _refreshButton = new()
    {
        AutoSize = true,
        Text = "Refresh displays"
    };
    private readonly Button _applyCountButton = new()
    {
        AutoSize = true,
        Text = "Apply monitor count"
    };
    private readonly System.Windows.Forms.Timer _captureTimer = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2000 };
    private readonly CancellationTokenSource _latestReleaseCheckCancellation = new();
    private readonly NativeMethods.LowLevelHookProc _mouseHookCallback;
    private readonly NativeMethods.LowLevelHookProc _keyboardHookCallback;

    private IReadOnlyList<DisplayTarget> _displays = Array.Empty<DisplayTarget>();
    private DisplayTarget? _selectedDisplay;
    private nint _mouseHook;
    private nint _keyboardHook;
    private bool _driverAvailable;
    private bool _keyboardInputActive;
    private bool _mouseButtonHeld;
    private bool _refreshingStatus;
    private bool _changingCount;
    private bool _checkingLatestRelease;
    private string? _latestReleaseUrl;
    private string _lastCaptureError = "";

    public MainForm()
    {
        Text = "Virtual Monitor Window 1.0.7";
        MinimumSize = new Size(640, 480);
        Size = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;
        _mouseHookCallback = MouseHookCallback;
        _keyboardHookCallback = KeyboardHookCallback;
        BuildLayout();

        _captureTimer.Tick += CaptureTimerTick;
        _refreshTimer.Tick += RefreshTimerTick;
        _displayPicker.SelectedIndexChanged += DisplayPickerChanged;
        _refreshButton.Click += (_, _) => RefreshDisplays();
        _applyCountButton.Click += ApplyCountClicked;
        _installButton.Click += InstallClicked;
        _vddSettingsToggle.Click += (_, _) => ToggleVddSettings();
        _previewOnlyToggle.Click += (_, _) => SetPreviewOnly(!_previewOnly);
        _interactive.CheckedChanged += InteractiveCheckedChanged;
        _darkMode.CheckedChanged += (_, _) => ApplyTheme(_darkMode.Checked);
        _latestVersionLink.LinkClicked += LatestVersionLinkClicked;
        Shown += MainFormShown;
        FormClosed += MainFormClosed;
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 8,
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        _layout = layout;

        var heading = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
            Text = "Virtual Monitor Window 1.0.7",
            Padding = new Padding(0, 0, 0, 8)
        };
        layout.Controls.Add(heading, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 6)
        };
        toolbar.Controls.Add(_vddSettingsToggle);
        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(_darkMode);
        toolbar.Controls.Add(_previewOnlyToggle);
        layout.Controls.Add(toolbar, 0, 1);

        _vddSettingsPanel.Controls.Add(_installButton);
        _vddSettingsPanel.Controls.Add(_driverStatus);
        _vddSettingsPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Virtual monitors:",
            Padding = new Padding(12, 7, 2, 0)
        });
        _vddSettingsPanel.Controls.Add(_displayCount);
        _vddSettingsPanel.Controls.Add(_applyCountButton);
        _vddSettingsPanel.Controls.Add(_interactive);
        layout.Controls.Add(_vddSettingsPanel, 0, 2);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            Text = "Virtual Monitor",
            Padding = new Padding(0, 0, 0, 4)
        }, 0, 3);
        var displayRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        displayRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        displayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        displayRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        displayRow.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Display:",
            Margin = new Padding(0),
            Padding = new Padding(0, 5, 8, 0)
        }, 0, 0);
        _displayPicker.Margin = new Padding(0);
        _displayPicker.Dock = DockStyle.Fill;
        displayRow.Controls.Add(_displayPicker, 1, 0);
        layout.Controls.Add(displayRow, 0, 4);
        _preview.Margin = new Padding(0);
        layout.Controls.Add(_preview, 0, 5);
        var previewFooter = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
        };
        previewFooter.Controls.Add(_previewHint);
        layout.Controls.Add(previewFooter, 0, 6);

        var statusPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 26,
            Padding = new Padding(2, 4, 0, 0)
        };
        statusPanel.Controls.Add(_status);
        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        footer.Controls.Add(_latestVersionLink);
        footer.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "by Full_Quantum | v1.0.7",
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 2, 0)
        });
        statusPanel.Controls.Add(footer);
        layout.Controls.Add(statusPanel, 0, 7);
    }

    private async void LatestVersionLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (_latestReleaseUrl is null)
        {
            await CheckLatestReleaseAsync();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_latestReleaseUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Win32Exception error)
        {
            _status.Text = $"Could not open GitHub releases: {error.Message}";
        }
    }

    private async Task CheckLatestReleaseAsync()
    {
        if (_checkingLatestRelease || IsDisposed)
            return;
        _checkingLatestRelease = true;
        _latestVersionLink.Text = "Latest: checking...";
        _latestReleaseUrl = null;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://api.github.com/repos/Full-Quantum/Virtual-Monitor-Window/releases/latest");
            request.Headers.UserAgent.ParseAdd("VirtualMonitorWindow");
            using var response = await GitHubClient.SendAsync(
                request,
                _latestReleaseCheckCancellation.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(
                _latestReleaseCheckCancellation.Token);
            using var release = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: _latestReleaseCheckCancellation.Token);
            if (IsDisposed)
                return;
            var root = release.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("html_url", out var urlElement) ||
                urlElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("GitHub returned incomplete release information.");
            }

            var tag = tagElement.GetString();
            var releaseUrl = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith('v') ||
                !Version.TryParse(tag[1..], out var latestVersion) ||
                string.IsNullOrWhiteSpace(releaseUrl) ||
                !Uri.TryCreate(releaseUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                uri.Host != "github.com")
            {
                throw new InvalidDataException("GitHub returned invalid release information.");
            }

            var currentVersion = typeof(MainForm).Assembly.GetName().Version ?? new Version(0, 0);
            _latestReleaseUrl = uri.AbsoluteUri;
            _latestVersionLink.Text = latestVersion > currentVersion
                ? $"Update available: {tag}"
                : $"Latest: {tag}";
            _latestVersionToolTip.SetToolTip(_latestVersionLink, "Open the latest GitHub release.");
            _latestVersionLink.LinkArea = new LinkArea(0, _latestVersionLink.Text.Length);
        }
        catch (Exception error) when (
            error is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException)
        {
            if (IsDisposed)
                return;
            _latestVersionLink.Text = "Latest version unavailable (click to retry)";
            _latestVersionLink.LinkArea = new LinkArea(0, _latestVersionLink.Text.Length);
            _latestVersionToolTip.SetToolTip(_latestVersionLink, error.Message);
        }
        finally
        {
            _checkingLatestRelease = false;
        }
    }

    private void ToggleVddSettings()
    {
        _vddSettingsPanel.Visible = !_vddSettingsPanel.Visible;
        _vddSettingsToggle.Text = _vddSettingsPanel.Visible
            ? "Hide virtual display settings"
            : "Show virtual display settings";
    }

    private void SetPreviewOnly(bool enabled)
    {
        if (enabled)
            _vddSettingsWereVisible = _vddSettingsPanel.Visible;
        _previewOnly = enabled;
        SuspendLayout();
        _layout.SuspendLayout();
        foreach (Control control in _layout.Controls)
        {
            if (control != _preview)
                control.Visible = !enabled;
        }

        if (!enabled)
            _vddSettingsPanel.Visible = _vddSettingsWereVisible;
        _layout.Padding = enabled ? Padding.Empty : new Padding(8);
        _preview.BorderStyle = enabled ? BorderStyle.None : BorderStyle.FixedSingle;
        if (enabled)
        {
            _previewOnlyToggle.Text = "Return to Main View";
            _previewOnlyToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _preview.Controls.Add(_previewOnlyToggle);
            _previewOnlyToggle.Location = new Point(_preview.ClientSize.Width - _previewOnlyToggle.Width, 0);
            _previewOnlyToggle.BringToFront();
        }
        else
        {
            _preview.Controls.Remove(_previewOnlyToggle);
            _previewOnlyToggle.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _previewOnlyToggle.Text = "Virtual Monitor Only Mode";
            var toolbar = (FlowLayoutPanel)_layout.GetControlFromPosition(0, 1)!;
            toolbar.Controls.Add(_previewOnlyToggle);
        }

        _layout.ResumeLayout(true);
        ResumeLayout(true);
        ApplyTheme(_darkMode.Checked);
    }

    private void StyleReturnButton()
    {
        if (!_previewOnly)
            return;
        _previewOnlyToggle.FlatStyle = FlatStyle.Flat;
        _previewOnlyToggle.UseVisualStyleBackColor = false;
        _previewOnlyToggle.BackColor = Color.FromArgb(0, 95, 184);
        _previewOnlyToggle.ForeColor = Color.White;
        _previewOnlyToggle.FlatAppearance.BorderColor = Color.White;
    }

    private void ApplyTheme(bool darkMode)
    {
        var background = darkMode
            ? Color.FromArgb(32, 33, 36)
            : SystemColors.Control;
        var surface = darkMode
            ? Color.FromArgb(43, 45, 49)
            : SystemColors.Control;
        var raisedSurface = darkMode
            ? Color.FromArgb(53, 55, 60)
            : SystemColors.Control;
        var foreground = darkMode
            ? Color.FromArgb(232, 234, 237)
            : SystemColors.ControlText;

        BackColor = background;
        ForeColor = foreground;
        ApplyThemeToChildren(this, darkMode, surface, raisedSurface, foreground);
        StyleReturnButton();

        _preview.BackColor = Color.FromArgb(18, 20, 25);
        _latestVersionLink.LinkColor = darkMode ? Color.FromArgb(138, 180, 248) : SystemColors.HotTrack;
        _latestVersionLink.ActiveLinkColor = darkMode ? Color.FromArgb(190, 215, 255) : Color.Red;
        _latestVersionLink.VisitedLinkColor = _latestVersionLink.LinkColor;
        _latestVersionLink.DisabledLinkColor = darkMode ? Color.FromArgb(170, 176, 184) : Color.FromArgb(133, 133, 133);
        _previewHint.ForeColor = darkMode
            ? Color.FromArgb(170, 176, 184)
            : Color.DimGray;
        _driverStatus.ForeColor = _driverAvailable
            ? (darkMode ? Color.FromArgb(129, 201, 149) : Color.DarkGreen)
            : (darkMode ? Color.FromArgb(242, 139, 130) : Color.DarkRed);
    }

    private void ApplyThemeToChildren(
        Control parent,
        bool darkMode,
        Color surface,
        Color raisedSurface,
        Color foreground)
    {
        foreach (Control control in parent.Controls)
        {
            control.ForeColor = foreground;
            if (control is Label)
            {
                control.BackColor = Color.Transparent;
            }
            else if (control is Button button)
            {
                button.BackColor = raisedSurface;
                button.FlatStyle = darkMode ? FlatStyle.Flat : FlatStyle.Standard;
                button.UseVisualStyleBackColor = !darkMode;
                if (darkMode)
                    button.FlatAppearance.BorderColor = Color.FromArgb(95, 99, 104);
            }
            else if (control is CheckBox checkBox)
            {
                checkBox.BackColor = parent.BackColor;
                checkBox.UseVisualStyleBackColor = !darkMode;
            }
            else if (control is ComboBox or NumericUpDown)
            {
                control.BackColor = darkMode ? surface : SystemColors.Window;
            }
            else if (control is TableLayoutPanel or FlowLayoutPanel or Panel or SplitterPanel or SplitContainer)
            {
                control.BackColor = darkMode ? surface : SystemColors.Control;
            }

            ApplyThemeToChildren(control, darkMode, surface, raisedSurface, foreground);
        }
    }

    private async void MainFormShown(object? sender, EventArgs e)
    {
        _ = CheckLatestReleaseAsync();
        RefreshDisplays();
        await RefreshDriverStatusAsync();
        _captureTimer.Start();
        _refreshTimer.Start();
    }

    private async void RefreshTimerTick(object? sender, EventArgs e)
    {
        RefreshDisplays();
        await RefreshDriverStatusAsync();
    }

    private void RefreshDisplays()
    {
        var previousName = _selectedDisplay?.DeviceName;
        _displays = VirtualMonitorService.EnumerateDisplays();
        _displayPicker.BeginUpdate();
        _displayPicker.Items.Clear();
        foreach (var display in _displays)
            _displayPicker.Items.Add(display);
        var selection = _displays
            .Select((display, index) => (display, index))
            .FirstOrDefault(pair => pair.display.DeviceName.Equals(
                previousName,
                StringComparison.OrdinalIgnoreCase));
        var selectedIndex = previousName is null
            ? -1
            : selection.display is null ? -1 : selection.index;
        if (selectedIndex < 0 && _displays.Count > 0)
            selectedIndex = 0;
        _displayPicker.SelectedIndex = selectedIndex;
        _displayPicker.EndUpdate();

        var virtualCount = _displays.Count(display => display.IsVirtual);
        if (_driverAvailable && !_changingCount)
            _displayCount.Value = Math.Min(_displayCount.Maximum, virtualCount);
        _installButton.Enabled = !_driverAvailable;
        _applyCountButton.Enabled = _driverAvailable;
        _displayCount.Enabled = _driverAvailable;
    }

    private void DisplayPickerChanged(object? sender, EventArgs e)
    {
        _selectedDisplay = _displayPicker.SelectedItem as DisplayTarget;
        _lastCaptureError = "";
        UpdatePreviewHint();
        if (_interactive.Checked && _selectedDisplay?.IsVirtual != true)
            _interactive.Checked = false;
        CaptureFrame();
    }

    private async void ApplyCountClicked(object? sender, EventArgs e)
    {
        if (!_driverAvailable)
        {
            MessageBox.Show(
                this,
                "Install and start the virtual display driver before changing its monitor count.",
                "Driver not available",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _applyCountButton.Enabled = false;
        _changingCount = true;
        try
        {
            var count = (int)_displayCount.Value;
            await VirtualMonitorService.ApplyDisplayCountAsync(count);
            _status.Text = $"Requested {count} virtual monitor(s). Waiting for Windows to update the display list...";
            await Task.Delay(4000);
            RefreshDisplays();
            await RefreshDriverStatusAsync();
            _status.Text = $"Applied virtual monitor count: {count}.";
        }
        catch (Exception error)
        {
            _status.Text = $"Could not change monitor count: {error.Message}";
            MessageBox.Show(this, error.Message, "Monitor count failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _changingCount = false;
            _applyCountButton.Enabled = _driverAvailable;
        }
    }

    private async void InstallClicked(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "This installs the official signed Virtual Display Driver for Windows x64." +
            Environment.NewLine + Environment.NewLine +
            "Windows will ask for administrator approval. The driver is a system-wide display component; it can change display arrangement and may cause a black screen in some driver-update situations. " +
            "Before major graphics or chipset driver updates, remove VDD using the vendor's Virtual Driver Control tool." +
            Environment.NewLine + Environment.NewLine +
            "Continue to the Windows elevation prompt?",
            "Install virtual display driver",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
            return;

        _installButton.Enabled = false;
        _status.Text = "Waiting for Windows administrator approval...";
        try
        {
            var result = await VirtualMonitorService.InstallWithUacAsync();
            if (result.Success)
            {
                MessageBox.Show(this, result.Message, "Driver installation", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _status.Text = "Driver installation completed. Refreshing displays...";
                await Task.Delay(2000);
                RefreshDisplays();
                await RefreshDriverStatusAsync();
            }
            else
            {
                _status.Text = result.Message.Split(Environment.NewLine)[0];
                MessageBox.Show(this, result.Message, "Driver installation", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _installButton.Enabled = !_driverAvailable;
        }
    }

    private async Task RefreshDriverStatusAsync()
    {
        if (_refreshingStatus || IsDisposed)
            return;
        _refreshingStatus = true;
        try
        {
            _driverAvailable = await VirtualMonitorService.IsDriverAvailableAsync();
            _driverStatus.Text = _driverAvailable
                ? "VDD driver running"
                : "VDD driver not running";
            _driverStatus.ForeColor = _driverAvailable
                ? (_darkMode.Checked ? Color.FromArgb(129, 201, 149) : Color.DarkGreen)
                : (_darkMode.Checked ? Color.FromArgb(242, 139, 130) : Color.DarkRed);
            _installButton.Enabled = !_driverAvailable;
            _applyCountButton.Enabled = _driverAvailable;
            _displayCount.Enabled = _driverAvailable;
            if (!_driverAvailable && _interactive.Checked)
                _interactive.Checked = false;
        }
        finally
        {
            _refreshingStatus = false;
        }
    }

    private void InteractiveCheckedChanged(object? sender, EventArgs e)
    {
        if (_interactive.Checked && !_driverAvailable)
        {
            _interactive.Checked = false;
            MessageBox.Show(
                this,
                "The virtual display driver is not running.",
                "Control unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        if (_interactive.Checked && _selectedDisplay?.IsVirtual != true)
        {
            _interactive.Checked = false;
            MessageBox.Show(
                this,
                "Select a display identified as a Virtual Display Driver screen before enabling mouse and keyboard control.",
                "Select a virtual display",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (_interactive.Checked)
        {
            try
            {
                InstallInputHooks();
                _status.Text = "Control is enabled only while the pointer is over the Virtual Monitor. Press Escape to stop.";
            }
            catch (Win32Exception error)
            {
                _interactive.Checked = false;
                _status.Text = $"Could not enable input control: {error.Message}";
                MessageBox.Show(this, error.Message, "Input control unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else
        {
            RemoveInputHooks();
            _keyboardInputActive = false;
            _mouseButtonHeld = false;
            _status.Text = "Control is off. The Virtual Monitor is view-only.";
        }
        UpdatePreviewHint();
    }

    private void UpdatePreviewHint()
    {
        if (_selectedDisplay is null)
        {
            _previewHint.Text = "No active display is available.";
        }
        else if (!_selectedDisplay.IsVirtual)
        {
            _previewHint.Text = $"Viewing {_selectedDisplay.AdapterName} ({_selectedDisplay.MonitorName}). Select a VDD display to enable control.";
        }
        else if (_interactive.Checked)
        {
            _previewHint.Text = "Control is on. Mouse and keyboard input over the image goes to the virtual display. Press Escape to stop.";
        }
        else
        {
            _previewHint.Text = "Virtual Monitor is view-only. Enable control above when you want to interact.";
        }
    }

    private void CaptureTimerTick(object? sender, EventArgs e) => CaptureFrame();

    private void CaptureFrame()
    {
        if (_selectedDisplay is null || !Visible || WindowState == FormWindowState.Minimized)
            return;

        try
        {
            var image = VirtualMonitorService.CaptureDisplay(_selectedDisplay);
            var previous = _preview.Image;
            _preview.Image = image;
            previous?.Dispose();
            if (_lastCaptureError.Length > 0)
            {
                _lastCaptureError = "";
                _status.Text = $"Showing {_selectedDisplay.DeviceName}.";
            }
        }
        catch (Exception error) when (error is Win32Exception or ArgumentException or ExternalException)
        {
            var message = error.Message;
            if (!message.Equals(_lastCaptureError, StringComparison.Ordinal))
            {
                _lastCaptureError = message;
                _status.Text = $"Display capture failed: {message}";
            }
        }
    }

    private void InstallInputHooks()
    {
        if (_mouseHook != 0 || _keyboardHook != 0)
            RemoveInputHooks();
        var module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLowLevel,
            _mouseHookCallback,
            module,
            0);
        if (_mouseHook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLowLevel,
            _keyboardHookCallback,
            module,
            0);
        if (_keyboardHook == 0)
        {
            var error = Marshal.GetLastWin32Error();
            RemoveInputHooks();
            throw new Win32Exception(error);
        }
    }

    private void RemoveInputHooks()
    {
        if (_keyboardHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
        if (_mouseHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
    }

    private nint MouseHookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0 || !_interactive.Checked)
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);

        try
        {
            var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(lParam);
            if ((data.Flags & NativeMethods.InjectedFlag) != 0)
                return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);

            var point = new Point(data.Point.X, data.Point.Y);
            var viewport = GetPreviewViewport();
            var inside = viewport.Width > 0 && viewport.Height > 0 && viewport.Contains(point);
            var message = unchecked((int)wParam);
            var isDown = IsMouseDown(message);
            var isUp = IsMouseUp(message);

            if (isDown && inside)
            {
                _mouseButtonHeld = true;
                _keyboardInputActive = true;
                BeginInvoke((Action)(() => _preview.Focus()));
            }
            else if (isUp && !_mouseButtonHeld)
            {
                return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
            }
            else if (!inside && !_mouseButtonHeld)
            {
                if (isDown)
                    _keyboardInputActive = false;
                return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
            }

            var targetPoint = MapToDisplay(point, viewport, clamp: _mouseButtonHeld);
            if (targetPoint is null)
                return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);

            var flags = MouseFlagsForMessage(message);
            var mouseData = data.MouseData;
            if (message == NativeMethods.WmMouseWheel)
                flags |= NativeMethods.Wheel;
            else if (message == NativeMethods.WmMouseHWheel)
                flags |= NativeMethods.HorizontalWheel;

            SendMouseInput(targetPoint.Value, flags | NativeMethods.Absolute | NativeMethods.VirtualDesk, mouseData);

            if (isUp)
            {
                _mouseButtonHeld = false;
                if (!inside)
                    _keyboardInputActive = false;
            }

            return 1;
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                BeginInvoke((Action)(() => _status.Text = $"Input forwarding failed: {error.Message}"));
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }
    }

    private nint KeyboardHookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0 || !_interactive.Checked || !_keyboardInputActive)
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);

        try
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
            if ((data.Flags & NativeMethods.InjectedFlag) != 0)
                return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);

            var message = unchecked((int)wParam);
            var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            var isUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
            if (!isDown && !isUp)
                return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);

            if (isDown && data.VirtualKey == (uint)Keys.Escape)
            {
                BeginInvoke((Action)(() => _interactive.Checked = false));
                return 1;
            }

            uint flags = isUp ? NativeMethods.KeyUp : 0;
            if ((data.Flags & 0x01) != 0)
                flags |= 0x01;
            var input = new NativeMethods.Input
            {
                Type = 1,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeyboardInput
                    {
                        VirtualKey = (ushort)data.VirtualKey,
                        ScanCode = (ushort)data.ScanCode,
                        Flags = flags
                    }
                }
            };
            SendInput(input);
            return 1;
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                BeginInvoke((Action)(() => _status.Text = $"Keyboard forwarding failed: {error.Message}"));
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        }
    }

    private RectangleF GetPreviewViewport()
    {
        if (_preview.Image is null || _preview.ClientSize.Width == 0 || _preview.ClientSize.Height == 0)
            return RectangleF.Empty;
        var client = _preview.RectangleToScreen(_preview.ClientRectangle);
        var imageAspect = (float)_preview.Image.Width / _preview.Image.Height;
        var controlAspect = (float)client.Width / client.Height;
        if (controlAspect > imageAspect)
        {
            var width = client.Height * imageAspect;
            return new RectangleF(client.Left + (client.Width - width) / 2f, client.Top, width, client.Height);
        }
        var height = client.Width / imageAspect;
        return new RectangleF(client.Left, client.Top + (client.Height - height) / 2f, client.Width, height);
    }

    private Point? MapToDisplay(Point point, RectangleF viewport, bool clamp)
    {
        if (_selectedDisplay is null || viewport.Width <= 0 || viewport.Height <= 0)
            return null;
        if (!clamp && !viewport.Contains(point))
            return null;
        var xRatio = Math.Clamp((point.X - viewport.Left) / viewport.Width, 0f, 1f);
        var yRatio = Math.Clamp((point.Y - viewport.Top) / viewport.Height, 0f, 1f);
        var x = _selectedDisplay.Bounds.Left + (int)Math.Round(xRatio * (_selectedDisplay.Bounds.Width - 1));
        var y = _selectedDisplay.Bounds.Top + (int)Math.Round(yRatio * (_selectedDisplay.Bounds.Height - 1));
        return new Point(x, y);
    }

    private static bool IsMouseDown(int message) =>
        message is NativeMethods.WmLeftButtonDown or NativeMethods.WmRightButtonDown or
            NativeMethods.WmMiddleButtonDown or NativeMethods.WmXButtonDown;

    private static bool IsMouseUp(int message) =>
        message is NativeMethods.WmLeftButtonUp or NativeMethods.WmRightButtonUp or
            NativeMethods.WmMiddleButtonUp or NativeMethods.WmXButtonUp;

    private static uint MouseFlagsForMessage(int message) => message switch
    {
        NativeMethods.WmMouseMove => NativeMethods.MouseMove,
        NativeMethods.WmLeftButtonDown => NativeMethods.LeftDown,
        NativeMethods.WmLeftButtonUp => NativeMethods.LeftUp,
        NativeMethods.WmRightButtonDown => NativeMethods.RightDown,
        NativeMethods.WmRightButtonUp => NativeMethods.RightUp,
        NativeMethods.WmMiddleButtonDown => NativeMethods.MiddleDown,
        NativeMethods.WmMiddleButtonUp => NativeMethods.MiddleUp,
        NativeMethods.WmXButtonDown => NativeMethods.XDown,
        NativeMethods.WmXButtonUp => NativeMethods.XUp,
        NativeMethods.WmMouseWheel => 0,
        NativeMethods.WmMouseHWheel => 0,
        _ => 0
    };

    private static void SendMouseInput(Point point, uint flags, uint mouseData)
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        if (width <= 1 || height <= 1)
            throw new InvalidOperationException("Windows did not report a valid desktop coordinate space.");
        var normalizedX = (int)Math.Round((point.X - left) * 65535d / (width - 1));
        var normalizedY = (int)Math.Round((point.Y - top) * 65535d / (height - 1));
        var input = new NativeMethods.Input
        {
            Type = 0,
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MouseInput
                {
                    X = normalizedX,
                    Y = normalizedY,
                    MouseData = mouseData,
                    Flags = flags
                }
            }
        };
        SendInput(input);
    }

    private static void SendInput(NativeMethods.Input input)
    {
        var sent = NativeMethods.SendInput(
            1,
            [input],
            Marshal.SizeOf<NativeMethods.Input>());
        if (sent != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void MainFormClosed(object? sender, FormClosedEventArgs e)
    {
        _latestReleaseCheckCancellation.Cancel();
        _latestReleaseCheckCancellation.Dispose();
        _captureTimer.Stop();
        _refreshTimer.Stop();
        RemoveInputHooks();
        _preview.Image?.Dispose();
        _preview.Image = null;
        _captureTimer.Dispose();
        _refreshTimer.Dispose();
    }
}
