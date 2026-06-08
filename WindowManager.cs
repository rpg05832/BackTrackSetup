using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace BackTrack
{
    /// <summary>
    /// Lightweight description of an open, arrangeable top-level window.
    /// Handle is the native HWND serialized as a string so it survives JSON / query round-trips.
    /// </summary>
    public class WindowInfo
    {
        public string Handle { get; set; } = "";
        public string Title { get; set; } = "";
        public string ProcessName { get; set; } = "";
    }

    /// <summary>
    /// Enumerates open windows and arranges (snaps/tiles) them into side-by-side layouts.
    /// </summary>
    public class WindowManager
    {
        // Visual gap (in pixels) left between tiled windows and the screen edges.
        private const int Gap = 8;

        /// <summary>
        /// Returns the currently visible, top-level application windows that can be arranged.
        /// Filters out the desktop, taskbar, hidden/cloaked windows and BackTrack itself.
        /// </summary>
        public List<WindowInfo> GetOpenWindows()
        {
            var result = new List<WindowInfo>();
            try
            {
                Win32.EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (!Win32.IsWindowVisible(hWnd)) return true;

                        int length = Win32.GetWindowTextLength(hWnd);
                        if (length == 0) return true;

                        // Skip owned windows and tool windows (dialogs, popups, palettes).
                        IntPtr owner = Win32.GetWindow(hWnd, Win32.GW_OWNER);
                        int exStyle = Win32.GetWindowLong(hWnd, Win32.GWL_EXSTYLE);
                        if (owner != IntPtr.Zero || (exStyle & Win32.WS_EX_TOOLWINDOW) != 0) return true;

                        // Skip cloaked windows (invisible UWP background windows).
                        if (Win32.DwmGetWindowAttribute(hWnd, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                            return true;

                        // Skip shell windows (desktop / taskbar) by class name.
                        var cls = new StringBuilder(256);
                        Win32.GetClassName(hWnd, cls, cls.Capacity);
                        string className = cls.ToString();
                        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow")
                            return true;

                        var sb = new StringBuilder(length + 1);
                        Win32.GetWindowText(hWnd, sb, sb.Capacity);
                        string title = sb.ToString();
                        if (string.IsNullOrWhiteSpace(title)) return true;

                        Win32.GetWindowThreadProcessId(hWnd, out uint pid);
                        if (pid == 0) return true;

                        string procName = "";
                        try
                        {
                            using var proc = Process.GetProcessById((int)pid);
                            procName = proc.ProcessName;
                        }
                        catch { }

                        string lower = procName.ToLowerInvariant();
                        if (lower is "החזר פעולה במחשב" or "backtrack")
                            return true;

                        result.Add(new WindowInfo
                        {
                            Handle = ((long)hWnd).ToString(),
                            Title = title,
                            ProcessName = procName
                        });
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetOpenWindows error: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Arranges the given window handles (in order) according to the named layout.
        /// Returns how many windows were successfully placed.
        /// </summary>
        public int ArrangeLayout(string layout, List<long> handles)
        {
            if (handles == null || handles.Count == 0) return 0;

            Rectangle area = GetWorkArea();
            var rects = ComputeRects(layout, handles.Count, area);

            int placed = 0;
            for (int i = 0; i < handles.Count && i < rects.Count; i++)
            {
                if (PlaceWindow(new IntPtr(handles[i]), rects[i])) placed++;
            }
            return placed;
        }

        /// <summary>
        /// Arranges every currently open window using the given layout.
        /// </summary>
        public int TileAll(string layout)
        {
            var handles = GetOpenWindows()
                .Select(w => long.TryParse(w.Handle, out var h) ? h : 0L)
                .Where(h => h != 0)
                .ToList();
            return ArrangeLayout(layout, handles);
        }

        /// <summary>
        /// EXPERIMENTAL: merges the given windows into one tabbed container window (reparenting).
        /// </summary>
        public int MergeWindows(List<long> handles)
        {
            if (handles == null || handles.Count == 0) return 0;
            DockHost.RequestMerge(handles);
            return handles.Count;
        }

        private Rectangle GetWorkArea()
        {
            try
            {
                return Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            }
            catch
            {
                return new Rectangle(0, 0, 1280, 720);
            }
        }

        /// <summary>
        /// Computes the target rectangle for each window for a given layout.
        /// </summary>
        private List<Rectangle> ComputeRects(string layout, int count, Rectangle a)
        {
            var list = new List<Rectangle>();
            layout = (layout ?? "grid").ToLowerInvariant();

            switch (layout)
            {
                // Equal vertical columns (2 windows = classic side-by-side).
                case "left-right":
                case "side-by-side":
                case "halves":
                case "columns":
                {
                    int w = a.Width / count;
                    for (int i = 0; i < count; i++)
                        list.Add(new Rectangle(a.Left + i * w, a.Top, w, a.Height));
                    break;
                }

                // Equal horizontal rows (stacked).
                case "rows":
                case "stacked":
                {
                    int h = a.Height / count;
                    for (int i = 0; i < count; i++)
                        list.Add(new Rectangle(a.Left, a.Top + i * h, a.Width, h));
                    break;
                }

                // One large main window on the left + the rest stacked on the right.
                case "main-side":
                {
                    if (count == 1) { list.Add(a); break; }
                    int mainW = (int)(a.Width * 0.6);
                    list.Add(new Rectangle(a.Left, a.Top, mainW, a.Height));
                    int rest = count - 1;
                    int rh = a.Height / rest;
                    int sideX = a.Left + mainW;
                    int sideW = a.Width - mainW;
                    for (int i = 0; i < rest; i++)
                        list.Add(new Rectangle(sideX, a.Top + i * rh, sideW, rh));
                    break;
                }

                // Auto grid (default): as square as possible.
                case "grid":
                default:
                {
                    int cols = (int)Math.Ceiling(Math.Sqrt(count));
                    int rows = (int)Math.Ceiling((double)count / cols);
                    int w = a.Width / cols;
                    int h = a.Height / rows;
                    for (int i = 0; i < count; i++)
                    {
                        int r = i / cols;
                        int c = i % cols;
                        list.Add(new Rectangle(a.Left + c * w, a.Top + r * h, w, h));
                    }
                    break;
                }
            }
            return list;
        }

        /// <summary>
        /// Restores (un-minimizes / un-maximizes) the window then moves+resizes it into the target rectangle,
        /// leaving a small gap so the tiled windows look clean.
        /// </summary>
        private bool PlaceWindow(IntPtr hWnd, Rectangle r)
        {
            try
            {
                if (Win32.IsZoomed(hWnd) || Win32.IsIconic(hWnd))
                {
                    Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
                }

                int x = r.Left + Gap;
                int y = r.Top + Gap;
                int w = Math.Max(120, r.Width - Gap * 2);
                int h = Math.Max(80, r.Height - Gap * 2);

                Win32.SetWindowPos(hWnd, IntPtr.Zero, x, y, w, h,
                    Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PlaceWindow error: {ex.Message}");
                return false;
            }
        }
    }
}
