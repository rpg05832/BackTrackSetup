using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;

namespace BackTrack
{
    static class Program
    {
        private static NotifyIcon? _trayIcon;
        private static Tracker? _tracker;
        private static RecentFilesWatcher? _recentWatcher;
        private static ClipboardMonitor? _clipboardMonitor;
        private static SnapshotManager? _snapshotManager;
        private static WindowManager? _windowManager;
        private static WebServer? _webServer;
        private static MainForm? _mainForm;
        private static Form? _uiInvoker;
        private static System.Threading.Timer? _pollTimer;
        private static Mutex? _appMutex;
        private const string MutexName = "BackTrackSingleInstanceMutex";
        private const int WebPort = 8420;
        private const int HotkeyId = 1;

        [STAThread]
        static void Main()
        {
            // 1. Single Instance Check
            _appMutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                MessageBox.Show("אפליקציית BackTrack כבר רצה ברקע במחשב זה.", "BackTrack", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // Set to start with Windows startup
                SetStartup(true);

                // 2. Initialize Core Logic
                _tracker = new Tracker();
                _clipboardMonitor = new ClipboardMonitor();
                _snapshotManager = new SnapshotManager();
                _windowManager = new WindowManager();

                // 2b. Hidden form used to marshal work onto the UI thread (for window merge).
                _uiInvoker = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Opacity = 0, Width = 0, Height = 0 };
                var _ = _uiInvoker.Handle; // force handle creation on the UI thread
                UiBridge.Invoker = _uiInvoker;
                DockHost.UiInvoke = (action) =>
                {
                    try
                    {
                        if (_uiInvoker != null && _uiInvoker.IsHandleCreated)
                            _uiInvoker.BeginInvoke(action);
                    }
                    catch (Exception ex) { Debug.WriteLine($"UiInvoke error: {ex.Message}"); }
                };

                // 3. Start Polling Loop for folders and apps (Every 1 second)
                _pollTimer = new System.Threading.Timer((state) =>
                {
                    _tracker?.Poll();
                }, null, 1000, 1000);

                // 4. Start Recent Files Watcher
                _recentWatcher = new RecentFilesWatcher((filePath, fileName) =>
                {
                    _tracker?.AddFileToHistory(filePath, fileName);
                });
                _recentWatcher.Start();

                // 5. Start Local Web Server (serves the UI loaded by the native window)
                _webServer = new WebServer(_tracker, _clipboardMonitor, _snapshotManager, _windowManager, WebPort);
                _webServer.Start();

                // 6. Register Global Hotkey (Ctrl + Alt + Z)
                bool hotkeyRegistered = Win32.RegisterHotKey(IntPtr.Zero, HotkeyId, Win32.MOD_CONTROL | Win32.MOD_ALT, 0x5A);

                // 7. Setup Hotkey Event Message Filter
                Application.AddMessageFilter(new HotkeyMessageFilter(OnHotkeyTriggered));

                // 8. Setup System Tray Icon
                SetupTrayIcon(hotkeyRegistered);

                // 8b. Start Groupy-style drag-to-group detection
                DragGroupManager.Start();

                // 9. Run message loop
                Application.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"שגיאה במהלך הפעלת האפליקציה: {ex.Message}", "שגיאת מערכת BackTrack", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cleanup();
            }
        }

