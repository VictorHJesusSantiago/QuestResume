using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace QuestResume.Api.IntegrationTests;

public sealed class EndToEndFlowTests
{
    [Fact]
    public async Task FullFlow_IndexSearchAndAsk_WorksEndToEnd()
    {
        using var factory = new QuestResumeApiFactory();
        AuthenticationTests.SeedAdminUser(factory, "e2e-user", "SenhaForte123!");

        
        var sampleFile = Path.Combine(factory.DocumentsFolder, "manual.txt");
        await File.WriteAllTextAsync(sampleFile,
            "O QuestResume é um sistema de indexação e busca de documentos totalmente offline. " +
            "Ele utiliza Lucene.NET para busca textual e pode opcionalmente gerar embeddings.");

        using var client = factory.CreateClient();
        var token = await AuthenticationTests.LoginAsync(client, "e2e-user", "SenhaForte123!");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        
        var indexResponse = await client.PostAsJsonAsync("/api/index", new { folderPath = factory.DocumentsFolder });
        var indexBody = await indexResponse.Content.ReadAsStringAsync();
        Assert.True(indexResponse.IsSuccessStatusCode, $"Indexação falhou ({indexResponse.StatusCode}): {indexBody}");

        using var indexJson = JsonDocument.Parse(indexBody);
        Assert.True(indexJson.RootElement.GetProperty("filesProcessed").GetInt32() >= 1);

        
        var searchResponse = await client.PostAsJsonAsync("/api/search", new { query = "Lucene.NET", topK = 5 });
        var searchBody = await searchResponse.Content.ReadAsStringAsync();
        Assert.True(searchResponse.IsSuccessStatusCode, $"Busca falhou ({searchResponse.StatusCode}): {searchBody}");

        using var searchJson = JsonDocument.Parse(searchBody);
        var results = searchJson.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.GetProperty("fileName").GetString() == "manual.txt");

        
        
