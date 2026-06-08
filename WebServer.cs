using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace BackTrack
{
    public class WebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Tracker _tracker;
        private readonly ClipboardMonitor _clipboardMonitor;
        private readonly SnapshotManager _snapshotManager;
        private readonly WindowManager _windowManager;
        private readonly int _port;
        private bool _isRunning = false;

        public WebServer(Tracker tracker, ClipboardMonitor clipboardMonitor, SnapshotManager snapshotManager, WindowManager windowManager, int port = 8420)
        {
            _tracker = tracker;
            _clipboardMonitor = clipboardMonitor;
            _snapshotManager = snapshotManager;
            _windowManager = windowManager;
            _port = port;
            _listener.Prefixes.Add($"http://localhost:{port}/");
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                _isRunning = true;
                Task.Run(() => ListenLoop());
                Debug.WriteLine($"Web server started at http://localhost:{_port}/");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start web server: {ex.Message}");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                _listener.Stop();
            }
            catch { }
        }

        private async Task ListenLoop()
        {
            while (_isRunning && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (Exception ex)
                {
                    if (!_isRunning) break;
                    Debug.WriteLine($"Listener loop error: {ex.Message}");
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // CORS headers
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                response.Close();
                return;
            }

            string urlPath = request.Url?.LocalPath.ToLowerInvariant() ?? "/";
            try
            {
                // Serve Web Assets (Embedded resources)
                if (urlPath == "/" || urlPath == "/index.html")
                {
                    await ServeEmbeddedResource(response, "index.html", "text/html; charset=utf-8");
                }
                else if (urlPath == "/style.css")
                {
                    await ServeEmbeddedResource(response, "style.css", "text/css");
                }
                else if (urlPath == "/app.js")
                {
                    await ServeEmbeddedResource(response, "app.js", "application/javascript");
                }

                // --- 1. HISTORY API ENDPOINTS ---
                else if (urlPath == "/api/history" && request.HttpMethod == "GET")
                {
                    var history = _tracker.GetHistory();
                    string json = JsonSerializer.Serialize(history);
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/restore" && request.HttpMethod == "POST")
                {
                    string? id = request.QueryString["id"];
                    if (string.IsNullOrEmpty(id))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing id parameter");
                    }
                    else
                    {
                        bool success = _tracker.RestoreItem(id);
                        string json = JsonSerializer.Serialize(new { success });
                        await SendJsonResponse(response, json);
                    }
                }
                else if (urlPath == "/api/history" && request.HttpMethod == "DELETE")
                {
                    _tracker.ClearHistory();
                    string json = JsonSerializer.Serialize(new { success = true });
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/history/delete" && request.HttpMethod == "DELETE")
                {
                    string? id = request.QueryString["id"];
                    if (string.IsNullOrEmpty(id))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing id parameter");
                    }
                    else
                    {
                        bool success = _tracker.DeleteHistoryItem(id);
                        string json = JsonSerializer.Serialize(new { success });
                        await SendJsonResponse(response, json);
                    }
                }

                // --- 2. PRIVATE MODE API ENDPOINTS ---
                else if (urlPath == "/api/private-mode" && request.HttpMethod == "GET")
                {
                    bool enabled = _tracker.IsPrivateMode;
                    string json = JsonSerializer.Serialize(new { enabled });
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/private-mode" && request.HttpMethod == "POST")
                {
                    string? enabledStr = request.QueryString["enabled"];
                    if (bool.TryParse(enabledStr, out bool enabled))
                    {
                        _tracker.IsPrivateMode = enabled;
                        _clipboardMonitor.IsPrivateMode = enabled;
                        string json = JsonSerializer.Serialize(new { success = true, enabled });
                        await SendJsonResponse(response, json);
                    }
                    else
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Invalid enabled parameter");
                    }
                }

                // --- 3. WORKSPACE SNAPSHOTS API ENDPOINTS ---
                else if (urlPath == "/api/snapshots" && request.HttpMethod == "GET")
                {
                    var snapshots = _snapshotManager.GetSnapshots();
                    string json = JsonSerializer.Serialize(snapshots);
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/snapshots/create" && request.HttpMethod == "POST")
                {
                    string? name = request.QueryString["name"];
                    // Decode name if it contains URL encoding (e.g. Hebrew characters)
                    if (!string.IsNullOrEmpty(name))
                    {
                        name = Uri.UnescapeDataString(name);
                    }
                    bool success = _snapshotManager.CreateSnapshot(name ?? "");
                    string json = JsonSerializer.Serialize(new { success });
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/snapshots/create-from-windows" && request.HttpMethod == "POST")
                {
                    string? name = request.QueryString["name"];
                    if (!string.IsNullOrEmpty(name)) name = Uri.UnescapeDataString(name);
                    string? handlesRaw = request.QueryString["handles"];
                    var handles = new List<long>();
                    if (!string.IsNullOrEmpty(handlesRaw))
                        foreach (var part in handlesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                            if (long.TryParse(part.Trim(), out long h)) handles.Add(h);
                    bool success = _snapshotManager.CreateSnapshotFromHandles(name ?? "", handles);
                    await SendJsonResponse(response, JsonSerializer.Serialize(new { success }));
                }
                else if (urlPath == "/api/snapshots/restore" && request.HttpMethod == "POST")
                {
                    string? id = request.QueryString["id"];
                    if (string.IsNullOrEmpty(id))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing id parameter");
                    }
                    else
                    {
                        bool success = _snapshotManager.RestoreSnapshot(id);
                        string json = JsonSerializer.Serialize(new { success });
                        await SendJsonResponse(response, json);
                    }
                }
                else if (urlPath == "/api/snapshots/restore-merged" && request.HttpMethod == "POST")
                {
                    string? id = request.QueryString["id"];
                    if (string.IsNullOrEmpty(id))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing id parameter");
                    }
                    else
                    {
                        var items = _snapshotManager.GetItems(id);
                        GroupLauncher.OpenMerged(items);
                        string json = JsonSerializer.Serialize(new { success = items.Count > 0 });
                        await SendJsonResponse(response, json);
                    }
                }
                else if (urlPath == "/api/snapshots/delete" && request.HttpMethod == "DELETE")
                {
                    string? id = request.QueryString["id"];
                    if (string.IsNullOrEmpty(id))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing id parameter");
                    }
                    else
                    {
                        bool success = _snapshotManager.DeleteSnapshot(id);
                        string json = JsonSerializer.Serialize(new { success });
                        await SendJsonResponse(response, json);
                    }
                }
                else if (urlPath == "/api/snapshots/add-items" && request.HttpMethod == "POST")
                {
                    string? id = request.QueryString["id"];
                    string body = await ReadRequestBody(request);
                    if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(body))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing id or body");
                    }
                    else
                    {
                        List<SnapshotItem>? items = null;
                        try
                        {
                            items = JsonSerializer.Deserialize<List<SnapshotItem>>(body,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                        catch { }
                        bool success = _snapshotManager.AddItems(id, items);
                        string json = JsonSerializer.Serialize(new { success });
                        await SendJsonResponse(response, json);
                    }
                }
                else if (urlPath == "/api/snapshots/item" && request.HttpMethod == "DELETE")
                {
                    string? id = request.QueryString["id"];
                    string? path = request.QueryString["path"];
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(path))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing id or path parameter");
                    }
                    else
                    {
                        bool success = _snapshotManager.RemoveItem(id, Uri.UnescapeDataString(path));
                        string json = JsonSerializer.Serialize(new { success });
                        await SendJsonResponse(response, json);
                    }
                }

                // --- 4. WINDOW ARRANGE (SNAP) API ENDPOINTS ---
                else if (urlPath == "/api/windows" && request.HttpMethod == "GET")
                {
                    var windows = _windowManager.GetOpenWindows();
                    string json = JsonSerializer.Serialize(windows);
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/windows/arrange" && request.HttpMethod == "POST")
                {
                    string layout = request.QueryString["layout"] ?? "grid";
                    string? handlesRaw = request.QueryString["handles"];
                    var handles = new List<long>();
                    if (!string.IsNullOrEmpty(handlesRaw))
                    {
                        foreach (var part in handlesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (long.TryParse(part.Trim(), out long h)) handles.Add(h);
                        }
                    }
                    int placed = _windowManager.ArrangeLayout(layout, handles);
                    string json = JsonSerializer.Serialize(new { success = placed > 0, placed });
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/windows/tile-all" && request.HttpMethod == "POST")
                {
                    string layout = request.QueryString["layout"] ?? "grid";
                    int placed = _windowManager.TileAll(layout);
                    string json = JsonSerializer.Serialize(new { success = placed > 0, placed });
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/windows/merge" && request.HttpMethod == "POST")
                {
                    string? handlesRaw = request.QueryString["handles"];
                    var handles = new List<long>();
                    if (!string.IsNullOrEmpty(handlesRaw))
                    {
                        foreach (var part in handlesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (long.TryParse(part.Trim(), out long h)) handles.Add(h);
                        }
                    }
                    int merged = _windowManager.MergeWindows(handles);
                    string json = JsonSerializer.Serialize(new { success = merged > 0, merged });
                    await SendJsonResponse(response, json);
                }

                // --- 6. FILE BROWSER (folder tabs) API ENDPOINTS ---
                else if (urlPath == "/api/fs/list" && request.HttpMethod == "GET")
                {
                    string? p = request.QueryString["path"];
                    if (!string.IsNullOrEmpty(p)) p = Uri.UnescapeDataString(p);
                    var listing = FileBrowser.List(p);
                    await SendJsonResponse(response, JsonSerializer.Serialize(listing));
                }
                else if (urlPath == "/api/fs/open" && request.HttpMethod == "POST")
                {
                    string? p = request.QueryString["path"];
                    if (string.IsNullOrEmpty(p))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing path parameter");
                    }
                    else
                    {
                        bool ok = FileBrowser.Open(Uri.UnescapeDataString(p));
                        await SendJsonResponse(response, JsonSerializer.Serialize(new { success = ok }));
                    }
                }
                else if (urlPath == "/api/fs/pick-folder" && request.HttpMethod == "GET")
                {
                    string? picked = UiBridge.PickFolder();
                    await SendJsonResponse(response, JsonSerializer.Serialize(new { path = picked }));
                }

                // --- 5. CLIPBOARD HISTORY API ENDPOINTS ---
                else if (urlPath == "/api/clipboard" && request.HttpMethod == "GET")
                {
                    var clipboardHistory = _clipboardMonitor.GetHistory();
                    string json = JsonSerializer.Serialize(clipboardHistory);
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/clipboard/copy" && request.HttpMethod == "POST")
                {
                    string? id = request.QueryString["id"];
                    if (string.IsNullOrEmpty(id))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing id parameter");
                    }
                    else
                    {
                        bool success = _clipboardMonitor.CopyToClipboard(id);
                        string json = JsonSerializer.Serialize(new { success });
                        await SendJsonResponse(response, json);
                    }
                }
                else if (urlPath == "/api/clipboard" && request.HttpMethod == "DELETE")
                {
                    _clipboardMonitor.ClearHistory();
                    string json = JsonSerializer.Serialize(new { success = true });
                    await SendJsonResponse(response, json);
                }
                else if (urlPath == "/api/clipboard/delete" && request.HttpMethod == "DELETE")
                {
                    string? id = request.QueryString["id"];
                    if (string.IsNullOrEmpty(id))
                    {
                        await SendError(response, HttpStatusCode.BadRequest, "Missing id parameter");
                    }
                    else
                    {
                        bool success = _clipboardMonitor.DeleteItem(id);
                        string json = JsonSerializer.Serialize(new { success });
                        await SendJsonResponse(response, json);
                    }
                }
                else
                {
                    await SendError(response, HttpStatusCode.NotFound, "Not Found");
                }
            }
            catch (Exception ex)
            {
                await SendError(response, HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task ServeEmbeddedResource(HttpListenerResponse response, string fileName, string contentType)
        {
            var assembly = typeof(Program).Assembly;
            string resourceName = $"BackTrack.web.{fileName}";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                await SendError(response, HttpStatusCode.NotFound, $"Resource {fileName} not found");
                return;
            }

            response.ContentType = contentType;
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentLength64 = stream.Length;
            await stream.CopyToAsync(response.OutputStream);
            response.OutputStream.Close();
        }

        private async Task<string> ReadRequestBody(HttpListenerRequest request)
        {
            try
            {
                if (!request.HasEntityBody) return "";
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }
            catch
            {
                return "";
            }
        }

        private async Task SendJsonResponse(HttpListenerResponse response, string json)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json; charset=utf-8";
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private async Task SendError(HttpListenerResponse response, HttpStatusCode statusCode, string message)
        {
            var errorObj = new { error = message };
            string json = JsonSerializer.Serialize(errorObj);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json; charset=utf-8";
            response.StatusCode = (int)statusCode;
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
    }
}
