using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Island.Services;

/// <summary>
/// Loopback-only HTTP endpoint so other programs (e.g. a Claude Code hook) can raise an island notification.
/// POST /notify with header <c>X-Island-Token</c> and body <c>{"title": "...", "message": "..."}</c>.
/// Browsers cannot add that header to a cross-site request without a CORS preflight this server never answers,
/// so a web page cannot forge a notification.
/// </summary>
public sealed class NotifyServer : IDisposable
{
    public const string TokenHeader = "X-Island-Token";
    public const int MaxBodyBytes = 8192;

    private readonly string _token;
    private HttpListener? _listener;

    public event Action<string, string>? Notified;

    public NotifyServer(string token) => _token = token;

    public bool Start(int port)
    {
        if (string.IsNullOrEmpty(_token)) return false;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
            return true;
        }
        catch
        {
            _listener = null;
            return false;
        }
    }

    /// <summary>Compares tokens in constant time; an empty expected token never matches.</summary>
    public static bool IsAuthorized(string expected, string? provided)
    {
        if (expected.Length == 0 || provided is null) return false;
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(provided);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Reads at most <see cref="MaxBodyBytes"/>; returns null when the body is larger, whatever the declared length says.</summary>
    public static async Task<string?> ReadBodyAsync(Stream input)
    {
        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (read == 0) break;
            total += read;
        }

        return total > MaxBodyBytes ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }

    private async Task LoopAsync()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                return;
            }

            try
            {
                await HandleAsync(ctx);
            }
            catch
            {
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        if (req.Headers["Origin"] is not null || req.Url?.AbsolutePath != "/notify")
        {
            Reject(res, 403);
            return;
        }

        if (req.HttpMethod != "POST")
        {
            Reject(res, 405);
            return;
        }

        if (!IsAuthorized(_token, req.Headers[TokenHeader]))
        {
            Reject(res, 401);
            return;
        }

        if (req.ContentType is null || !req.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            Reject(res, 415);
            return;
        }

        var body = await ReadBodyAsync(req.InputStream);
        if (body is null)
        {
            Reject(res, 413);
            return;
        }

        string title, message;
        try
        {
            using var doc = JsonDocument.Parse(body);
            title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            Reject(res, 400);
            return;
        }

        if (title.Length > 80) title = title[..80];
        if (message.Length > 300) message = message[..300];
        Notified?.Invoke(string.IsNullOrWhiteSpace(title) ? Loc.T("Уведомление") : title, message);
        res.StatusCode = 204;
        res.Close();
    }

    private static void Reject(HttpListenerResponse res, int status)
    {
        res.StatusCode = status;
        res.Close();
    }

    public void Dispose()
    {
        try { _listener?.Close(); } catch { }
        _listener = null;
    }
}
