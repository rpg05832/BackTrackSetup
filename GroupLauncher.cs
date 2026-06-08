using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BackTrack
{
    /// <summary>
    /// Opens a saved group: launches each item (folder/app/document), waits for its window to appear,
    /// then merges all of them into a single Groupy-style tabbed window (DockHost).
    /// </summary>
    public static class GroupLauncher
    {
        public static void OpenMerged(List<SnapshotItem> items)
        {
            if (items == null || items.Count == 0) return;

            Task.Run(() =>
            {
                try
                {
                    var wm = new WindowManager();
                    var seen = new HashSet<long>(CurrentHandles(wm));
                    var collected = new List<long>();

                    foreach (var it in items)
                    {
                        if (string.IsNullOrWhiteSpace(it.Path)) continue;
                        try
                        {
                            Process.Start(new ProcessStartInfo { FileName = it.Path, UseShellExecute = true });
                        }
                        catch (Exception ex) { Debug.WriteLine($"launch {it.Path}: {ex.Message}"); continue; }

                        // Poll up to ~4s for a new top-level window to show up.
                        for (int t = 0; t < 13; t++)
                        {
                            Thread.Sleep(300);
                            var now = CurrentHandles(wm);
                            var fresh = now.Where(h => !seen.Contains(h)).ToList();
                            if (fresh.Count > 0)
                            {
                                foreach (var h in fresh) { collected.Add(h); seen.Add(h); }
                                break;
                            }
                        }
                    }

                    if (collected.Count > 0)
                    {
                        DockHost.RequestMerge(collected);
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"OpenMerged: {ex.Message}"); }
            });
        }

        private static List<long> CurrentHandles(WindowManager wm)
        {
            return wm.GetOpenWindows()
                .Select(w => long.TryParse(w.Handle, out var h) ? h : 0L)
                .Where(h => h != 0)
                .ToList();
        }
    }
}
