using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Text;

namespace BackTrack
{
    public class HistoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "folder"; // "folder", "file", "app"
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class Tracker
    {
        private readonly string _historyDir;
        private readonly string _historyFilePath;
        private readonly int _maxHistory = 100; // Increased to 100 for premium experience
        private readonly int _keepDays = 7;     // Keep history for 7 days

        // Active folder window state
        private Dictionary<string, string> _activeFolders = new Dictionary<string, string>(); // BackTrackID -> Path
        private Dictionary<string, string> _folderNames = new Dictionary<string, string>();     // BackTrackID -> LocationName

        // Active application window state
        private Dictionary<IntPtr, string> _activeApps = new Dictionary<IntPtr, string>();       // HWND -> ExecutablePath
        private Dictionary<IntPtr, string> _activeAppTitles = new Dictionary<IntPtr, string>();   // HWND -> WindowTitle

        private List<HistoryItem> _history = new List<HistoryItem>();
        private readonly object _lock = new object();
        private bool _isPrivateMode = false;

        public bool IsPrivateMode
        {
            get => _isPrivateMode;
            set
            {
                lock (_lock)
                {
                    _isPrivateMode = value;
                    if (_isPrivateMode)
                    {
                        // Clear active states on toggle to private so we don't log them when turning private off
                        _activeFolders.Clear();
                        _folderNames.Clear();
                        _activeApps.Clear();
                        _activeAppTitles.Clear();
                    }
                }
            }
        }

        public Tracker()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _historyDir = Path.Combine(userProfile, ".backtrack");
            _historyFilePath = Path.Combine(_historyDir, "history.json");

            LoadHistory();
        }

        public void Poll()
        {
            lock (_lock)
            {
                if (_isPrivateMode) return;
            }

            bool changed = false;

            // --- 1. POLL FILE EXPLORER WINDOWS ---
            var currentFolders = new Dictionary<string, string>();
            var currentFolderNames = new Dictionary<string, string>();

            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic windows = shell.Windows();
                        int count = windows.Count;

                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                dynamic window = windows.Item(i);
                                if (window == null) continue;

                                string fullName = window.FullName;
                                if (fullName.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    string path = window.Document.Folder.Self.Path;
                                    string name = window.LocationName;

                                    if (string.IsNullOrEmpty(path)) continue;

                                    string? backtrackId = window.GetProperty("BackTrackID") as string;
                                    if (string.IsNullOrEmpty(backtrackId))
                                    {
                                        backtrackId = Guid.NewGuid().ToString();
                                        window.PutProperty("BackTrackID", backtrackId);
                                    }

                                    currentFolders[backtrackId] = path;
                                    currentFolderNames[backtrackId] = !string.IsNullOrEmpty(name) ? name : Path.GetFileName(path);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"COM Explorer error: {ex.Message}");
            }

            // --- 2. POLL APPLICATION WINDOWS ---
            var currentApps = new Dictionary<IntPtr, string>();
            var currentAppTitles = new Dictionary<IntPtr, string>();

