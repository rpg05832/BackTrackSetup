using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace BackTrack
{
    /// <summary>
    /// Native application window hosting the local UI via WebView2.
    /// Closing the window fully disposes WebView2 so its memory is released (low idle footprint).
    /// </summary>
    public class MainForm : Form
    {
        private readonly WebView2 _web;
        private readonly int _port;

        public MainForm(int port)
        {
            _port = port;
            Text = "BackTrack";
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(720, 520);
            StartPosition = FormStartPosition.CenterScreen;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;

            try
            {
                using var stream = typeof(Program).Assembly.GetManifestResourceStream("BackTrack.backtrack_icon.ico");
                if (stream != null) Icon = new Icon(stream);
            }
            catch { }

            _web = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_web);

            Load += async (s, e) => await InitializeWebViewAsync();
            // Release the (heavy) WebView2 / Chromium memory when the window is closed.
            FormClosed += (s, e) => { try { _web?.Dispose(); } catch { } };
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                string udf = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BackTrack", "webview2");
                Directory.CreateDirectory(udf);

                var env = await CoreWebView2Environment.CreateAsync(null, udf);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.Navigate($"http://localhost:{_port}/");
            }
            catch (Exception ex)
            {
                // WebView2 runtime missing/failed: offer to download it (opens Microsoft's installer),
                // and meanwhile open the UI in the default browser as a fallback.
                try
                {
                    var r = MessageBox.Show(
                        "כדי להציג את חלון הניהול המובנה דרוש הרכיב WebView2 של מיקרוסופט (חינמי, קל).\n\n" +
                        "להוריד ולהתקין אותו עכשיו? בינתיים ההגדרות ייפתחו בדפדפן.",
                        "BackTrack — דרוש רכיב WebView2",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (r == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                            UseShellExecute = true
                        });
                    }
                }
                catch { }

                try
                {
                    Process.Start(new ProcessStartInfo { FileName = $"http://localhost:{_port}/", UseShellExecute = true });
                }
                catch { }

                Debug.WriteLine($"WebView2 init failed: {ex.Message}");
                Close();
            }
        }
    }
}
