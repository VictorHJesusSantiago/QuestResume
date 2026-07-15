using System.Net;

namespace QuestResume.Core.CloudSync;

/// <summary>
/// Servidor HTTP local mínimo (<see cref="HttpListener"/>) usado pela CLI para receber o
/// redirecionamento do provedor OAuth2 após o usuário autorizar o acesso no navegador
/// (fluxo "loopback" recomendado pela RFC 8252 para apps nativos/desktop).
/// </summary>
public static class OAuthLoopbackListener
{
    /// <summary>
    /// Sobe um listener em <c>http://localhost:{port}/callback/</c>, aguarda uma única
    /// requisição contendo <c>?code=...</c> (ou <c>?error=...</c>) e retorna o código
    /// recebido. Responde ao navegador com uma página simples informando que pode ser
    /// fechado.
    /// </summary>
    public static async Task<string> WaitForAuthorizationCodeAsync(int port, CancellationToken cancellationToken = default)
    {
        using var listener = new HttpListener();
        var prefix = $"http://localhost:{port}/callback/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        try
        {
            using var registration = cancellationToken.Register(() =>
            {
                try { listener.Stop(); } catch { /* ignore */ }
            });

            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cancellationToken));
            if (completed != contextTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var context = await contextTask;
            var query = context.Request.QueryString;
            var code = query["code"];
            var error = query["error"];

            var responseHtml = error is null
                ? "<html><body><h2>Autenticação concluída.</h2><p>Você já pode fechar esta janela e voltar ao QuestResume.</p></body></html>"
                : $"<html><body><h2>Falha na autenticação.</h2><p>{error}</p></body></html>";

            var buffer = System.Text.Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, cancellationToken);
            context.Response.OutputStream.Close();

            if (error is not null)
            {
                throw new InvalidOperationException($"O provedor retornou um erro de autorização: {error}");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("Nenhum código de autorização foi recebido no redirecionamento.");
            }

            return code;
        }
        finally
        {
            try { listener.Stop(); } catch { /* ignore */ }
        }
    }
}
