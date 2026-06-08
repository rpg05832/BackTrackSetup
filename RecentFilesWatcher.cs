using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

namespace BackTrack
{
    public class RecentFilesWatcher
    {
        private FileSystemWatcher? _watcher;
        private readonly Action<string, string> _onFileClosedProxy; // Callback: (filePath, fileName)
        private readonly string _recentFolder;

        public RecentFilesWatcher(Action<string, string> onFileClosedProxy)
        {
            _onFileClosedProxy = onFileClosedProxy;
            _recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        }

        public void Start()
        {
            try
            {
                if (!Directory.Exists(_recentFolder))
                {
                    Debug.WriteLine("Recent folder does not exist.");
                    return;
                }

                _watcher = new FileSystemWatcher(_recentFolder)
                {
                    Filter = "*.lnk",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnRecentChanged;
                _watcher.Changed += OnRecentChanged;

                Debug.WriteLine($"Recent files watcher started on {_recentFolder}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start Recent files watcher: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
        }

        private void OnRecentChanged(object sender, FileSystemEventArgs e)
        {
            // Run asynchronously to not block the FileSystemWatcher thread
            Task.Run(async () =>
            {
                try
                {
                    // Wait 200ms to ensure the OS has finished writing the .lnk file
                    await Task.Delay(200);

                    if (!File.Exists(e.FullPath)) return;

                    string? targetPath = ResolveShortcut(e.FullPath);
                    if (string.IsNullOrEmpty(targetPath)) return;

                    // Filter targets:
                    // 1. Must be a file that exists (not a directory)
                    // 2. Ignore executables (.exe) and batch files (.bat, .cmd)
                    // 3. Ignore other shortcut targets
                    if (File.Exists(targetPath) && !Directory.Exists(targetPath))
                    {
                        string ext = Path.GetExtension(targetPath).ToLowerInvariant();
                        if (ext != ".exe" && ext != ".lnk" && ext != ".bat" && ext != ".cmd" && ext != ".dll")
                        {
                            string fileName = Path.GetFileName(targetPath);
                            _onFileClosedProxy(targetPath, fileName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing recent shortcut change: {ex.Message}");
                }
            });
        }

        private string? ResolveShortcut(string lnkPath)
        {
            try
            {
                Type? wshShellType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshShellType == null) return null;

                dynamic? wshShell = Activator.CreateInstance(wshShellType);
                if (wshShell == null) return null;

                dynamic shortcut = wshShell.CreateShortcut(lnkPath);
                string target = shortcut.TargetPath;
                
                return target;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"COM error resolving shortcut {lnkPath}: {ex.Message}");
                return null;
            }
        }
    }
}
