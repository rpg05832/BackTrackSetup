using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace BackTrack
{
    /// <summary>
    /// A single member of a workspace group: a folder, an application, or a document/file.
    /// </summary>
    public class SnapshotItem
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "folder"; // "folder" | "app" | "file"
    }

    public class WorkspaceSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";

        // New rich model: folders + apps + documents.
        public List<SnapshotItem> Items { get; set; } = new List<SnapshotItem>();

        // Legacy field (folder paths only). Kept so old saved groups still load,
        // and mirrored on save for backward compatibility.
        public List<string> Paths { get; set; } = new List<string>();

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class SnapshotManager
    {
        private readonly string _filePath;
        private readonly string _dirPath;
        private List<WorkspaceSnapshot> _snapshots = new List<WorkspaceSnapshot>();
        private readonly object _lock = new object();

        public SnapshotManager()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _dirPath = Path.Combine(userProfile, ".backtrack");
            _filePath = Path.Combine(_dirPath, "snapshots.json");
            LoadSnapshots();
        }

        public List<WorkspaceSnapshot> GetSnapshots()
        {
            lock (_lock)
            {
                return _snapshots.ToList();
            }
        }

        /// <summary>Returns the items of a group by id (migrating legacy folder paths if needed).</summary>
        public List<SnapshotItem> GetItems(string id)
        {
            lock (_lock)
            {
                var s = _snapshots.FirstOrDefault(x => x.Id == id);
                if (s == null) return new List<SnapshotItem>();
                if (s.Items != null && s.Items.Count > 0) return s.Items.ToList();
                return (s.Paths ?? new List<string>())
                    .Select(p => new SnapshotItem { Path = p, Name = SafeFolderName(p), Type = "folder" }).ToList();
            }
        }

        /// <summary>
        /// Captures the current workspace: all open Explorer folders AND all open applications.
        /// </summary>
        public bool CreateSnapshot(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"קבוצה מ-{DateTime.Now:dd/MM/yyyy HH:mm}";
            }

            var items = new List<SnapshotItem>();

            // 1. Open folders (Explorer windows)
            foreach (var p in GetOpenExplorerPaths())
            {
                items.Add(new SnapshotItem { Path = p, Name = SafeFolderName(p), Type = "folder" });
            }

            // 2. Open applications (one entry per executable)
            foreach (var app in GetOpenApps())
            {
                items.Add(app);
            }

            if (items.Count == 0) return false;

            lock (_lock)
            {
                var snapshot = new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Items = items,
                    Paths = items.Where(i => i.Type == "folder").Select(i => i.Path).ToList(),
                    Timestamp = DateTime.Now
                };
                _snapshots.Insert(0, snapshot);
                SaveSnapshotsLocked();
            }
            return true;
        }

        /// <summary>
        /// Creates a group from a specific set of window handles selected in the UI.
        /// Explorer windows are saved as their folder; other windows as their application.
        /// </summary>
        public bool CreateSnapshotFromHandles(string name, List<long> handles)
        {
            if (handles == null || handles.Count == 0) return false;
            if (string.IsNullOrWhiteSpace(name)) name = $"בחירה מ-{DateTime.Now:dd/MM/yyyy HH:mm}";

            var folderByHwnd = new Dictionary<long, string>();
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
                                dynamic w = windows.Item(i);
                                if (w == null) continue;
                                string fullName = w.FullName;
                                if (fullName.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    long hwnd = (long)w.HWND;
                                    string path = w.Document.Folder.Self.Path;
                                    if (!string.IsNullOrEmpty(path)) folderByHwnd[hwnd] = path;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"shell map error: {ex.Message}"); }

            var items = new List<SnapshotItem>();
            var seenApp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var h in handles)
            {
                if (folderByHwnd.TryGetValue(h, out var fp))
                {
                    if (seenFolder.Add(fp)) items.Add(new SnapshotItem { Path = fp, Name = SafeFolderName(fp), Type = "folder" });
                    continue;
                }
                try
                {
                    Win32.GetWindowThreadProcessId(new IntPtr(h), out uint pid);
                    if (pid == 0) continue;
                    using var proc = Process.GetProcessById((int)pid);
                    string exe = proc.MainModule?.FileName ?? "";
                    if (string.IsNullOrEmpty(exe)) continue;
                    string pn = Path.GetFileName(exe).ToLowerInvariant();
                    if (pn is "explorer.exe" or "backtrack.exe" or "החזר פעולה במחשב.exe") continue;
                    if (seenApp.Add(exe)) items.Add(new SnapshotItem { Path = exe, Name = Path.GetFileNameWithoutExtension(exe), Type = "app" });
                }
                catch { }
            }

            if (items.Count == 0) return false;
            lock (_lock)
            {
                _snapshots.Insert(0, new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Items = items,
                    Paths = items.Where(i => i.Type == "folder").Select(i => i.Path).ToList(),
                    Timestamp = DateTime.Now
                });
                SaveSnapshotsLocked();
            }
            return true;
        }

        /// <summary>
        /// Adds extra items (typically documents/files the user picked) to an existing group.
        /// </summary>
        public bool AddItems(string id, List<SnapshotItem>? newItems)
        {
            if (newItems == null || newItems.Count == 0) return false;

            lock (_lock)
            {
                var s = _snapshots.FirstOrDefault(x => x.Id == id);
                if (s == null) return false;

                s.Items ??= new List<SnapshotItem>();

                foreach (var it in newItems)
                {
                    if (it == null || string.IsNullOrWhiteSpace(it.Path)) continue;
                    if (s.Items.Any(e => string.Equals(e.Path, it.Path, StringComparison.OrdinalIgnoreCase))) continue;

                    if (string.IsNullOrWhiteSpace(it.Name)) it.Name = SafeFolderName(it.Path);
                    if (string.IsNullOrWhiteSpace(it.Type)) it.Type = "file";
                    s.Items.Add(it);
                }

                s.Paths = s.Items.Where(i => i.Type == "folder").Select(i => i.Path).ToList();
                SaveSnapshotsLocked();
            }
            return true;
        }

        /// <summary>
        /// Removes a single item (by path) from a group.
        /// </summary>
        public bool RemoveItem(string id, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            lock (_lock)
            {
                var s = _snapshots.FirstOrDefault(x => x.Id == id);
                if (s?.Items == null) return false;

                int removed = s.Items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    s.Paths = s.Items.Where(i => i.Type == "folder").Select(i => i.Path).ToList();
                    SaveSnapshotsLocked();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Opens every item in a group: folders in Explorer, apps launched, documents in their default app.
        /// </summary>
        public bool RestoreSnapshot(string id)
        {
            WorkspaceSnapshot? snapshot = null;
            lock (_lock)
            {
                snapshot = _snapshots.FirstOrDefault(x => x.Id == id);
            }

            if (snapshot == null) return false;

            var items = (snapshot.Items != null && snapshot.Items.Count > 0)
                ? snapshot.Items
                : snapshot.Paths.Select(p => new SnapshotItem { Path = p, Name = SafeFolderName(p), Type = "folder" }).ToList();

            bool success = false;
            foreach (var item in items)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.Path,
                        UseShellExecute = true
                    });
                    success = true;
                    // Small stagger so the windows open in a predictable order.
                    System.Threading.Thread.Sleep(120);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open snapshot item {item.Path}: {ex.Message}");
                }
            }
            return success;
        }

        public bool DeleteSnapshot(string id)
        {
            lock (_lock)
            {
                var snapshot = _snapshots.FirstOrDefault(x => x.Id == id);
                if (snapshot != null)
                {
                    _snapshots.Remove(snapshot);
                    SaveSnapshotsLocked();
                    return true;
                }
            }
            return false;
        }

        private static string SafeFolderName(string p)
        {
            try
            {
                var n = Path.GetFileName(p.TrimEnd('\\', '/'));
                return string.IsNullOrEmpty(n) ? p : n;
            }
            catch
            {
                return p;
            }
        }

        private List<string> GetOpenExplorerPaths()
        {
            var paths = new List<string>();
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
                                    if (!string.IsNullOrEmpty(path))
                                    {
                                        paths.Add(path);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get open Explorer paths for snapshot: {ex.Message}");
            }
            return paths.Distinct().ToList();
        }

        /// <summary>
        /// Returns one entry per open application executable (deduplicated), skipping the shell and BackTrack itself.
        /// </summary>
        private List<SnapshotItem> GetOpenApps()
        {
            var apps = new List<SnapshotItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Win32.EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (!Win32.IsWindowVisible(hWnd)) return true;

                        int length = Win32.GetWindowTextLength(hWnd);
                        if (length == 0) return true;

                        IntPtr owner = Win32.GetWindow(hWnd, Win32.GW_OWNER);
                        int exStyle = Win32.GetWindowLong(hWnd, Win32.GWL_EXSTYLE);
                        if (owner != IntPtr.Zero || (exStyle & Win32.WS_EX_TOOLWINDOW) != 0) return true;

                        if (Win32.DwmGetWindowAttribute(hWnd, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                            return true;

                        Win32.GetWindowThreadProcessId(hWnd, out uint pid);
                        if (pid == 0) return true;

                        using var proc = Process.GetProcessById((int)pid);
                        string exePath = proc.MainModule?.FileName ?? "";
                        if (string.IsNullOrEmpty(exePath)) return true;

                        string procName = Path.GetFileName(exePath).ToLowerInvariant();
                        if (procName is "explorer.exe" or "החזר פעולה במחשב.exe" or "backtrack.exe"
                            or "shellexperiencehost.exe" or "searchhost.exe" or "applicationframehost.exe"
                            or "textinputhost.exe" or "taskhostw.exe" or "ctfmon.exe" or "systemsettings.exe")
                        {
                            return true;
                        }

                        if (seen.Add(exePath))
                        {
                            apps.Add(new SnapshotItem
                            {
                                Path = exePath,
                                Name = Path.GetFileNameWithoutExtension(exePath),
                                Type = "app"
                            });
                        }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetOpenApps error: {ex.Message}");
            }
            return apps;
        }

        private void LoadSnapshots()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        string json = File.ReadAllText(_filePath);
                        _snapshots = JsonSerializer.Deserialize<List<WorkspaceSnapshot>>(json) ?? new List<WorkspaceSnapshot>();

                        // Migrate legacy groups (folder paths only) into the new Items model.
                        foreach (var s in _snapshots)
                        {
                            s.Items ??= new List<SnapshotItem>();
                            s.Paths ??= new List<string>();
                            if (s.Items.Count == 0 && s.Paths.Count > 0)
                            {
                                s.Items = s.Paths
                                    .Select(p => new SnapshotItem { Path = p, Name = SafeFolderName(p), Type = "folder" })
                                    .ToList();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load snapshots: {ex.Message}");
                    _snapshots = new List<WorkspaceSnapshot>();
                }
            }
        }

        private void SaveSnapshotsLocked()
        {
            try
            {
                if (!Directory.Exists(_dirPath))
                {
                    Directory.CreateDirectory(_dirPath);
                }

                string json = JsonSerializer.Serialize(_snapshots, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save snapshots: {ex.Message}");
            }
        }
    }
}