        var askResponse = await client.PostAsJsonAsync("/api/ask", new { question = "O que é o QuestResume?" });
        Assert.Equal(HttpStatusCode.BadRequest, askResponse.StatusCode);
        var askBody = await askResponse.Content.ReadAsStringAsync();
        Assert.Contains("modelo", askBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_ReturnsBadRequest_WhenNoIndexExistsYet()
    {
        using var factory = new QuestResumeApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/search", new { query = "qualquer coisa" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Index_ReturnsBadRequest_WhenFolderDoesNotExist()
    {
        using var factory = new QuestResumeApiFactory();
        using var client = factory.CreateClient();

        var missingFolder = Path.Combine(factory.RootFolder, "nao-existe-" + Guid.NewGuid().ToString("N"));
        var response = await client.PostAsJsonAsync("/api/index", new { folderPath = missingFolder });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_ReturnsOk_WithoutAuthentication()
    {
        using var factory = new QuestResumeApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DocumentsPreview_PaginatesContent_WithPageAndPageSize()
    {
        using var factory = new QuestResumeApiFactory();
        AuthenticationTests.SeedAdminUser(factory, "preview-user", "SenhaForte123!");

        
        var sampleFile = Path.Combine(factory.DocumentsFolder, "grande.txt");
        var paragraph = string.Concat(Enumerable.Repeat("Conteúdo de teste para paginação do preview. ", 50));
        await File.WriteAllTextAsync(sampleFile, paragraph);

        using var client = factory.CreateClient();
        var token = await AuthenticationTests.LoginAsync(client, "preview-user", "SenhaForte123!");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var indexResponse = await client.PostAsJsonAsync("/api/index", new { folderPath = factory.DocumentsFolder });
        Assert.True(indexResponse.IsSuccessStatusCode, await indexResponse.Content.ReadAsStringAsync());

        
        var page1Response = await client.GetAsync($"/api/documents/preview?path={Uri.EscapeDataString(sampleFile)}&page=1&pageSize=100");
        var page1Body = await page1Response.Content.ReadAsStringAsync();
        Assert.True(page1Response.IsSuccessStatusCode, page1Body);

        using var page1Json = JsonDocument.Parse(page1Body);
        var page1Content = page1Json.RootElement.GetProperty("content").GetString();
        var totalPages = page1Json.RootElement.GetProperty("totalPages").GetInt32();
        Assert.Equal(1, page1Json.RootElement.GetProperty("page").GetInt32());
        Assert.True(totalPages > 1, $"Esperava mais de uma página para forçar a paginação, obteve {totalPages}.");
        Assert.True(page1Content!.Length <= 100);

        var page2Response = await client.GetAsync($"/api/documents/preview?path={Uri.EscapeDataString(sampleFile)}&page=2&pageSize=100");
        var page2Body = await page2Response.Content.ReadAsStringAsync();
        Assert.True(page2Response.IsSuccessStatusCode, page2Body);

        using var page2Json = JsonDocument.Parse(page2Body);
        var page2Content = page2Json.RootElement.GetProperty("content").GetString();
        Assert.Equal(2, page2Json.RootElement.GetProperty("page").GetInt32());
        Assert.NotEqual(page1Content, page2Content);
    }

    [Fact]
    public async Task Suggest_ReturnsIndexedTermsStartingWithPrefix()
    {
        using var factory = new QuestResumeApiFactory();
        AuthenticationTests.SeedAdminUser(factory, "suggest-user", "SenhaForte123!");

        var sampleFile = Path.Combine(factory.DocumentsFolder, "manual.txt");
        await File.WriteAllTextAsync(sampleFile, "O QuestResume é um sistema de indexação e busca de documentos offline.");

        using var client = factory.CreateClient();
        var token = await AuthenticationTests.LoginAsync(client, "suggest-user", "SenhaForte123!");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var indexResponse = await client.PostAsJsonAsync("/api/index", new { folderPath = factory.DocumentsFolder });
        Assert.True(indexResponse.IsSuccessStatusCode, await indexResponse.Content.ReadAsStringAsync());

        var response = await client.GetAsync("/api/search/suggest?q=docu");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using var json = JsonDocument.Parse(body);
        var suggestions = json.RootElement.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.NotEmpty(suggestions);
    }

    [Fact]
    public async Task DidYouMean_ReturnsSuggestionsForMisspelledTerm()
    {
        using var factory = new QuestResumeApiFactory();
        AuthenticationTests.SeedAdminUser(factory, "didyoumean-user", "SenhaForte123!");

        var sampleFile = Path.Combine(factory.DocumentsFolder, "manual.txt");
        await File.WriteAllTextAsync(sampleFile, "O QuestResume é um sistema de indexação e busca de documentos offline.");

        using var client = factory.CreateClient();
        var token = await AuthenticationTests.LoginAsync(client, "didyoumean-user", "SenhaForte123!");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var indexResponse = await client.PostAsJsonAsync("/api/index", new { folderPath = factory.DocumentsFolder });
        Assert.True(indexResponse.IsSuccessStatusCode, await indexResponse.Content.ReadAsStringAsync());

        
        var response = await client.GetAsync("/api/search/didyoumean?q=documenty");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using var json = JsonDocument.Parse(body);
        var suggestions = json.RootElement.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.NotEmpty(suggestions);
    }

    [Fact]
    public async Task SimilarDocuments_WithoutEmbeddingsEnabled_ReturnsBadRequest()
    {
        using var factory = new QuestResumeApiFactory();
        AuthenticationTests.SeedAdminUser(factory, "similar-user", "SenhaForte123!");

        var sampleFile = Path.Combine(factory.DocumentsFolder, "manual.txt");
        await File.WriteAllTextAsync(sampleFile, "O QuestResume é um sistema de indexação e busca de documentos offline.");

        using var client = factory.CreateClient();
        var token = await AuthenticationTests.LoginAsync(client, "similar-user", "SenhaForte123!");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var indexResponse = await client.PostAsJsonAsync("/api/index", new { folderPath = factory.DocumentsFolder });
        Assert.True(indexResponse.IsSuccessStatusCode, await indexResponse.Content.ReadAsStringAsync());

        
        
        var response = await client.GetAsync($"/api/documents/similar?path={Uri.EscapeDataString(sampleFile)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
