using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace QuestResume.Api.IntegrationTests;

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

        
        var createResponse = await client.PostAsJsonAsync("/api/collections", new { name = "projetos" });
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.IsSuccessStatusCode, $"Criação da coleção falhou ({createResponse.StatusCode}): {createBody}");

        
        var listResponse = await client.GetAsync("/api/collections");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        using var listJson = JsonDocument.Parse(listBody);
        var names = listJson.RootElement.EnumerateArray().Select(e => e.GetProperty("nome").GetString()).ToList();
        Assert.Contains("projetos", names);

        
        
        var sampleFile = Path.Combine(factory.DocumentsFolder, "projeto-x.txt");
        await File.WriteAllTextAsync(sampleFile, "Documento exclusivo da coleção projetos, com o termo único ZylophoneMarker.");

        client.DefaultRequestHeaders.Remove("X-Collection");
        client.DefaultRequestHeaders.Add("X-Collection", "projetos");

        var indexResponse = await client.PostAsJsonAsync("/api/index", new { folderPath = factory.DocumentsFolder });
        var indexBody = await indexResponse.Content.ReadAsStringAsync();
        Assert.True(indexResponse.IsSuccessStatusCode, $"Indexação na coleção falhou ({indexResponse.StatusCode}): {indexBody}");

        
        var searchInCollectionResponse = await client.PostAsJsonAsync("/api/search", new { query = "ZylophoneMarker", topK = 5 });
        var searchInCollectionBody = await searchInCollectionResponse.Content.ReadAsStringAsync();
        Assert.True(searchInCollectionResponse.IsSuccessStatusCode, $"Busca na coleção falhou: {searchInCollectionBody}");
        using var searchInCollectionJson = JsonDocument.Parse(searchInCollectionBody);
        Assert.NotEmpty(searchInCollectionJson.RootElement.EnumerateArray().ToList());

        
        
        client.DefaultRequestHeaders.Remove("X-Collection");
        var searchInDefaultResponse = await client.PostAsJsonAsync("/api/search", new { query = "ZylophoneMarker", topK = 5 });

        
        
        
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
