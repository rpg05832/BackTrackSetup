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
    /// Groupy-style container with a CUSTOM (borderless) chrome: the tabs sit in the title-bar area,
    /// next to a "+" menu and the window buttons (minimize / maximize / close). Real windows are
    /// reparented into the area below the caption. Closing (or exiting BackTrack) restores them.
    /// </summary>
    public class DockHost : Form
    {
        public static Action<Action>? UiInvoke;
        private static readonly List<DockHost> _open = new List<DockHost>();
        private static readonly object _sync = new object();

        private class Child
        {
            public IntPtr Handle;
            public long OriginalStyle;
            public Win32.RECT OriginalRect;
            public string Title = "חלון";
            public Image? IconImg;
            public Rectangle TabRect;
            public Rectangle CloseRect;
        }

        private readonly List<long> _pending;
        private readonly List<Child> _children = new List<Child>();
        private int _active = 0;
        private bool _restored = false;

        // layout
        private const int CaptionH = 34;
        private const int TabMaxW = 210;
        private const int BtnW = 46;
        private Rectangle _plusRect, _minRect, _maxRect, _closeRect;

        // tab drag state
        private int _pressedTab = -1;
        private Point _pressStart;
        private bool _dragging = false;

        // win32 chrome constants
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12,
                          HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        private const int EdgeB = 6;

        public static void RequestMerge(List<long> handles)
        {
            if (handles == null || handles.Count == 0) return;
            Action act = () =>
            {
                try { var h = new DockHost(handles); lock (_sync) { _open.Add(h); } h.Show(); }
                catch (Exception ex) { Debug.WriteLine($"DockHost create: {ex.Message}"); }
            };
            if (UiInvoke != null) UiInvoke(act); else act();
        }

        public static void RestoreAll()
        {
            List<DockHost> copy; lock (_sync) { copy = _open.ToList(); }
            foreach (var h in copy) { try { h.RestoreChildren(); } catch { } }
        }

        public static DockHost? TryGetByHandle(IntPtr h)
        {
            lock (_sync) { return _open.FirstOrDefault(d => d.IsHandleCreated && d.Handle == h); }
        }

        private DockHost(List<long> handles)
        {
            _pending = handles;
            Text = "BackTrack — קבוצת לשוניות";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1040, 700);
            MinimumSize = new Size(560, 360);
            DoubleBuffered = true;
            BackColor = Color.FromArgb(243, 243, 243);
            KeyPreview = true;
            ShowInTaskbar = true;

            try
            {
                using var st = typeof(Program).Assembly.GetManifestResourceStream("BackTrack.backtrack_icon.ico");
                if (st != null) Icon = new Icon(st);
            }
            catch { }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            MouseDown += DockHost_MouseDown;
            MouseMove += DockHost_MouseMove;
            MouseUp += DockHost_MouseUp;
            Shown += (s, e) => { UpdateMaxBounds(); AttachAll(); };
            Resize += (s, e) => { UpdateMaxBounds(); LayoutActive(); Invalidate(CaptionStrip()); };
            FormClosing += (s, e) => RestoreChildren();
        }

        private void UpdateMaxBounds()
        {
            try { var scr = Screen.FromControl(this); MaximizedBounds = scr.WorkingArea; } catch { }
        }

        private Rectangle CaptionStrip() => new Rectangle(0, 0, ClientSize.Width, CaptionH);
        private Rectangle ContentRect() => new Rectangle(0, CaptionH, ClientSize.Width, Math.Max(0, ClientSize.Height - CaptionH));

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab)) { Cycle(1); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { Cycle(-1); return true; }
            if (keyData == (Keys.Control | Keys.W)) { if (_children.Count > 0) DetachChild(_children[_active]); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Cycle(int dir)
        {
            if (_children.Count == 0) return;
            SetActive((_active + dir + _children.Count) % _children.Count);
        }

        private void AttachAll()
        {
            foreach (var raw in _pending)
            {
                IntPtr h = new IntPtr(raw);
                if (h == IntPtr.Zero || h == Handle) continue;
                try { AttachChild(h); } catch (Exception ex) { Debug.WriteLine($"attach: {ex.Message}"); }
            }
            if (_children.Count == 0)
            {
                MessageBox.Show("לא ניתן היה למזג את החלונות שנבחרו.", "BackTrack", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close(); return;
            }
            SetActive(0);
            Invalidate();
        }

        private bool Has(IntPtr h) => _children.Any(c => c.Handle == h);

        public void AddExternal(IntPtr h)
        {
            if (InvokeRequired) { try { BeginInvoke(new Action(() => AddExternal(h))); } catch { } return; }
            try
            {
                AttachChild(h);
                SetActive(_children.Count - 1);
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate(); Invalidate();
            }
            catch (Exception ex) { Debug.WriteLine($"AddExternal: {ex.Message}"); }
        }

        private void AttachChild(IntPtr h)
        {
            if (Has(h)) return;
            int len = Win32.GetWindowTextLength(h);
            var sb = new StringBuilder(len + 1);
            Win32.GetWindowText(h, sb, sb.Capacity);
            string title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) title = "חלון";

            var c = new Child { Handle = h, Title = title };
            c.OriginalStyle = Win32.GetWindowLongValue(h, Win32.GWL_STYLE);
            Win32.GetWindowRect(h, out c.OriginalRect);
            c.IconImg = LoadIcon(h);

            Win32.SetParent(h, Handle);
            long ns = (c.OriginalStyle & ~(Win32.WS_POPUP | Win32.WS_CAPTION | Win32.WS_THICKFRAME)) | Win32.WS_CHILD | Win32.WS_VISIBLE;
            Win32.SetWindowLongValue(h, Win32.GWL_STYLE, ns);
            var area = ContentRect();
            Win32.SetWindowPos(h, IntPtr.Zero, area.Left, area.Top, area.Width, area.Height,
                Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
            _children.Add(c);
        }

        private static Image? LoadIcon(IntPtr h)
        {
            try
            {
                Win32.GetWindowThreadProcessId(h, out uint pid);
                if (pid == 0) return null;
                using var p = Process.GetProcessById((int)pid);
                string exe = p.MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exe)) return null;
                using var ico = Icon.ExtractAssociatedIcon(exe);
                return ico == null ? null : new Bitmap(ico.ToBitmap(), new Size(16, 16));
            }
            catch { return null; }
        }

        private void SetActive(int index)
        {
            if (_children.Count == 0) return;
            _active = Math.Max(0, Math.Min(index, _children.Count - 1));
            var area = ContentRect();
            for (int i = 0; i < _children.Count; i++)
            {
                var c = _children[i];
                if (i == _active)
                {
                    Win32.SetWindowPos(c.Handle, IntPtr.Zero, area.Left, area.Top, area.Width, area.Height,
                        Win32.SWP_NOZORDER | Win32.SWP_SHOWWINDOW);
                    Win32.ShowWindow(c.Handle, Win32.SW_SHOW);
                }
                else
                {
                    Win32.ShowWindow(c.Handle, Win32.SW_HIDE);
                }
            }
            Invalidate(CaptionStrip());
        }

        private void LayoutActive()
        {
            if (_children.Count == 0) return;
            var area = ContentRect();
            var c = _children[_active];
            try { Win32.MoveWindow(c.Handle, area.Left, area.Top, area.Width, area.Height, true); } catch { }
        }

        // ---------------- Painting the caption (tabs + buttons) ----------------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            try
            {
                using (var capBg = new SolidBrush(Color.FromArgb(228, 229, 233)))
                    g.FillRectangle(capBg, CaptionStrip());

                // window buttons at the right
                _closeRect = new Rectangle(ClientSize.Width - BtnW, 0, BtnW, CaptionH);
                _maxRect = new Rectangle(ClientSize.Width - BtnW * 2, 0, BtnW, CaptionH);
                _minRect = new Rectangle(ClientSize.Width - BtnW * 3, 0, BtnW, CaptionH);
                DrawButton(g, _minRect, "–");              // minimize –
                DrawButton(g, _maxRect, WindowState == FormWindowState.Maximized ? "❐" : "□"); // restore/maximize
                DrawButton(g, _closeRect, "✕", true);      // close ✕

                // tabs
                int x = 4;
                int tabsRight = _minRect.Left - 34;
                for (int i = 0; i < _children.Count; i++)
                {
                    var c = _children[i];
                    int tw = Math.Min(TabMaxW, Math.Max(120, (tabsRight - 4) / Math.Max(1, _children.Count)));
                    if (x + tw > tabsRight) tw = Math.Max(80, tabsRight - x);
                    c.TabRect = new Rectangle(x, 4, tw, CaptionH - 4);
                    bool act = i == _active;
                    using (var tb = new SolidBrush(act ? Color.FromArgb(99, 102, 241) : Color.FromArgb(210, 212, 219)))
                        g.FillRectangle(tb, c.TabRect);

                    int ix = c.TabRect.Left + 8;
                    if (c.IconImg != null) { g.DrawImage(c.IconImg, new Rectangle(ix, c.TabRect.Top + (c.TabRect.Height - 16) / 2, 16, 16)); ix += 22; }
                    c.CloseRect = new Rectangle(c.TabRect.Right - 22, c.TabRect.Top + (c.TabRect.Height - 16) / 2, 16, 16);
                    var tr = new Rectangle(ix, c.TabRect.Top, c.CloseRect.Left - ix - 4, c.TabRect.Height);
                    TextRenderer.DrawText(g, c.Title, Font, tr, act ? Color.White : Color.FromArgb(40, 40, 40),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, "✕", Font, c.CloseRect, act ? Color.White : Color.FromArgb(80, 80, 80),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x += tw + 2;
                }

                // "+" button
                _plusRect = new Rectangle(Math.Min(x + 2, tabsRight - 28), 4, 26, CaptionH - 8);
                using (var pb = new SolidBrush(Color.FromArgb(210, 212, 219))) g.FillRectangle(pb, _plusRect);
                TextRenderer.DrawText(g, "+", new Font(Font.FontFamily, 12f, FontStyle.Bold), _plusRect, Color.FromArgb(40, 40, 40),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // content background (visible only if no child covers it)
                using (var cb = new SolidBrush(Color.FromArgb(243, 243, 243))) g.FillRectangle(cb, ContentRect());
            }
            catch (Exception ex) { Debug.WriteLine($"OnPaint: {ex.Message}"); }
        }

        private void DrawButton(Graphics g, Rectangle r, string glyph, bool danger = false)
        {
            TextRenderer.DrawText(g, glyph, Font, r, danger ? Color.FromArgb(200, 30, 30) : Color.FromArgb(60, 60, 60),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // ---------------- Mouse / hit testing ----------------
        private enum HitKind { None, Tab, TabClose, Plus, Min, Max, Close }

        private (HitKind kind, int index) HitTest(Point p)
        {
            if (_minRect.Contains(p)) return (HitKind.Min, -1);
            if (_maxRect.Contains(p)) return (HitKind.Max, -1);
            if (_closeRect.Contains(p)) return (HitKind.Close, -1);
            if (_plusRect.Contains(p)) return (HitKind.Plus, -1);
            for (int i = 0; i < _children.Count; i++)
            {
                if (_children[i].CloseRect.Contains(p)) return (HitKind.TabClose, i);
                if (_children[i].TabRect.Contains(p)) return (HitKind.Tab, i);
            }
            return (HitKind.None, -1);
        }

        private void DockHost_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var (kind, idx) = HitTest(e.Location);
            switch (kind)
            {
                case HitKind.Min: WindowState = FormWindowState.Minimized; break;
                case HitKind.Max: WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; LayoutActive(); Invalidate(); break;
                case HitKind.Close: Close(); break;
                case HitKind.Plus: ShowAddMenu(_plusRect); break;
                case HitKind.TabClose: DetachChild(_children[idx]); break;
                case HitKind.Tab:
                    SetActive(idx);
                    _pressedTab = idx; _pressStart = e.Location; _dragging = false;
                    break;
            }
        }

        private void DockHost_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_pressedTab < 0 || (e.Button & MouseButtons.Left) == 0) return;
            if (Math.Abs(e.X - _pressStart.X) > 14 || Math.Abs(e.Y - _pressStart.Y) > 14) _dragging = true;
        }

        private void DockHost_MouseUp(object? sender, MouseEventArgs e)
        {
            if (_dragging && _pressedTab >= 0 && _pressedTab < _children.Count)
            {
                Win32.GetCursorPos(out var pt);
                if (!Bounds.Contains(pt.X, pt.Y))
                    DetachChild(_children[_pressedTab], pt);
            }
            _pressedTab = -1; _dragging = false;
        }

        private void ShowAddMenu(Rectangle anchor)
        {
            var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
            menu.Items.Add("➕ הוסף חלונות", null, (s, e) => AddWindowsViaPicker());
            menu.Items.Add("📁 פתח תיקייה", null, (s, e) => AddFolderTab());
            menu.Show(this, new Point(anchor.Left, anchor.Bottom));
        }

        // ---------------- Add / detach ----------------
        private void AddWindowsViaPicker()
        {
            try
            {
                var existing = new HashSet<long>(_children.Select(c => (long)c.Handle)) { (long)Handle };
                var candidates = new WindowManager().GetOpenWindows()
                    .Where(w => long.TryParse(w.Handle, out var h) && !existing.Contains(h)).ToList();
                if (candidates.Count == 0)
                {
                    MessageBox.Show("אין כרגע חלונות נוספים להוספה.", "BackTrack", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var chosen = ShowWindowPicker(candidates);
                if (chosen == null) return;
                foreach (var h in chosen) { try { AttachChild(new IntPtr(h)); } catch { } }
                SetActive(_children.Count - 1); Invalidate();
            }
            catch (Exception ex) { Debug.WriteLine($"picker: {ex.Message}"); }
        }

        private void AddFolderTab()
        {
            try
            {
                string? folder;
                using (var dlg = new FolderBrowserDialog { Description = "בחר תיקייה לפתיחה כלשונית", UseDescriptionForTitle = true })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    folder = dlg.SelectedPath;
                }
                if (string.IsNullOrEmpty(folder)) return;
                var before = new HashSet<long>(new WindowManager().GetOpenWindows()
                    .Where(w => string.Equals(w.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
                    .Select(w => long.TryParse(w.Handle, out var h) ? h : 0L));
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                var t = new System.Windows.Forms.Timer { Interval = 700 };
                int tries = 0;
                t.Tick += (s, e) =>
                {
                    tries++;
                    var now = new WindowManager().GetOpenWindows()
                        .Where(w => string.Equals(w.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
                        .Select(w => long.TryParse(w.Handle, out var h) ? h : 0L)
                        .Where(h => h != 0 && !before.Contains(h)).ToList();
                    if (now.Count > 0) { t.Stop(); t.Dispose(); foreach (var h in now) { try { AttachChild(new IntPtr(h)); } catch { } } SetActive(_children.Count - 1); Invalidate(); }
                    else if (tries >= 5) { t.Stop(); t.Dispose(); }
                };
                t.Start();
            }
            catch (Exception ex) { Debug.WriteLine($"folder: {ex.Message}"); }
        }

        private static List<long>? ShowWindowPicker(List<WindowInfo> candidates)
        {
            using var dlg = new Form
            {
                Text = "בחר חלונות להוספה", ClientSize = new Size(480, 380), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
                RightToLeft = RightToLeft.Yes, RightToLeftLayout = true
            };
            var clb = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            foreach (var w in candidates) clb.Items.Add($"{w.Title}  —  {w.ProcessName}");
            var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
            var ok = new Button { Text = "הוסף", DialogResult = DialogResult.OK, Width = 100 };
            var cancel = new Button { Text = "ביטול", DialogResult = DialogResult.Cancel, Width = 100 };
            panel.Controls.Add(ok); panel.Controls.Add(cancel);
            dlg.Controls.Add(clb); dlg.Controls.Add(panel); dlg.AcceptButton = ok; dlg.CancelButton = cancel;
            if (dlg.ShowDialog() != DialogResult.OK) return null;
            var res = new List<long>();
            foreach (int i in clb.CheckedIndices) if (i >= 0 && i < candidates.Count && long.TryParse(candidates[i].Handle, out var h)) res.Add(h);
            return res;
        }

        private void DetachChild(Child c, Win32.POINT? at = null)
        {
            int idx = _children.IndexOf(c);
            RestoreChild(c, at);
            _children.Remove(c);
            if (_children.Count == 0) { Close(); return; }
            SetActive(Math.Max(0, idx - 1));
            Invalidate();
        }

        private void RestoreChild(Child c, Win32.POINT? at = null)
        {
            try
            {
                Win32.SetParent(c.Handle, IntPtr.Zero);
                Win32.SetWindowLongValue(c.Handle, Win32.GWL_STYLE, c.OriginalStyle);
                int w = c.OriginalRect.Right - c.OriginalRect.Left;
                int ht = c.OriginalRect.Bottom - c.OriginalRect.Top;
                if (w < 100) w = 800;
                if (ht < 80) ht = 600;
                int x = c.OriginalRect.Left, y = c.OriginalRect.Top;
                if (at.HasValue) { x = at.Value.X - 80; y = at.Value.Y - 10; }
                Win32.SetWindowPos(c.Handle, IntPtr.Zero, x, y, w, ht,
                    Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
                Win32.ShowWindow(c.Handle, Win32.SW_SHOWNORMAL);
            }
            catch (Exception ex) { Debug.WriteLine($"restore: {ex.Message}"); }
            finally { try { c.IconImg?.Dispose(); } catch { } }
        }

        private void RestoreChildren()
        {
            if (_restored) return;
            _restored = true;
            foreach (var c in _children) RestoreChild(c);
            lock (_sync) { _open.Remove(this); }
        }

        // ---------------- Borderless drag + resize ----------------
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                int lp = (int)(long)m.LParam;
                int sx = unchecked((short)(lp & 0xFFFF));
                int sy = unchecked((short)((lp >> 16) & 0xFFFF));
                Point p = PointToClient(new Point(sx, sy));
                int w = ClientSize.Width, h = ClientSize.Height;

                if (WindowState == FormWindowState.Normal)
                {
                    bool l = p.X <= EdgeB, r = p.X >= w - EdgeB, t = p.Y <= EdgeB, b = p.Y >= h - EdgeB;
                    if (t && l) { m.Result = (IntPtr)HTTOPLEFT; return; }
                    if (t && r) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                    if (b && l) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                    if (b && r) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                    if (l) { m.Result = (IntPtr)HTLEFT; return; }
                    if (r) { m.Result = (IntPtr)HTRIGHT; return; }
                    if (t) { m.Result = (IntPtr)HTTOP; return; }
                    if (b) { m.Result = (IntPtr)HTBOTTOM; return; }
                }

                if (p.Y >= 0 && p.Y < CaptionH)
                {
                    var (kind, _) = HitTest(p);
                    m.Result = (IntPtr)(kind == HitKind.None ? HTCAPTION : HTCLIENT);
                    return;
                }

                m.Result = (IntPtr)HTCLIENT;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