        private static void SetupTrayIcon(bool hotkeyRegistered)
        {
            Icon icon;
            try
            {
                var assembly = typeof(Program).Assembly;
                using var stream = assembly.GetManifestResourceStream("BackTrack.backtrack_icon.ico");
                icon = stream != null ? new Icon(stream) : SystemIcons.Application;
            }
            catch
            {
                icon = SystemIcons.Application;
            }

            _trayIcon = new NotifyIcon
            {
                Icon = icon,
                Text = "BackTrack - שחזור פעולות במחשב",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip
            {
                RightToLeft = RightToLeft.Yes
            };

            var openItem = new ToolStripMenuItem("פתח את BackTrack", null, (s, e) => ShowMainWindow());
            var restoreItem = new ToolStripMenuItem("שחזר פעולה אחרונה (Ctrl + Alt + Z)", null, (s, e) => OnHotkeyTriggered());

            // Saved workspace groups submenu (populated dynamically on open)
            var groupsItem = new ToolStripMenuItem("פתח קבוצת עבודה");

            // Toggle Groupy-style drag-to-group
            var dragItem = new ToolStripMenuItem("מיזוג בגרירה") { CheckOnClick = true, Checked = DragGroupManager.Enabled };
            dragItem.Click += (s, e) => { DragGroupManager.Enabled = dragItem.Checked; };

            // Private Mode Item with checkbox toggle
            var privateModeItem = new ToolStripMenuItem("מצב פרטי (אל תפריע)", null, (s, e) => TogglePrivateMode());
            privateModeItem.CheckOnClick = true;

            var exitItem = new ToolStripMenuItem("יציאה", null, (s, e) => Application.Exit());

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(restoreItem);
            contextMenu.Items.Add(groupsItem);
            contextMenu.Items.Add(dragItem);
            contextMenu.Items.Add(privateModeItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();

            // Synchronize the menu state with the API when opening the context menu
            contextMenu.Opening += (s, e) =>
            {
                privateModeItem.Checked = _tracker?.IsPrivateMode ?? false;
                BuildGroupsMenu(groupsItem);
                dragItem.Checked = DragGroupManager.Enabled;
            };

            string hotkeyStatus = hotkeyRegistered ? "קיצור המקשים Ctrl+Alt+Z פעיל." : "שגיאה ברישום קיצור המקשים.";
            _trayIcon.ShowBalloonTip(3000, "BackTrack פעיל", $"המערכת מנטרת תיקיות, קבצים ואפליקציות שנסגרים. {hotkeyStatus}", ToolTipIcon.Info);
        }

        private static void BuildGroupsMenu(ToolStripMenuItem groupsItem)
        {
            groupsItem.DropDownItems.Clear();
            var groups = _snapshotManager?.GetSnapshots() ?? new System.Collections.Generic.List<WorkspaceSnapshot>();

            if (groups.Count == 0)
            {
                var none = new ToolStripMenuItem("(אין קבוצות שמורות)") { Enabled = false };
                groupsItem.DropDownItems.Add(none);
                return;
            }

            foreach (var g in groups)
            {
                int count = (g.Items != null && g.Items.Count > 0) ? g.Items.Count : (g.Paths?.Count ?? 0);
                string label = $"{g.Name}  ({count})";
                string id = g.Id;
                var gi = new ToolStripMenuItem(label, null, (s, e) =>
                {
                    var items = _snapshotManager?.GetItems(id) ?? new System.Collections.Generic.List<SnapshotItem>();
                    if (items.Count > 0) GroupLauncher.OpenMerged(items);
                    else _trayIcon?.ShowBalloonTip(1500, "BackTrack", "הקבוצה ריקה.", ToolTipIcon.Warning);
                });
                groupsItem.DropDownItems.Add(gi);
            }
        }

        private static void ShowMainWindow()
        {
            try
            {
                if (_mainForm == null || _mainForm.IsDisposed)
                {
                    _mainForm = new MainForm(WebPort);
                    _mainForm.Show();
                }
                else
                {
                    if (!_mainForm.Visible) _mainForm.Show();
                    if (_mainForm.WindowState == FormWindowState.Minimized) _mainForm.WindowState = FormWindowState.Normal;
                    _mainForm.Activate();
                    _mainForm.BringToFront();
                }
            }
            catch (Exception ex)
            {
                // Fallback to the browser if the native window cannot be shown.
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = $"http://localhost:{WebPort}/", UseShellExecute = true });
                }
                catch { }
                Debug.WriteLine($"ShowMainWindow error: {ex.Message}");
            }
        }

        private static void TogglePrivateMode()
        {
            if (_tracker == null || _clipboardMonitor == null || _trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in _trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text != null && menuItem.Text.StartsWith("מצב פרטי"))
                {
                    bool enabled = menuItem.Checked;
                    _tracker.IsPrivateMode = enabled;
                    _clipboardMonitor.IsPrivateMode = enabled;

                    string statusText = enabled ? "מצב פרטי פעיל. הניטור מושהה זמנית." : "מצב פרטי כבוי. הניטור פעיל.";
                    _trayIcon.ShowBalloonTip(1500, "BackTrack", statusText, ToolTipIcon.Info);
                    break;
                }
            }
        }

        private static void OnHotkeyTriggered()
        {
            if (_tracker == null) return;

            // 1. Check if the active window is a web browser
            if (IsActiveWindowBrowser())
            {
                Win32.SimulateCtrlShiftT();
                return;
            }

            // 2. Otherwise, restore the last tracked closed item
            bool success = _tracker.RestoreLast();
            if (!success)
            {
                _trayIcon?.ShowBalloonTip(1500, "BackTrack", "אין פעילויות שנסגרו בזיכרון לשחזור.", ToolTipIcon.Warning);
            }
        }

        private static bool IsActiveWindowBrowser()
        {
            try
            {
                IntPtr hWnd = Win32.GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return false;

                Win32.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return false;

                using var proc = Process.GetProcessById((int)pid);
                string procName = proc.ProcessName.ToLowerInvariant();

                return procName == "chrome" ||
                       procName == "msedge" ||
                       procName == "firefox" ||
                       procName == "opera" ||
                       procName == "brave";
            }
            catch
            {
                return false;
            }
        }

        private static void Cleanup()
        {
            // Restore any windows that were merged into a container (experimental feature)
            try { DockHost.RestoreAll(); } catch { }
            try { DragGroupManager.Stop(); } catch { }

            // Unregister hotkey
            Win32.UnregisterHotKey(IntPtr.Zero, HotkeyId);

            // Stop watchers & timer
            _recentWatcher?.Stop();
            _pollTimer?.Dispose();

            // Stop clipboard listener
            _clipboardMonitor?.Dispose();

            // Stop server
            _webServer?.Stop();

            // Dispose native windows
            try { _mainForm?.Dispose(); } catch { }
            try { _uiInvoker?.Dispose(); } catch { }

            // Remove tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            // Release mutex
            if (_appMutex != null)
            {
                _appMutex.ReleaseMutex();
                _appMutex.Close();
            }
        }

        private static void SetStartup(bool startWithWindows)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(appPath))
                    {
                        if (startWithWindows)
                        {
                            key.SetValue("BackTrack", $"\"{appPath}\"");
                        }
                        else
                        {
                            key.DeleteValue("BackTrack", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set startup registry key: {ex.Message}");
            }
        }
    }

    public class HotkeyMessageFilter : IMessageFilter
    {
        private readonly Action _onHotkeyTriggered;

        public HotkeyMessageFilter(Action onHotkeyTriggered)
        {
            _onHotkeyTriggered = onHotkeyTriggered;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == Win32.WM_HOTKEY)
            {
                _onHotkeyTriggered();
                return true;
            }
            return false;
        }
    }
}
