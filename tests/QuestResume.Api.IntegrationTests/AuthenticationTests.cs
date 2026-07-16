using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuestResume.Core.Auth;

namespace QuestResume.Api.IntegrationTests;

/// <summary>
/// Cobre o fluxo de autenticação JWT multiusuário exposto pela API: sem nenhum usuário
/// cadastrado a API opera em modo compatibilidade (sem exigir token); assim que um usuário é
/// registrado, endpoints sob /api/* passam a exigir "Authorization: Bearer &lt;jwt&gt;".
/// </summary>
public sealed class AuthenticationTests
{
    [Fact]
    public async Task ProtectedEndpoint_RejectsRequest_WhenNoTokenProvidedAndUserExists()
    {
        using var factory = new QuestResumeApiFactory();
        SeedAdminUser(factory, "auth-reject", "SenhaForte123!");

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_AcceptsRequest_WhenValidTokenProvided()
    {
        using var factory = new QuestResumeApiFactory();
        SeedAdminUser(factory, "auth-accept", "SenhaForte123!");

        using var client = factory.CreateClient();
        var token = await LoginAsync(client, "auth-accept", "SenhaForte123!");

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_ForInvalidCredentials()
    {
        using var factory = new QuestResumeApiFactory();
        SeedAdminUser(factory, "auth-badcreds", "SenhaForte123!");

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "auth-badcreds", password = "senha-errada" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsTokenAndRole_ForValidCredentials()
    {
        using var factory = new QuestResumeApiFactory();
        SeedAdminUser(factory, "auth-login-ok", "SenhaForte123!");

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "auth-login-ok", password = "SenhaForte123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal("Admin", body.Role);
    }

    /// <summary>
    /// Semeia um usuário diretamente via <see cref="UserStore"/> resolvido do container de DI da
    /// fábrica de testes — necessário porque POST /api/users exige a policy "AdminOnly", ou seja,
    /// não é possível criar o primeiro usuário Admin através da própria API (mesma limitação que
    /// existe em produção: o primeiro usuário precisa ser provisionado fora da API HTTP).
    /// </summary>
    internal static void SeedAdminUser(QuestResumeApiFactory factory, string username, string password)
    {
        using var scope = factory.Services.CreateScope();
        var userStore = scope.ServiceProvider.GetRequiredService<UserStore>();
        userStore.CreateUser(username, password, UserRole.Admin);
    }

    internal static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    internal sealed record LoginResponse(string Token, string UserId, string Username, string Role);
}
