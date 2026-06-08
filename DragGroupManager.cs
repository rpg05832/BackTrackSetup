using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace BackTrack
{
    /// <summary>
    /// Detects when the user drags one window's title bar onto another window and merges them
    /// into a tabbed group (Groupy-style). Uses a WinEvent hook for window move start/end.
    /// </summary>
    public static class DragGroupManager
    {
        private static Win32.WinEventDelegate? _proc; // kept referenced so it is not GC'd
        private static IntPtr _hook = IntPtr.Zero;
        private static IntPtr _dragged = IntPtr.Zero;

        // When true, dropping a window's title bar onto another window merges them.
        public static bool Enabled = true;

        // Only merge if the drop point is within this many pixels of the target's top (title-bar band).
        private const int TitleBandHeight = 38;

        public static void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = WinEventProc;
            _hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_MOVESIZESTART, Win32.EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero, _proc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        }

        public static void Stop()
        {
            if (_hook != IntPtr.Zero) { try { Win32.UnhookWinEvent(_hook); } catch { } _hook = IntPtr.Zero; }
            _proc = null;
        }

        private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (idObject != Win32.OBJID_WINDOW) return;
                if (eventType == Win32.EVENT_SYSTEM_MOVESIZESTART)
                {
                    _dragged = hwnd;
                }
                else if (eventType == Win32.EVENT_SYSTEM_MOVESIZEEND)
                {
                    var dragged = _dragged;
                    _dragged = IntPtr.Zero;
                    if (!Enabled) return;
                    if (dragged == IntPtr.Zero || dragged != hwnd) return;
                    HandleDrop(dragged);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"WinEventProc: {ex.Message}"); }
        }

        private static void HandleDrop(IntPtr dragged)
        {
            if (!IsMergeable(dragged)) return;

            Win32.GetCursorPos(out var pt);
            IntPtr under = Win32.GetAncestor(Win32.WindowFromPoint(pt), Win32.GA_ROOT);
            if (under == IntPtr.Zero || under == dragged) return;

            // Dropped onto an existing group container -> add as a new tab.
            var existingGroup = DockHost.TryGetByHandle(under);
            if (existingGroup != null)
            {
                existingGroup.AddExternal(dragged);
                return;
            }

            if (!IsMergeable(under)) return;

            // Require the drop to land near the target's title bar (avoids accidental merges).
            if (!Win32.GetWindowRect(under, out var r)) return;
            if (pt.Y > r.Top + TitleBandHeight) return;

            DockHost.RequestMerge(new List<long> { (long)under, (long)dragged });
        }

        private static bool IsMergeable(IntPtr h)
        {
            try
            {
                if (h == IntPtr.Zero || !Win32.IsWindowVisible(h)) return false;
                IntPtr owner = Win32.GetWindow(h, Win32.GW_OWNER);
                int ex = Win32.GetWindowLong(h, Win32.GWL_EXSTYLE);
                if (owner != IntPtr.Zero || (ex & Win32.WS_EX_TOOLWINDOW) != 0) return false;
                if (Win32.GetWindowTextLength(h) == 0) return false;

                var cls = new StringBuilder(256);
                Win32.GetClassName(h, cls, cls.Capacity);
                string c = cls.ToString();
                if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow") return false;

                Win32.GetWindowThreadProcessId(h, out uint pid);
                if (pid == 0) return false;
                using var p = Process.GetProcessById((int)pid);
                string pn = p.ProcessName.ToLowerInvariant();
                if (pn is "backtrack" or "החזר פעולה במחשב") return false;
                return true;
            }
            catch { return false; }
        }
    }
}
