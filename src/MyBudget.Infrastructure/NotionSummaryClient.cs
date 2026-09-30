using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyBudget.Core;

namespace MyBudget.Infrastructure;

/// <summary>Opt-in, one-way summary publishing. Never receives a statement or transaction list.</summary>
public sealed class NotionSummaryClient : IDisposable
{
    public const string ApiVersion = "2026-03-11";
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NotionSummaryClient(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
    }

    public static Guid ParsePageId(string value)
    {
        value = value.Trim();
        if (Guid.TryParse(value, out var id) && id != Guid.Empty) return id;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 &&
            (uri.Host == "notion.so" || uri.Host.EndsWith(".notion.so", StringComparison.OrdinalIgnoreCase) ||
             uri.Host == "notion.site" || uri.Host.EndsWith(".notion.site", StringComparison.OrdinalIgnoreCase)))
        {
            var match = Regex.Match(uri.AbsolutePath.TrimEnd('/'), @"([a-fA-F0-9]{32}|[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12})$");
            if (match.Success && Guid.TryParse(match.Value, out id) && id != Guid.Empty) return id;
        }
        throw new ArgumentException("Paste a Notion page link or page ID, not a database link or another website.");
    }

    public async Task<Uri> PublishAsync(Guid parentId, string token, MonthlySummary summary,
        CancellationToken cancellationToken = default)
    {
        if (parentId == Guid.Empty) throw new ArgumentException("Choose a Notion parent page.");
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512 || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("Enter a valid Notion internal connection token.");
        if (summary.Text.Length > 1900 || !summary.Text.StartsWith(summary.Marker + "\n", StringComparison.Ordinal))
            throw new ArgumentException("The monthly summary is not valid for publishing.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var children = await ChildrenAsync(parentId, token, cancellationToken);
            var matches = children.Where(block => block.TryGetProperty("child_page", out var child) &&
                child.GetProperty("title").GetString() == summary.Title).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("More than one matching monthly page exists in Notion. Resolve the duplicate pages before sending again.");
            Guid pageId;
            if (matches.Length == 0)
            {
                var body = new
                {
                    parent = new { page_id = parentId.ToString("D") },
                    properties = new { title = new { type = "title", title = RichText(summary.Title) } },
                    children = new[] { new { @object = "block", type = "paragraph", paragraph = new { rich_text = RichText(summary.Text) } } }
                };
                using var created = await SendAsync(HttpMethod.Post, "pages", token, body, cancellationToken);
                pageId = created.RootElement.GetProperty("id").GetGuid();
            }
            else
            {
                pageId = matches[0].GetProperty("id").GetGuid();
                var blocks = await ChildrenAsync(pageId, token, cancellationToken);
                var owned = blocks.Where(block => block.TryGetProperty("paragraph", out var paragraph) &&
                    ReadText(paragraph.GetProperty("rich_text")).StartsWith(summary.Marker + "\n", StringComparison.Ordinal)).ToArray();
                if (owned.Length != 1)
                    throw new InvalidOperationException("The existing monthly page's MyBudget summary block could not be identified safely. Nothing was overwritten.");
                var blockId = owned[0].GetProperty("id").GetGuid();
                using var updated = await SendAsync(HttpMethod.Patch, $"blocks/{blockId:D}", token,
                    new { paragraph = new { rich_text = RichText(summary.Text) } }, cancellationToken);
            }
            return new Uri($"https://www.notion.so/{pageId:N}");
        }
        finally { _gate.Release(); }
    }

    private async Task<List<JsonElement>> ChildrenAsync(Guid id, string token, CancellationToken cancellationToken)
    {
        var result = new List<JsonElement>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var path = $"blocks/{id:D}/children?page_size=100";
            if (cursor is not null) path += $"&start_cursor={Uri.EscapeDataString(cursor)}";
            using var response = await SendAsync(HttpMethod.Get, path, token, null, cancellationToken);
            foreach (var child in response.RootElement.GetProperty("results").EnumerateArray()) result.Add(child.Clone());
            if (!response.RootElement.GetProperty("has_more").GetBoolean()) return result;
            cursor = response.RootElement.GetProperty("next_cursor").GetString();
            if (string.IsNullOrWhiteSpace(cursor)) throw new InvalidDataException("Notion returned an incomplete page list.");
        }
        throw new InvalidOperationException("Choose a dedicated Notion parent page with fewer than 1,000 blocks.");
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string token, object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri($"https://api.notion.com/v1/{path}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Notion-Version", ApiVersion);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Deliberately omit server bodies, token values and URLs from errors.
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Notion rejected the connection token. Check it in Settings.",
                HttpStatusCode.Forbidden or HttpStatusCode.NotFound => "Share the parent Notion page with this connection and enable read, insert and update content permissions.",
                HttpStatusCode.TooManyRequests => "Notion is rate-limiting requests. Wait a moment, then preview and send again.",
                _ => "Notion could not complete the request. Check the connection and try again. Your local budget is unchanged."
            });
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(bytes, cancellationToken)) > 0)
        {
            if (buffer.Length + read > 1024 * 1024) throw new InvalidDataException("Notion's response was larger than expected.");
            buffer.Write(bytes, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }

    private static object[] RichText(string text) => [new { type = "text", text = new { content = text } }];
    private static string ReadText(JsonElement values) => string.Concat(values.EnumerateArray().Select(value =>
        value.TryGetProperty("plain_text", out var plain) ? plain.GetString() : value.GetProperty("text").GetProperty("content").GetString()));

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
        _gate.Dispose();
    }
}
