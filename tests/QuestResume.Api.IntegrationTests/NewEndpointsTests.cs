using System.Net.Http.Json;
using System.Text.Json;

namespace QuestResume.Api.IntegrationTests;

/// <summary>
/// Testa os endpoints novos: detecção de hardware (item 6) e lista de personas (item 3). O
/// benchmark (item 7) exige um modelo real carregado, então não é exercitado aqui.
/// </summary>
public sealed class NewEndpointsTests
{
    [Fact]
    public async Task SuggestGpuLayers_ReturnsHardwareInfo()
    {
        using var factory = new QuestResumeApiFactory();
        AuthenticationTests.SeedAdminUser(factory, "hw-user", "SenhaForte123!");

        using var client = factory.CreateClient();
        var token = await AuthenticationTests.LoginAsync(client, "hw-user", "SenhaForte123!");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/hardware/suggest-gpu-layers");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("totalRamMb").GetDouble() > 0);
        Assert.True(json.RootElement.GetProperty("suggestedGpuLayerCount").GetInt32() >= 0);
    }

    [Fact]
    public async Task Personas_ReturnsDefaults()
    {
        using var factory = new QuestResumeApiFactory();
        AuthenticationTests.SeedAdminUser(factory, "persona-user", "SenhaForte123!");

        using var client = factory.CreateClient();
        var token = await AuthenticationTests.LoginAsync(client, "persona-user", "SenhaForte123!");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/personas");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using var json = JsonDocument.Parse(body);
        var names = json.RootElement.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("Padrão", names);
    }
}
