using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;

namespace BackTrack
{
    public class ClipboardItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class ClipboardMonitor : IDisposable
    {
        private readonly string _filePath;
        private readonly string _dirPath;
        private List<ClipboardItem> _history = new List<ClipboardItem>();
        private string _lastContent = "";
        private readonly object _lock = new object();
        private bool _isPrivateMode = false;
        private readonly ClipboardListenerWindow _listenerWindow;

        public bool IsPrivateMode
        {
            get => _isPrivateMode;
            set
            {
                lock (_lock)
                {
                    _isPrivateMode = value;
                }
            }
        }

        public ClipboardMonitor()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _dirPath = Path.Combine(userProfile, ".backtrack");
            _filePath = Path.Combine(_dirPath, "clipboard.json");
            LoadHistory();

            // Create the hidden native window to listen to clipboard updates
            _listenerWindow = new ClipboardListenerWindow(this);
        }

        public void OnClipboardChanged()
        {
            lock (_lock)
            {
                if (_isPrivateMode) return;
            }

            // Called on the main thread (STA), so we can read the clipboard directly
            string currentText = "";
            try
            {
                if (Clipboard.ContainsText())
                {
                    currentText = Clipboard.GetText();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read clipboard on event: {ex.Message}");
                return;
            }

            if (string.IsNullOrWhiteSpace(currentText)) return;

            // Limit maximum length stored in history to prevent huge memory bloat (e.g. 50KB limit per clip)
            if (currentText.Length > 50000)
            {
                currentText = currentText.Substring(0, 50000) + "... [טקסט מקוצר עקב גודלו]";
            }

            lock (_lock)
            {
                if (currentText != _lastContent)
                {
                    _lastContent = currentText;

                    // Remove existing item to move it to the top (de-duplication)
                    var existing = _history.FirstOrDefault(x => x.Content == currentText);
                    if (existing != null)
                    {
                        _history.Remove(existing);
                    }

                    _history.Insert(0, new ClipboardItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = currentText,
                        Timestamp = DateTime.Now
                    });

                    // Limit clipboard history size to 100 items
                    while (_history.Count > 100)
                    {
                        _history.RemoveAt(_history.Count - 1);
                    }

                    SaveHistoryLocked();
                }
            }
        }

        public bool CopyToClipboard(string id)
        {
            string? content = null;
            lock (_lock)
            {
                var item = _history.FirstOrDefault(x => x.Id == id);
                if (item != null)
                {
                    content = item.Content;
                }
            }

            if (string.IsNullOrEmpty(content)) return false;

            bool success = false;
            // WebServer calls this from a threadpool thread (MTA), so we must use an STA thread to write to Clipboard safely
            var thread = new Thread(() =>
            {
                try
                {
                    Clipboard.SetText(content);
                    success = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to write clipboard in STA thread: {ex.Message}");
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(200))
            {
                try { thread.Interrupt(); } catch { }
            }

            if (success)
            {
                lock (_lock)
                {
                    _lastContent = content; // Prevent re-adding it to history as a new copy
                }
            }
            return success;
        }

        public List<ClipboardItem> GetHistory()
        {
            lock (_lock)
            {
                return _history.ToList();
            }
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                _history.Clear();
                _lastContent = "";
                SaveHistoryLocked();
            }
        }

        public bool DeleteItem(string id)
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
                    if (File.Exists(_filePath))
                    {
                        string json = File.ReadAllText(_filePath);
                        _history = JsonSerializer.Deserialize<List<ClipboardItem>>(json) ?? new List<ClipboardItem>();
                        if (_history.Count > 0)
                        {
                            _lastContent = _history[0].Content;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load clipboard history: {ex.Message}");
                    _history = new List<ClipboardItem>();
                }
            }
        }

        private void SaveHistoryLocked()
        {
            try
            {
                if (!Directory.Exists(_dirPath))
                {
                    Directory.CreateDirectory(_dirPath);
                }

                string json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save clipboard history: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _listenerWindow.Dispose();
        }

        /// <summary>
        /// Hidden window to hook system clipboard updates.
        /// </summary>
        private class ClipboardListenerWindow : NativeWindow, IDisposable
        {
            private readonly ClipboardMonitor _monitor;

            public ClipboardListenerWindow(ClipboardMonitor monitor)
            {
                _monitor = monitor;
                var cp = new CreateParams
                {
                    Style = 0, // WS_POPUP
                    ExStyle = 0x00000080 // WS_EX_TOOLWINDOW
                };
                CreateHandle(cp);
                Win32.AddClipboardFormatListener(Handle);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Win32.WM_CLIPBOARDUPDATE)
                {
                    _monitor.OnClipboardChanged();
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                {
                    Win32.RemoveClipboardFormatListener(Handle);
                    DestroyHandle();
                }
            }
        }
    }
}