            try
            {
                Win32.EnumWindows((hWnd, lParam) =>
                {
                    if (Win32.IsWindowVisible(hWnd))
                    {
                        int length = Win32.GetWindowTextLength(hWnd);
                        if (length > 0)
                        {
                            IntPtr owner = Win32.GetWindow(hWnd, Win32.GW_OWNER);
                            int exStyle = Win32.GetWindowLong(hWnd, Win32.GWL_EXSTYLE);
                            
                            if (owner == IntPtr.Zero && (exStyle & Win32.WS_EX_TOOLWINDOW) == 0)
                            {
                                StringBuilder sb = new StringBuilder(length + 1);
                                Win32.GetWindowText(hWnd, sb, sb.Capacity);
                                string title = sb.ToString();

                                Win32.GetWindowThreadProcessId(hWnd, out uint pid);
                                if (pid > 0)
                                {
                                    try
                                    {
                                        using var proc = Process.GetProcessById((int)pid);
                                        string exePath = proc.MainModule?.FileName ?? "";
                                        if (!string.IsNullOrEmpty(exePath))
                                        {
                                            string procName = Path.GetFileName(exePath).ToLowerInvariant();
                                            
                                            if (procName != "explorer.exe" && 
                                                procName != "החזר פעולה במחשב.exe" && 
                                                procName != "backtrack.exe")
                                            {
                                                if (procName != "shellexperiencehost.exe" && 
                                                    procName != "searchhost.exe" && 
                                                    procName != "applicationframehost.exe" &&
                                                    procName != "taskhostw.exe" &&
                                                    procName != "ctfmon.exe")
                                                {
                                                    currentApps[hWnd] = exePath;
                                                    currentAppTitles[hWnd] = title;
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnumWindows error: {ex.Message}");
            }

            // --- 3. COMPARE AND IDENTIFY CLOSURES ---
            lock (_lock)
            {
                // Verify we are not in private mode (double check)
                if (_isPrivateMode) return;

                // Check closed folders
                foreach (var kvp in _activeFolders)
                {
                    string oldId = kvp.Key;
                    string oldPath = kvp.Value;

                    if (!currentFolders.ContainsKey(oldId))
                    {
                        string name = _folderNames.TryGetValue(oldId, out var n) ? n : Path.GetFileName(oldPath);
                        if (string.IsNullOrEmpty(name)) name = oldPath;

                        AddToHistoryLocked(oldPath, name, "folder");
                        changed = true;
                    }
                }

                // Check closed apps
                foreach (var kvp in _activeApps)
                {
                    IntPtr oldHWnd = kvp.Key;
                    string oldExePath = kvp.Value;

                    if (!currentApps.ContainsKey(oldHWnd))
                    {
                        string title = _activeAppTitles.TryGetValue(oldHWnd, out var t) ? t : "";
                        string appName = Path.GetFileNameWithoutExtension(oldExePath);
                        string displayName = !string.IsNullOrEmpty(title) ? $"{appName} ({title})" : appName;

                        AddToHistoryLocked(oldExePath, displayName, "app");
                        changed = true;
                    }
                }

                // Update active states
                _activeFolders = currentFolders;
                _folderNames = currentFolderNames;
                _activeApps = currentApps;
                _activeAppTitles = currentAppTitles;

                // Daily auto-prune during active polling
                if (PruneOldHistoryLocked())
                {
                    changed = true;
                }

                if (changed)
                {
                    SaveHistoryLocked();
                }
            }
        }

        public void AddFileToHistory(string filePath, string fileName)
        {
            lock (_lock)
            {
                if (_isPrivateMode) return;

                AddToHistoryLocked(filePath, fileName, "file");
                SaveHistoryLocked();
            }
        }

        private void AddToHistoryLocked(string path, string name, string type)
        {
            // Avoid duplicate additions at the top of history (within 3 seconds)
            if (_history.Count > 0)
            {
                var last = _history[0];
                if (last.Path == path && (DateTime.Now - last.Timestamp).TotalSeconds < 3)
                {
                    last.Timestamp = DateTime.Now; // update timestamp
                    return;
                }
            }

            var item = new HistoryItem
            {
                Id = Guid.NewGuid().ToString(),
                Path = path,
                Name = name,
                Type = type,
                Timestamp = DateTime.Now
            };

            _history.Insert(0, item);

            // Limit history size
            while (_history.Count > _maxHistory)
            {
                _history.RemoveAt(_history.Count - 1);
            }
        }

        private bool PruneOldHistoryLocked()
        {
            int beforeCount = _history.Count;
            _history = _history.Where(item => (DateTime.Now - item.Timestamp).TotalDays <= _keepDays).ToList();
            return _history.Count != beforeCount;
        }

        public List<HistoryItem> GetHistory()
        {
            lock (_lock)
            {
                return _history.ToList();
            }
        }

        public bool RestoreLast()
        {
            HistoryItem? item = null;
            lock (_lock)
            {
                if (_history.Count > 0)
                {
                    item = _history[0];
                    _history.RemoveAt(0);
                    SaveHistoryLocked();
                }
            }

            if (item != null)
            {
                return RestoreItemInternal(item);
            }
            return false;
        }

        public bool RestoreItem(string id)
        {
            HistoryItem? item = null;
            lock (_lock)
            {
                item = _history.FirstOrDefault(x => x.Id == id);
                if (item != null)
                {
                    _history.Remove(item);
                    SaveHistoryLocked();
                }
            }

            if (item != null)
            {
                return RestoreItemInternal(item);
            }
            return false;
        }

        private bool RestoreItemInternal(HistoryItem item)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.Path,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to restore item: {ex.Message}");
            }
            return false;
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                _history.Clear();
                SaveHistoryLocked();
            }
        }

        public bool DeleteHistoryItem(string id)
        {
            lock (_lock)
            {
                var item = _history.FirstOrDefault(x => x.Id == id);
                if (item != null)
                {
                    _history.Remove(item);
                    SaveHistoryLocked();
                    return true;
                }
            }
            return false;
        }

        private void LoadHistory()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_historyFilePath))
                    {
                        string json = File.ReadAllText(_historyFilePath);
                        _history = JsonSerializer.Deserialize<List<HistoryItem>>(json) ?? new List<HistoryItem>();
                        PruneOldHistoryLocked();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load history: {ex.Message}");
                    _history = new List<HistoryItem>();
                }
            }
        }

        private void SaveHistoryLocked()
        {
            try
            {
                if (!Directory.Exists(_historyDir))
                {
                    Directory.CreateDirectory(_historyDir);
                }

                string json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save history: {ex.Message}");
            }
        }
    }
}
