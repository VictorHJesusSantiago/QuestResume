using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace QuestResume.Api.IntegrationTests;

/// <summary>
/// Verifica que múltiplas coleções (cabeçalho "X-Collection") mantêm índices isolados entre si —
/// indexar/buscar em uma coleção não deve aparecer nos resultados de outra nem na coleção
/// "default".
/// </summary>
public sealed class CollectionsTests
{
    [Fact]
    public async Task CreateCollection_ThenIndexAndSearchWithinIt_IsIsolatedFromDefault()
    {
        using var factory = new QuestResumeApiFactory();
        AuthenticationTests.SeedAdminUser(factory, "coll-user", "SenhaForte123!");

        using var client = factory.CreateClient();
        var token = await AuthenticationTests.LoginAsync(client, "coll-user", "SenhaForte123!");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Cria a coleção "projetos".
        var createResponse = await client.PostAsJsonAsync("/api/collections", new { name = "projetos" });
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.IsSuccessStatusCode, $"Criação da coleção falhou ({createResponse.StatusCode}): {createBody}");

        // Confirma que a coleção aparece na listagem.
        var listResponse = await client.GetAsync("/api/collections");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        using var listJson = JsonDocument.Parse(listBody);
        var names = listJson.RootElement.EnumerateArray().Select(e => e.GetProperty("nome").GetString()).ToList();
        Assert.Contains("projetos", names);

        // Indexa um arquivo de amostra dentro da pasta de documentos, mas com o cabeçalho
        // X-Collection apontando para "projetos" — deve ir para um índice separado do "default".
        var sampleFile = Path.Combine(factory.DocumentsFolder, "projeto-x.txt");
        await File.WriteAllTextAsync(sampleFile, "Documento exclusivo da coleção projetos, com o termo único ZylophoneMarker.");

        client.DefaultRequestHeaders.Remove("X-Collection");
        client.DefaultRequestHeaders.Add("X-Collection", "projetos");

        var indexResponse = await client.PostAsJsonAsync("/api/index", new { folderPath = factory.DocumentsFolder });
        var indexBody = await indexResponse.Content.ReadAsStringAsync();
        Assert.True(indexResponse.IsSuccessStatusCode, $"Indexação na coleção falhou ({indexResponse.StatusCode}): {indexBody}");

        // Busca dentro da coleção "projetos" deve encontrar o documento.
        var searchInCollectionResponse = await client.PostAsJsonAsync("/api/search", new { query = "ZylophoneMarker", topK = 5 });
        var searchInCollectionBody = await searchInCollectionResponse.Content.ReadAsStringAsync();
        Assert.True(searchInCollectionResponse.IsSuccessStatusCode, $"Busca na coleção falhou: {searchInCollectionBody}");
        using var searchInCollectionJson = JsonDocument.Parse(searchInCollectionBody);
        Assert.NotEmpty(searchInCollectionJson.RootElement.EnumerateArray().ToList());

        // Busca na coleção "default" (sem o cabeçalho X-Collection) não deve encontrar nada, pois
        // nunca foi indexada — confirma o isolamento entre coleções.
        client.DefaultRequestHeaders.Remove("X-Collection");
        var searchInDefaultResponse = await client.PostAsJsonAsync("/api/search", new { query = "ZylophoneMarker", topK = 5 });

        // A coleção default não tem índice ainda, então a API retorna 400 "Nenhum índice
        // encontrado" — o que já comprova que o documento indexado em "projetos" não vazou
        // para o índice default.
        Assert.Equal(HttpStatusCode.BadRequest, searchInDefaultResponse.StatusCode);
    }

    [Fact]
    public async Task CreateCollection_WithEmptyName_ReturnsBadRequest()
    {
        using var factory = new QuestResumeApiFactory();
        AuthenticationTests.SeedAdminUser(factory, "coll-empty", "SenhaForte123!");

        using var client = factory.CreateClient();
        var token = await AuthenticationTests.LoginAsync(client, "coll-empty", "SenhaForte123!");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/collections", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
