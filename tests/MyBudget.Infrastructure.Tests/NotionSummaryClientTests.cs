using System.Net;
using System.Text;
using System.Text.Json;
using MyBudget.Core;

namespace MyBudget.Infrastructure.Tests;

[TestClass]
public sealed class NotionSummaryClientTests
{
    private static readonly Guid Parent = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Page = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Block = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static MonthlySummary Summary => MonthlySummary.Create(BudgetSnapshot.Empty(new BudgetMonth(2026, 9)));

    [TestMethod]
    public async Task CreatesOnlyAChildPageWithApprovedText()
    {
        var handler = new FakeHandler(EmptyList(), new { id = Page });
        using var http = new HttpClient(handler);
        using var client = new NotionSummaryClient(http);
        Assert.AreEqual($"https://www.notion.so/{Page:N}", (await client.PublishAsync(Parent, "test-token-not-real", Summary)).ToString());
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual("POST", handler.Requests[1].Method);
        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.AreEqual(Parent, body.RootElement.GetProperty("parent").GetProperty("page_id").GetGuid());
        Assert.AreEqual(Summary.Text, body.RootElement.GetProperty("children")[0].GetProperty("paragraph").GetProperty("rich_text")[0].GetProperty("text").GetProperty("content").GetString());
        Assert.IsFalse(handler.Requests[1].Body!.Contains("test-token", StringComparison.Ordinal));
        Assert.IsTrue(handler.Requests.All(request => request.Host == "api.notion.com"));
        Assert.IsTrue(handler.Requests.All(request => request.Version == NotionSummaryClient.ApiVersion));
    }

    [TestMethod]
    public async Task RepeatedSendUpdatesOnlyTheOwnedBlock()
    {
        var handler = new FakeHandler(ChildList(), new
        {
            results = new object[] {
                new { id = Guid.NewGuid(), paragraph = new { rich_text = new[] { new { plain_text = "User's unrelated notes" } } } },
                new { id = Block, paragraph = new { rich_text = new[] { new { plain_text = Summary.Marker + "\nOld summary" } } } } },
            has_more = false
        }, new { id = Block });
        using var http = new HttpClient(handler);
        using var client = new NotionSummaryClient(http);
        await client.PublishAsync(Parent, "test-token-not-real", Summary);
        Assert.HasCount(3, handler.Requests);
        Assert.AreEqual("PATCH", handler.Requests[2].Method);
        Assert.AreEqual($"/v1/blocks/{Block:D}", handler.Requests[2].Path);
        Assert.IsFalse(handler.Requests.Any(request => request.Method == "POST"));
    }

    [TestMethod]
    public async Task UnrecognizedExistingPageIsNeverOverwritten()
    {
        var handler = new FakeHandler(ChildList(), EmptyList());
        using var http = new HttpClient(handler);
        using var client = new NotionSummaryClient(http);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.PublishAsync(Parent, "fake-token", Summary));
        Assert.IsTrue(handler.Requests.All(request => request.Method == "GET"));
    }

    [TestMethod]
    public async Task PaginatedChildrenAreSearchedBeforeCreating()
    {
        var handler = new FakeHandler(new { results = Array.Empty<object>(), has_more = true, next_cursor = "next cursor" }, ChildList(), EmptyList());
        using var http = new HttpClient(handler);
        using var client = new NotionSummaryClient(http);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.PublishAsync(Parent, "fake-token", Summary));
        StringAssert.Contains(handler.Requests[1].Query, "start_cursor=next%20cursor");
        Assert.IsFalse(handler.Requests.Any(request => request.Method == "POST"));
    }

    [TestMethod]
    public async Task ServerErrorsDoNotExposeBodiesOrTokens()
    {
        var handler = new FakeHandler() { Status = HttpStatusCode.Unauthorized };
        using var http = new HttpClient(handler);
        using var client = new NotionSummaryClient(http);
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.PublishAsync(Parent, "sensitive-token", Summary));
        Assert.IsFalse(exception.Message.Contains("sensitive", StringComparison.Ordinal));
        Assert.HasCount(1, handler.Requests); // No retry of ambiguous writes.
    }

    [TestMethod]
    [DataRow("https://evil.example/11111111111111111111111111111111")]
    [DataRow("https://notion.so.evil.example/11111111111111111111111111111111")]
    [DataRow("http://www.notion.so/11111111111111111111111111111111")]
    [DataRow("00000000000000000000000000000000")]
    public void RejectsUntrustedDestinations(string value) => Assert.ThrowsExactly<ArgumentException>(() => NotionSummaryClient.ParsePageId(value));

    [TestMethod]
    public void AcceptsPageLinksWithTitleAndQuery() => Assert.AreEqual(Parent,
        NotionSummaryClient.ParsePageId($"https://www.notion.so/My-budget-{Parent:N}?pvs=4"));

    private static object EmptyList() => new { results = Array.Empty<object>(), has_more = false };
    private static object ChildList() => new { results = new[] { new { id = Page, child_page = new { title = Summary.Title } } }, has_more = false };
    private sealed record Request(string Method, string Host, string Path, string Query, string? Body, string Version);
    private sealed class FakeHandler(params object[] replies) : HttpMessageHandler
    {
        private readonly Queue<object> _replies = new(replies);
        public List<Request> Requests { get; } = [];
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Request(request.Method.Method, request.RequestUri!.Host, request.RequestUri.AbsolutePath,
                request.RequestUri.Query, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.GetValues("Notion-Version").Single()));
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(
                _replies.Count == 0 ? "sensitive server response" : JsonSerializer.Serialize(_replies.Dequeue()), Encoding.UTF8, "application/json")
            };
        }
    }
}
