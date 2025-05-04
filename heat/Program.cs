#nullable enable

using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.Versioning;
using System.Text.Json;
using System.IO;
using System.Collections.Generic;
using WinFormsTimer = System.Windows.Forms.Timer;
using System.Threading.Tasks;
using System.Text;

namespace MinecraftAutoMiner
{
    [SupportedOSPlatform("windows")]
    public class MinecraftAutoMinerForm : Form
    {
        private const string SETTINGS_FILE = "settings.json";
        private const int MINIMUM_OPERATION_DELAY = 5000; // 5 seconds minimum between operations

        private class Settings
        {
            public int FixTimeValue { get; set; } = 5;
            public string FixTimeUnit { get; set; } = "Minutes";
        }

        [DllImport("user32.dll", EntryPoint = "RegisterHotKey", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Win32RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Win32UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", EntryPoint = "mouse_event", CharSet = CharSet.Auto)]
        private static extern void Win32MouseEvent(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll", EntryPoint = "keybd_event", CharSet = CharSet.Auto)]
        private static extern void Win32KeybdEvent(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private const uint MOUSE_LEFT_BUTTON_DOWN = 0x02;
        private const uint MOUSE_LEFT_BUTTON_UP = 0x04;
        private const int WM_HOTKEY_MESSAGE = 0x0312;
        private const uint HOTKEY_NO_MODIFIER = 0x0000;
        private const uint VIRTUAL_KEY_F1 = 0x70;  // F1 key
        private const uint VIRTUAL_KEY_F3 = 0x72;  // F3 key
        private const byte VK_T = 0x54;  // 'T' key
        private const byte VK_ENTER = 0x0D;  // Enter key
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_MENU = 0x12;  // Alt key
        private const byte VK_TAB = 0x09;   // Tab key
        private const byte VK_ESCAPE = 0x1B; // Escape key
        private bool _isAltPressed = false;
        private bool _isTabPressed = false;
        private bool _isEscapePressed = false;

        private readonly WinFormsTimer _miningTimer;
        private readonly WinFormsTimer _fixTimer;
        private readonly WinFormsTimer _monitorTimer;
        private readonly WinFormsTimer _windowCheckTimer;
        private readonly WinFormsTimer _driftCheckTimer;
        private readonly Button _mainButton;
        private readonly NumericUpDown _fixTimeValueInput;
        private readonly ComboBox _fixTimeUnitComboBox;
        private readonly Label _fixTotalTimeLabel;
        private bool _isMiningActive;
        private bool _isFixingActive;
        private readonly int _miningHotkeyId = 1;
        private bool _isMouseButtonDown = false;
        private bool _isOperationInProgress = false;
        private DateTime _lastOperationTime = DateTime.MinValue;
        private readonly Queue<Action> _operationQueue = new Queue<Action>();
        private readonly object _operationLock = new object();
        private bool _isFixOperationInProgress = false;
        private DateTime _lastFixOperationTime = DateTime.MinValue;
        private int _originalFixInterval;
        private DateTime _nextFixTime = DateTime.MinValue;
        private DateTime _lastFixCheck = DateTime.MinValue;
        private int _fixIntervalCount = 0;
        private DateTime _fixStartTime = DateTime.MinValue;
        private double _fixTotalDrift = 0;
        private DateTime _lastFixOperation = DateTime.MinValue;
        private readonly object _fixLock = new object();
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private bool _isClosing = false;
        private const string MINECRAFT_WINDOW_TITLE = "Minecraft";
        private bool _isMinecraftRunning = false;
        private bool _isFixTimerRunning = false;
        private readonly WinFormsTimer _sellTimer = new WinFormsTimer { Interval = 10000 };
        private bool _isSellingActive = false;
        private int _originalSellInterval = 10000;
        private DateTime _nextSellTime = DateTime.MinValue;
        private readonly int _sellingHotkeyId = 2;
        private NumericUpDown _sellTimeValueInput;
        private ComboBox _sellTimeUnitComboBox;
        private Label _sellTotalTimeLabel;
        private Button _sellButton;

        public MinecraftAutoMinerForm()
        {
            _isMiningActive = false;
            _isFixingActive = false;

            // Initialize system tray icon and menu
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Show Window", null, ShowWindow);
            _trayMenu.Items.Add("-");
            _trayMenu.Items.Add("Start Mining", null, (s, e) => ToggleMining(this, EventArgs.Empty));
            _trayMenu.Items.Add("-");
            _trayMenu.Items.Add("Exit", null, ExitApplication);

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                ContextMenuStrip = _trayMenu,
                Text = "HeatTheGoat",
                Visible = true
            };
            _trayIcon.DoubleClick += ShowWindow;

            _miningTimer = new WinFormsTimer { Interval = 100 };
            _miningTimer.Tick += MiningTick;

            // Initialize fix timer with 15 seconds (15000ms)
            _fixTimer = new WinFormsTimer { Interval = 15000 };
            _fixTimer.Tick += FixTick;
            _originalFixInterval = 15000;

            // Initialize monitor timer to check intervals every second
            _monitorTimer = new WinFormsTimer { Interval = 1000 };
            _monitorTimer.Tick += MonitorTick;
            _monitorTimer.Start();

            // Initialize window check timer
            _windowCheckTimer = new WinFormsTimer { Interval = 1000 };
            _windowCheckTimer.Tick += CheckMinecraftWindow;
            _windowCheckTimer.Start();

            // Initialize drift check timer
            _driftCheckTimer = new WinFormsTimer { Interval = 1000 };
            _driftCheckTimer.Tick += CheckTimerDrift;
            _driftCheckTimer.Start();

            _sellTimer.Tick += SellTick;

            this.SuspendLayout();

            this.Text = "HeatTheGoat";
            this.ClientSize = new Size(500, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.ShowInTaskbar = true;
            this.BackColor = Color.FromArgb(15, 15, 15);
            this.Font = new Font("Segoe UI", 9F);

            // Main Button Panel
            var mainButtonPanel = new Panel
            {
                Size = new Size(460, 80),
                Location = new Point(20, 80),
                BackColor = Color.FromArgb(20, 20, 20),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(mainButtonPanel);

            _mainButton = new Button
            {
                Text = "Start Mining (F1)",
                Size = new Size(440, 60),
                Location = new Point(10, 10),
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                BackColor = Color.FromArgb(220, 40, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _mainButton.FlatAppearance.BorderSize = 0;
            _mainButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 35, 35);
            _mainButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 30, 30);
            _mainButton.Click += ToggleMining;
            mainButtonPanel.Controls.Add(_mainButton);

            // Sell All Panel
            var sellPanel = new Panel
            {
                Size = new Size(460, 200),
                Location = new Point(20, 180),
                BackColor = Color.FromArgb(20, 20, 20),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(sellPanel);

            var sellTitleLabel = new Label
            {
                Text = "Auto-Sell Settings",
                Size = new Size(440, 30),
                Location = new Point(10, 10),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 50, 50),
                BackColor = Color.Transparent
            };
            sellPanel.Controls.Add(sellTitleLabel);

            // Sell time controls
            var sellTimeLabel = new Label
            {
                Text = "Sell Interval:",
                Size = new Size(440, 25),
                Location = new Point(10, 50),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            sellPanel.Controls.Add(sellTimeLabel);

            _sellTimeValueInput = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 999999,
                Value = 10,
                Size = new Size(200, 30),
                Location = new Point(10, 85),
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White
            };
            _sellTimeValueInput.ValueChanged += UpdateSellTimerInterval;
            sellPanel.Controls.Add(_sellTimeValueInput);

            _sellTimeUnitComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(220, 30),
                Location = new Point(220, 85),
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White
            };
            _sellTimeUnitComboBox.Items.AddRange(new string[] { "Milliseconds", "Seconds", "Minutes", "Hours" });
            _sellTimeUnitComboBox.SelectedItem = "Seconds";
            _sellTimeUnitComboBox.SelectedIndexChanged += UpdateSellTimerInterval;
            sellPanel.Controls.Add(_sellTimeUnitComboBox);

            _sellTotalTimeLabel = new Label
            {
                Text = "Total: 10s",
                Size = new Size(440, 25),
                Location = new Point(10, 125),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            sellPanel.Controls.Add(_sellTotalTimeLabel);

            // Sell button
            _sellButton = new Button
            {
                Text = "Start Auto-Sell (F3)",
                Size = new Size(440, 40),
                Location = new Point(10, 155),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                BackColor = Color.FromArgb(220, 40, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _sellButton.FlatAppearance.BorderSize = 0;
            _sellButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 35, 35);
            _sellButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 30, 30);
            _sellButton.Click += ToggleSelling;
            sellPanel.Controls.Add(_sellButton);

            // Fix Panel
            var fixPanel = new Panel
            {
                Size = new Size(460, 200),
                Location = new Point(20, 400),
                BackColor = Color.FromArgb(20, 20, 20),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(fixPanel);

            var fixTitleLabel = new Label
            {
                Text = "Auto-Fix Settings",
                Size = new Size(440, 30),
                Location = new Point(10, 10),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 50, 50),
                BackColor = Color.Transparent
            };
            fixPanel.Controls.Add(fixTitleLabel);

            // Fix time controls
            var fixTimeLabel = new Label
            {
                Text = "Fix Interval:",
                Size = new Size(440, 25),
                Location = new Point(10, 50),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            fixPanel.Controls.Add(fixTimeLabel);

            _fixTimeValueInput = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 999999,
                Value = 15,
                Size = new Size(200, 30),
                Location = new Point(10, 85),
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White
            };
            _fixTimeValueInput.ValueChanged += UpdateFixTimerInterval;
            fixPanel.Controls.Add(_fixTimeValueInput);

            _fixTimeUnitComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(220, 30),
                Location = new Point(220, 85),
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White
            };
            _fixTimeUnitComboBox.Items.AddRange(new string[] { "Milliseconds", "Seconds", "Minutes", "Hours" });
            _fixTimeUnitComboBox.SelectedItem = "Minutes";
            _fixTimeUnitComboBox.SelectedIndexChanged += UpdateFixTimerInterval;
            fixPanel.Controls.Add(_fixTimeUnitComboBox);

            _fixTotalTimeLabel = new Label
            {
                Text = "Total: 15s",
                Size = new Size(440, 25),
                Location = new Point(10, 125),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            fixPanel.Controls.Add(_fixTotalTimeLabel);

            // Status Panel
            var statusPanel = new Panel
            {
                Size = new Size(460, 40),
                Location = new Point(20, 620),
                BackColor = Color.FromArgb(20, 20, 20),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(statusPanel);

            var statusLabel = new Label
            {
                Text = "Status: Ready",
                Size = new Size(440, 40),
                Location = new Point(10, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            statusPanel.Controls.Add(statusLabel);

            this.ResumeLayout(false);

            RegisterHotkeys();
            UpdateFixTimerInterval(null, EventArgs.Empty);
        }

        private Settings LoadSettings()
        {
            try
            {
                if (File.Exists(SETTINGS_FILE))
                {
                    string json = File.ReadAllText(SETTINGS_FILE);
                    return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                }
            }
            catch (Exception)
            {
                // If there's any error, return default settings
            }
            return new Settings();
        }

        private void SaveSettings(Settings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings);
                File.WriteAllText(SETTINGS_FILE, json);
            }
            catch (Exception)
            {
                // If there's any error, just ignore it
            }
        }

        private void RegisterHotkeys()
        {
            try
            {
                if (!Win32RegisterHotKey(this.Handle, _miningHotkeyId, HOTKEY_NO_MODIFIER, VIRTUAL_KEY_F1))
                {
                    MessageBox.Show("Failed to register F1 hotkey. Please run as Administrator.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                if (!Win32RegisterHotKey(this.Handle, _sellingHotkeyId, HOTKEY_NO_MODIFIER, VIRTUAL_KEY_F3))
                {
                    MessageBox.Show("Failed to register F3 hotkey. Please run as Administrator.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error registering hotkeys: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MiningTick(object? sender, EventArgs e)
        {
            if (!_isMinecraftRunning)
            {
                if (_isMouseButtonDown)
                {
                    Win32MouseEvent(MOUSE_LEFT_BUTTON_UP, 0, 0, 0, 0);
                    _isMouseButtonDown = false;
                }
                return;
            }

            if (_isMiningActive && !_isMouseButtonDown && !_isFixOperationInProgress)
            {
                Win32MouseEvent(MOUSE_LEFT_BUTTON_DOWN, 0, 0, 0, 0);
                _isMouseButtonDown = true;
            }
            else if (_isMiningActive && _isMouseButtonDown && _isFixOperationInProgress)
            {
                Win32MouseEvent(MOUSE_LEFT_BUTTON_UP, 0, 0, 0, 0);
                _isMouseButtonDown = false;
            }
        }

        private void SendKeyEvent(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo)
        {
            // Don't send Alt key events
            if (bVk == VK_MENU)
            {
                return;
            }

            // If Alt is pressed, release it before sending other keys
            if (_isAltPressed)
            {
                Win32KeybdEvent(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
                _isAltPressed = false;
                Thread.Sleep(50); // Give time for Alt to be fully released
            }

            // If Tab is pressed, release it
            if (_isTabPressed)
            {
                Win32KeybdEvent(VK_TAB, 0, KEYEVENTF_KEYUP, 0);
                _isTabPressed = false;
                Thread.Sleep(50); // Give time for Tab to be fully released
            }

            // Send the actual key event
            Win32KeybdEvent(bVk, bScan, dwFlags, dwExtraInfo);
        }

        private void FixTick(object? sender, EventArgs e)
        {
            if (!_isMinecraftRunning)
            {
                return;
            }

            var now = DateTime.Now;
            if (now < _nextFixTime)
            {
                return;
            }

            Console.WriteLine("Starting fix operation");
            _isFixOperationInProgress = true;

            try
            {
                if (_isMouseButtonDown)
                {
                    Win32MouseEvent(MOUSE_LEFT_BUTTON_UP, 0, 0, 0, 0);
                    _isMouseButtonDown = false;
                }
                Thread.Sleep(100);

                // Send /fix command
                SendKeyEvent(VK_T, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(30);
                SendKeyEvent(VK_T, 0, KEYEVENTF_KEYUP, 0);

                SendKeys.SendWait("/fix");
                Thread.Sleep(50);

                SendKeyEvent(VK_ENTER, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(30);
                SendKeyEvent(VK_ENTER, 0, KEYEVENTF_KEYUP, 0);

                Thread.Sleep(500);

                // Send up arrow and enter to confirm
                SendKeyEvent(0x26, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(30);
                SendKeyEvent(0x26, 0, KEYEVENTF_KEYUP, 0);
                Thread.Sleep(50);

                SendKeyEvent(VK_ENTER, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(30);
                SendKeyEvent(VK_ENTER, 0, KEYEVENTF_KEYUP, 0);

                Thread.Sleep(500);

                // Resume mining if it was active
                if (_isMiningActive && !_isFixOperationInProgress)
                {
                    Win32MouseEvent(MOUSE_LEFT_BUTTON_DOWN, 0, 0, 0, 0);
                    _isMouseButtonDown = true;
                }
            }
            finally
            {
                _isFixOperationInProgress = false;
                _lastFixOperationTime = now;
                _nextFixTime = now.AddMilliseconds(_originalFixInterval);
                _fixIntervalCount++;
                Console.WriteLine($"Fix operation completed. Next fix scheduled for: {_nextFixTime:HH:mm:ss.fff}");
            }
        }

        private void MonitorTick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            
            // Check fix timer
            if (_isFixTimerRunning)
            {
                if (_fixStartTime == DateTime.MinValue)
                {
                    _fixStartTime = now;
                    _lastFixCheck = now;
                    _lastFixOperation = now;
                }

                lock (_fixLock)
                {
                    var timeSinceLastFix = (now - _lastFixCheck).TotalSeconds;
                    if (timeSinceLastFix >= 1)
                    {
                        var expectedIntervals = (int)(timeSinceLastFix * 1000 / _originalFixInterval);
                        var actualIntervals = _fixIntervalCount;
                        var drift = actualIntervals - expectedIntervals;
                        _fixTotalDrift += drift;

                        // Calculate total elapsed time and expected intervals
                        var totalElapsedMs = (now - _fixStartTime).TotalMilliseconds;
                        var expectedTotalIntervals = (int)(totalElapsedMs / _originalFixInterval);
                        var totalDrift = actualIntervals - expectedTotalIntervals;

                        // Calculate time since last operation
                        var timeSinceLastOperation = (now - _lastFixOperation).TotalMilliseconds;
                        var expectedTimeSinceLast = _originalFixInterval;
                        var operationDrift = timeSinceLastOperation - expectedTimeSinceLast;

                        Console.WriteLine($"Fix Timer Check - Interval: {_originalFixInterval}ms");
                        Console.WriteLine($"  Current: Count={actualIntervals}, Expected={expectedIntervals}, Drift={drift}");
                        Console.WriteLine($"  Total: Elapsed={totalElapsedMs:F0}ms, Expected={expectedTotalIntervals}, Total Drift={totalDrift}");
                        Console.WriteLine($"  Last Operation: {timeSinceLastOperation:F0}ms ago (Expected: {expectedTimeSinceLast}ms, Drift: {operationDrift:F0}ms)");
                        Console.WriteLine($"  Average Drift: {_fixTotalDrift / actualIntervals:F2} intervals per check");
                        
                        _lastFixCheck = now;
                    }
                }
            }

            // Check mining status
            if (_isMiningActive)
            {
                Console.WriteLine($"Mining Status - Active: {_isMiningActive}, Mouse Down: {_isMouseButtonDown}, Fix In Progress: {_isFixOperationInProgress}");
            }
        }

        private void ToggleMining(object? sender, EventArgs e)
        {
            if (!_isMiningActive)
            {
                _miningTimer.Start();
                _mainButton.Text = "Stop Mining (F1)";
                _mainButton.BackColor = Color.FromArgb(180, 35, 35);
                _mainButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(160, 30, 30);
                _mainButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(140, 25, 25);
                _isMiningActive = true;
                _trayMenu.Items[2].Text = "Stop Mining";
                Console.WriteLine("Mining started");
            }
            else
            {
                _miningTimer.Stop();
                _mainButton.Text = "Start Mining (F1)";
                _mainButton.BackColor = Color.FromArgb(220, 40, 40);
                _mainButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 35, 35);
                _mainButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 30, 30);
                _isMiningActive = false;
                _trayMenu.Items[2].Text = "Start Mining";
                Console.WriteLine("Mining stopped");

                if (_isMouseButtonDown)
                {
                    Win32MouseEvent(MOUSE_LEFT_BUTTON_UP, 0, 0, 0, 0);
                    _isMouseButtonDown = false;
                }
            }
        }

        private void UpdateFixTimerInterval(object? sender, EventArgs e)
        {
            int totalMilliseconds = 0;
            string unit = _fixTimeUnitComboBox.SelectedItem?.ToString() ?? "Seconds";

            switch (unit)
            {
                case "Hours":
                    totalMilliseconds = (int)(_fixTimeValueInput.Value * 3600000);
                    break;
                case "Minutes":
                    totalMilliseconds = (int)(_fixTimeValueInput.Value * 60000);
                    break;
                case "Seconds":
                    totalMilliseconds = (int)(_fixTimeValueInput.Value * 1000);
                    break;
                case "Milliseconds":
                    totalMilliseconds = (int)_fixTimeValueInput.Value;
                    break;
            }

            lock (_fixLock)
            {
                _originalFixInterval = totalMilliseconds;
                _fixTimer.Interval = totalMilliseconds;
                _fixTotalTimeLabel.Text = $"Total: {FormatTimeSpan(totalMilliseconds)}";

                // Reset monitoring counters when interval changes
                _lastFixCheck = DateTime.Now;
                _fixStartTime = DateTime.Now;
                _fixIntervalCount = 0;
                _fixTotalDrift = 0;
                _lastFixOperation = DateTime.Now;
                _nextFixTime = DateTime.Now.AddMilliseconds(totalMilliseconds);

                if (_isFixingActive)
                {
                    Console.WriteLine($"Fix interval updated to {FormatTimeSpan(totalMilliseconds)}. Next fix scheduled for: {_nextFixTime:HH:mm:ss.fff}");
                }
            }

            var settings = LoadSettings();
            settings.FixTimeValue = (int)_fixTimeValueInput.Value;
            settings.FixTimeUnit = unit;
            SaveSettings(settings);
        }

        private string FormatTimeSpan(int milliseconds)
        {
            var timeSpan = TimeSpan.FromMilliseconds(milliseconds);
            if (timeSpan.TotalHours >= 1)
            {
                return $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s {timeSpan.Milliseconds}ms";
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s {timeSpan.Milliseconds}ms";
            }
            else if (timeSpan.TotalSeconds >= 1)
            {
                return $"{timeSpan.Seconds}s {timeSpan.Milliseconds}ms";
            }
            else
            {
                return $"{timeSpan.Milliseconds}ms";
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY_MESSAGE)
            {
                if (m.WParam.ToInt32() == _miningHotkeyId)
                {
                    ToggleMining(this, EventArgs.Empty);
                }
                else if (m.WParam.ToInt32() == _sellingHotkeyId)
                {
                    ToggleSelling(this, EventArgs.Empty);
                }
            }
            else if (m.Msg == 0x0100) // WM_KEYDOWN
            {
                if (m.WParam.ToInt32() == VK_MENU)
                {
                    _isAltPressed = true;
                    return; // Prevent Alt from being processed
                }
                else if (m.WParam.ToInt32() == VK_TAB)
                {
                    _isTabPressed = true;
                    if (_isAltPressed)
                    {
                        // If Alt+Tab is pressed, send Escape to close any open menu
                        Win32KeybdEvent(VK_ESCAPE, 0, KEYEVENTF_KEYDOWN, 0);
                        Thread.Sleep(10);
                        Win32KeybdEvent(VK_ESCAPE, 0, KEYEVENTF_KEYUP, 0);
                        return;
                    }
                }
            }
            else if (m.Msg == 0x0101) // WM_KEYUP
            {
                if (m.WParam.ToInt32() == VK_MENU)
                {
                    _isAltPressed = false;
                    return; // Prevent Alt from being processed
                }
                else if (m.WParam.ToInt32() == VK_TAB)
                {
                    _isTabPressed = false;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_isClosing)
            {
                _isClosing = true;
                _trayIcon.Visible = false;
                
                // Clean up timers and operations
                if (_isMiningActive)
                {
                    _miningTimer.Stop();
                    if (_isMouseButtonDown)
                    {
                        Win32MouseEvent(MOUSE_LEFT_BUTTON_UP, 0, 0, 0, 0);
                        _isMouseButtonDown = false;
                    }
                }

                _fixTimer.Stop();
                _monitorTimer.Stop();
                _windowCheckTimer.Stop();
                _driftCheckTimer.Stop();

                // Unregister hotkeys
                Win32UnregisterHotKey(Handle, _miningHotkeyId);
                Win32UnregisterHotKey(Handle, _sellingHotkeyId);

                // Dispose of resources
                _miningTimer?.Dispose();
                _fixTimer?.Dispose();
                _monitorTimer?.Dispose();
                _windowCheckTimer?.Dispose();
                _driftCheckTimer?.Dispose();
                _trayIcon?.Dispose();
                _trayMenu?.Dispose();

                Application.Exit();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Win32UnregisterHotKey(Handle, _miningHotkeyId);
                Win32UnregisterHotKey(Handle, _sellingHotkeyId);
                _miningTimer?.Dispose();
                _fixTimer?.Dispose();
                _monitorTimer?.Dispose();
                _windowCheckTimer?.Dispose();
                _driftCheckTimer?.Dispose();
                _trayIcon?.Dispose();
                _trayMenu?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void QueueOperation(Action operation, bool isSellOperation)
        {
            lock (_operationLock)
            {
                if (isSellOperation)
                {
                    if (_isFixOperationInProgress)
                        return;
                    _isFixOperationInProgress = true;
                }
                else
                {
                    if (_isFixOperationInProgress)
                        return;
                    _isFixOperationInProgress = true;
                }

                _operationQueue.Enqueue(operation);
                ProcessOperationQueue();
            }
        }

        private void ProcessOperationQueue()
        {
            if (_operationQueue.Count == 0)
                return;

            var nextOperation = _operationQueue.Dequeue();
            nextOperation();
        }

        private void ShowWindow(object? sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        private void ExitApplication(object? sender, EventArgs e)
        {
            _isClosing = true;
            _trayIcon.Visible = false;
            
            // Clean up timers and operations
            if (_isMiningActive)
            {
                _miningTimer.Stop();
                if (_isMouseButtonDown)
                {
                    Win32MouseEvent(MOUSE_LEFT_BUTTON_UP, 0, 0, 0, 0);
                    _isMouseButtonDown = false;
                }
            }

            _fixTimer.Stop();
            _monitorTimer.Stop();
            _windowCheckTimer.Stop();
            _driftCheckTimer.Stop();

            // Unregister hotkeys
            Win32UnregisterHotKey(Handle, _miningHotkeyId);
            Win32UnregisterHotKey(Handle, _sellingHotkeyId);

            // Dispose of resources
            _miningTimer?.Dispose();
            _fixTimer?.Dispose();
            _monitorTimer?.Dispose();
            _windowCheckTimer?.Dispose();
            _driftCheckTimer?.Dispose();
            _trayIcon?.Dispose();
            _trayMenu?.Dispose();

            Application.Exit();
        }

        private void CheckMinecraftWindow(object? sender, EventArgs e)
        {
            bool wasMinecraftRunning = _isMinecraftRunning;
            _isMinecraftRunning = IsMinecraftRunning();

            if (!wasMinecraftRunning && _isMinecraftRunning)
            {
                // Minecraft just started, send Escape to ensure no menus are open
                Win32KeybdEvent(VK_ESCAPE, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(10);
                Win32KeybdEvent(VK_ESCAPE, 0, KEYEVENTF_KEYUP, 0);
                Thread.Sleep(100); // Give time for menu to close
            }

            // Update UI to show Minecraft status
            this.Text = $"HeatTheGoat - Minecraft: {(_isMinecraftRunning ? "Running" : "Not Running")}";
        }

        private bool IsMinecraftRunning()
        {
            bool found = false;
            EnumWindows((hWnd, lParam) =>
            {
                var windowTitle = new StringBuilder(256);
                GetWindowText(hWnd, windowTitle, 256);
                if (windowTitle.ToString().Contains(MINECRAFT_WINDOW_TITLE))
                {
                    found = true;
                    return false; // Stop enumeration
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);
            return found;
        }

        private void CheckTimerDrift(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            
            // Check fix timer
            if (_isFixTimerRunning)
            {
                if (_fixStartTime == DateTime.MinValue)
                {
                    _fixStartTime = now;
                    _lastFixCheck = now;
                    _lastFixOperation = now;
                }

                lock (_fixLock)
                {
                    var timeSinceLastFix = (now - _lastFixCheck).TotalSeconds;
                    if (timeSinceLastFix >= 1)
                    {
                        var expectedIntervals = (int)(timeSinceLastFix * 1000 / _originalFixInterval);
                        var actualIntervals = _fixIntervalCount;
                        var drift = actualIntervals - expectedIntervals;
                        _fixTotalDrift += drift;

                        // Calculate total elapsed time and expected intervals
                        var totalElapsedMs = (now - _fixStartTime).TotalMilliseconds;
                        var expectedTotalIntervals = (int)(totalElapsedMs / _originalFixInterval);
                        var totalDrift = actualIntervals - expectedTotalIntervals;

                        // Calculate time since last operation
                        var timeSinceLastOperation = (now - _lastFixOperation).TotalMilliseconds;
                        var expectedTimeSinceLast = _originalFixInterval;
                        var operationDrift = timeSinceLastOperation - expectedTimeSinceLast;

                        // Check if we need to adjust the timer
                        if (Math.Abs(operationDrift) > 100) // If drift is more than 100ms
                        {
                            Console.WriteLine($"Fix Timer Drift Detected: {operationDrift:F0}ms");
                            _fixTimer.Interval = _originalFixInterval;
                            _nextFixTime = now.AddMilliseconds(_originalFixInterval);
                        }

                        Console.WriteLine($"Fix Timer Status - Interval: {_originalFixInterval}ms");
                        Console.WriteLine($"  Current: Count={actualIntervals}, Expected={expectedIntervals}, Drift={drift}");
                        Console.WriteLine($"  Total: Elapsed={totalElapsedMs:F0}ms, Expected={expectedTotalIntervals}, Total Drift={totalDrift}");
                        Console.WriteLine($"  Last Operation: {timeSinceLastOperation:F0}ms ago (Expected: {expectedTimeSinceLast}ms, Drift: {operationDrift:F0}ms)");
                        Console.WriteLine($"  Average Drift: {_fixTotalDrift / actualIntervals:F2} intervals per check");
                        
                        _lastFixCheck = now;
                    }
                }
            }
        }

        private void ToggleSelling(object? sender, EventArgs e)
        {
            if (!_isSellingActive)
            {
                _sellTimer.Start();
                _nextSellTime = DateTime.Now.AddMilliseconds(_originalSellInterval);
                _isSellingActive = true;
                _sellButton.Text = "Stop Auto-Sell (F3)";
                _sellButton.BackColor = Color.FromArgb(180, 35, 35);
                Console.WriteLine("Auto-sell started");
            }
            else
            {
                _sellTimer.Stop();
                _isSellingActive = false;
                _sellButton.Text = "Start Auto-Sell (F3)";
                _sellButton.BackColor = Color.FromArgb(220, 40, 40);
                Console.WriteLine("Auto-sell stopped");
            }
        }

        private void UpdateSellTimerInterval(object? sender, EventArgs e)
        {
            int totalMilliseconds = 0;
            string unit = _sellTimeUnitComboBox.SelectedItem?.ToString() ?? "Seconds";

            switch (unit)
            {
                case "Hours":
                    totalMilliseconds = (int)(_sellTimeValueInput.Value * 3600000);
                    break;
                case "Minutes":
                    totalMilliseconds = (int)(_sellTimeValueInput.Value * 60000);
                    break;
                case "Seconds":
                    totalMilliseconds = (int)(_sellTimeValueInput.Value * 1000);
                    break;
                case "Milliseconds":
                    totalMilliseconds = (int)_sellTimeValueInput.Value;
                    break;
            }

            _originalSellInterval = totalMilliseconds;
            _sellTimer.Interval = totalMilliseconds;
            _sellTotalTimeLabel.Text = $"Total: {FormatTimeSpan(totalMilliseconds)}";

            if (_isSellingActive)
            {
                _nextSellTime = DateTime.Now.AddMilliseconds(totalMilliseconds);
                Console.WriteLine($"Sell interval updated to {FormatTimeSpan(totalMilliseconds)}. Next sell scheduled for: {_nextSellTime:HH:mm:ss.fff}");
            }
        }

        private void SellTick(object? sender, EventArgs e)
        {
            if (!_isMinecraftRunning)
            {
                return;
            }

            var now = DateTime.Now;
            if (now < _nextSellTime)
            {
                return;
            }

            bool wasMiningActive = _isMiningActive;
            
            if (wasMiningActive)
            {
                if (_isMouseButtonDown)
                {
                    Win32MouseEvent(MOUSE_LEFT_BUTTON_UP, 0, 0, 0, 0);
                    _isMouseButtonDown = false;
                }
            }

            try
            {
                // Send /sell all command
                SendKeyEvent(VK_T, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(100);
                SendKeyEvent(VK_T, 0, KEYEVENTF_KEYUP, 0);
                Thread.Sleep(300);

                // Type the command character by character with longer delays
                SendKeys.SendWait("/");
                Thread.Sleep(100);
                SendKeys.SendWait("sell");
                Thread.Sleep(100);
                SendKeys.SendWait(" ");
                Thread.Sleep(100);
                SendKeys.SendWait("all");
                Thread.Sleep(300);

                SendKeyEvent(VK_ENTER, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(100);
                SendKeyEvent(VK_ENTER, 0, KEYEVENTF_KEYUP, 0);

                Thread.Sleep(1500);

                // Resume mining if it was active
                if (wasMiningActive && !_isFixOperationInProgress)
                {
                    Win32MouseEvent(MOUSE_LEFT_BUTTON_DOWN, 0, 0, 0, 0);
                    _isMouseButtonDown = true;
                }

                Console.WriteLine($"Sell operation completed. Next sell scheduled for: {_nextSellTime:HH:mm:ss.fff}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during sell operation: {ex.Message}");
            }
            finally
            {
                _nextSellTime = now.AddMilliseconds(_originalSellInterval);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MinecraftAutoMinerForm());
        }
    }
}