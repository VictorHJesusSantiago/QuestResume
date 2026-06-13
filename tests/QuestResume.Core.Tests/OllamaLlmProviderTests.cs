using System.Net;
using System.Text;
using System.Text.Json;
using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class OllamaLlmProviderTests
{
    [Fact]
    public async Task CompleteAsync_ReturnsResponseField()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { response = "Resposta gerada.", done = true }),
                Encoding.UTF8,
                "application/json")
        });

        using var httpClient = new HttpClient(handler);
        using var provider = new OllamaLlmProvider("http://localhost:11434", "llama3.2", httpClient);

        var result = await provider.CompleteAsync("Pergunta de teste");

        Assert.Equal("Resposta gerada.", result);
    }

    [Fact]
    public async Task CompleteAsync_ConnectionFailure_ThrowsOllamaNotAvailableException()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("conexão recusada"));

        using var httpClient = new HttpClient(handler);
        using var provider = new OllamaLlmProvider("http://localhost:11434", "llama3.2", httpClient);

        await Assert.ThrowsAsync<OllamaNotAvailableException>(() => provider.CompleteAsync("Pergunta de teste"));
    }

    [Fact]
    public async Task CompleteAsync_NonSuccessStatusCode_ThrowsOllamaNotAvailableException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        using var httpClient = new HttpClient(handler);
        using var provider = new OllamaLlmProvider("http://localhost:11434", "llama3.2", httpClient);

        await Assert.ThrowsAsync<OllamaNotAvailableException>(() => provider.CompleteAsync("Pergunta de teste"));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
