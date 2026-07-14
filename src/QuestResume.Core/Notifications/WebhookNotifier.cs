using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuestResume.Core.Models;
using QuestResume.Core.Persistence;

namespace QuestResume.Core.Notifications;

/// <summary>
/// Notifica webhooks cadastrados (<see cref="WebhookStore"/>) quando eventos relevantes
/// ocorrem: <c>"indexing.completed"</c>, <c>"document.error"</c>, <c>"question.asked"</c>.
/// Cada notificação é enviada de forma assíncrona "fire-and-forget": nunca bloqueia nem lança
/// exceção para o chamador — falhas de rede/timeout apenas são logadas via <see cref="_log"/>.
/// </summary>
public sealed class WebhookNotifier
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _indexPath;
    private readonly HttpClient _httpClient;
    private readonly Action<string>? _log;

    public WebhookNotifier(string indexPath, HttpClient? httpClient = null, Action<string>? log = null)
    {
        _indexPath = indexPath;
        _httpClient = httpClient ?? new HttpClient();
        _log = log;
    }

    /// <summary>
    /// Dispara (sem aguardar) a notificação de <paramref name="eventName"/> para todos os
    /// webhooks cadastrados que estão inscritos nesse evento. Não lança exceções.
    /// </summary>
    public void Notify(string eventName, object payload)
    {
        try
        {
            var webhooks = new WebhookStore(_indexPath).Load()
                .Where(w => w.Events.Any(e => e.Equals(eventName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (webhooks.Count == 0)
            {
                return;
            }

            var body = JsonSerializer.Serialize(new
            {
                @event = eventName,
                timestampUtc = DateTime.UtcNow,
                data = payload
            }, JsonOptions);

            foreach (var webhook in webhooks)
            {
                // Fire-and-forget: cada envio roda em background e nunca propaga exceção
                // para o chamador (DocumentIndexer/RagQueryEngine), que não deve ter seu fluxo
                // principal afetado por um webhook lento ou indisponível.
                _ = SendAsync(webhook, body);
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Falha ao preparar notificação de webhook para o evento '{eventName}': {ex.Message}");
        }
    }

    private async Task SendAsync(WebhookConfig webhook, string body)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                request.Headers.Add("X-QuestResume-Signature", ComputeSignature(body, webhook.Secret));
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"Webhook '{webhook.Url}' respondeu com status {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Falha ao notificar webhook '{webhook.Url}': {ex.Message}");
        }
    }

    /// <summary>Calcula a assinatura HMAC-SHA256 (hex minúsculo) do corpo, usando <paramref name="secret"/> como chave.</summary>
    public static string ComputeSignature(string body, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(bodyBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
