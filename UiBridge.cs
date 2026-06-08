using System;
using System.Windows.Forms;

namespace BackTrack
{
    /// <summary>
    /// Bridges background threads (the web server) to the WinForms UI thread.
    /// </summary>
    public static class UiBridge
    {
        public static Control? Invoker;

        public static void Post(Action action)
        {
            try
            {
                if (Invoker != null && Invoker.IsHandleCreated)
                    Invoker.BeginInvoke(action);
            }
            catch { }
        }

        /// <summary>Shows a native folder picker on the UI thread and returns the chosen path (or null).</summary>
        public static string? PickFolder()
        {
            try
            {
                if (Invoker == null || !Invoker.IsHandleCreated) return null;
                return (string?)Invoker.Invoke(new Func<string?>(() =>
                {
                    using var dlg = new FolderBrowserDialog
                    {
                        Description = "בחר תיקייה לפתיחה בלשונית",
                        UseDescriptionForTitle = true
                    };
                    return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
                }));
            }
            catch { return null; }
        }
    }
}
