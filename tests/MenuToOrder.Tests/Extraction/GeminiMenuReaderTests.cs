using System.Net;
using System.Text.Json;
using MenuToOrder.Extraction;

namespace MenuToOrder.Tests.Extraction;

public class GeminiMenuReaderTests
{
    private static readonly MenuImage Image =
        new(File.ReadAllBytes(Repo.At("samples", "menus", "kose-lokantasi.jpg")), "image/jpeg");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_request_carries_the_image_the_pass_prompt_and_the_schema()
    {
        var recorded = File.ReadAllText(Repo.At("samples", "recorded", RecordedMenuReader.FileName(Image, Prompts.For(2))));
        var handler = new StubHandler(_ => Respond(HttpStatusCode.OK, recorded));

        var read = await Reader(handler).ReadAsync(Image, pass: 2, Ct);

        Assert.EndsWith("/models/gemini-3.8-flash:generateContent", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("test-key", handler.Request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("test-key", handler.Request.RequestUri.AbsoluteUri);
        using var body = JsonDocument.Parse(handler.Body);
        var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.Equal(Convert.ToBase64String(Image.Bytes), parts[0].GetProperty("inlineData").GetProperty("data").GetString());
        Assert.Equal(Prompts.For(2).Text, parts[1].GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("generationConfig").GetProperty("responseSchema").ValueKind);

        Assert.Equal("prices-first-v2", read.PromptVersion);
        Assert.Equal(recorded, read.RawResponse);
        Assert.Equal(11, read.Menu!.Items.Count);
    }

    [Fact]
    public async Task A_rejected_call_keeps_the_body_and_the_status()
    {
        var body = File.ReadAllText(Repo.At("tests", "fixtures", "gemini-503.json"));

        var read = await Reader(new StubHandler(_ => Respond(HttpStatusCode.ServiceUnavailable, body))).ReadAsync(Image, 1, Ct);

        Assert.Null(read.Menu);
        Assert.StartsWith("HTTP 503", read.Error);
        Assert.Equal(body, read.RawResponse);
    }

    [Fact]
    public async Task A_network_failure_is_recorded_as_an_error()
    {
        var read = await Reader(new StubHandler(_ => throw new HttpRequestException("connection reset"))).ReadAsync(Image, 1, Ct);

        Assert.Null(read.Menu);
        Assert.Null(read.RawResponse);
        Assert.Contains("connection reset", read.Error);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_swallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var reader = Reader(new StubHandler(_ => Respond(HttpStatusCode.OK, "{}")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(Image, 1, cancelled.Token));
    }

    private static GeminiMenuReader Reader(StubHandler handler) =>
        new(new HttpClient(handler), new GeminiOptions("test-key"), TimeProvider.System);

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return await respond(request);
        }
    }
}
