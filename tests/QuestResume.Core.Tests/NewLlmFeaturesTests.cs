using QuestResume.Core.Configuration;
using QuestResume.Core.Models;
using QuestResume.Core.Persistence;
using QuestResume.Core.Rag;
using QuestResume.Core.Rag.Agent;
using QuestResume.Core.Services;
using Xunit;

namespace QuestResume.Core.Tests;

/// <summary>
/// Testes das funcionalidades novas (itens 1, 2, 3, 6, 7, 11): amostragem configurável, prompt de
/// sistema customizável, personas, detecção de hardware, benchmark e ferramentas do agente.
/// </summary>
public sealed class NewLlmFeaturesTests
{
    // --- Item 1: amostragem configurável ---

    [Fact]
    public void AppOptions_SamplingDefaults_AreExpected()
    {
        var options = new AppOptions();
        Assert.Equal(0.8, options.LlmTemperature);
        Assert.Equal(0.9, options.LlmTopP);
        Assert.Null(options.LlmSeed);
    }

    [Theory]
    [InlineData(-0.1, 0.9)]
    [InlineData(0.8, 0)]
    [InlineData(0.8, 1.5)]
    public void AppOptions_Validate_RejectsInvalidSampling(double temperature, double topP)
    {
        var options = new AppOptions { LlmTemperature = temperature, LlmTopP = topP };
        Assert.Throws<AppOptionsValidationException>(options.Validate);
    }

    [Fact]
    public void LlmSamplingOptions_Default_MatchesLlamaDefaults()
    {
        Assert.Equal(0.8, LlmSamplingOptions.Default.Temperature);
        Assert.Equal(0.9, LlmSamplingOptions.Default.TopP);
        Assert.Null(LlmSamplingOptions.Default.Seed);
    }

    // --- Item 2: prompt de sistema customizável ---

    [Fact]
    public void PromptBuilder_WithoutOverride_UsesDefaultSystemPrompt()
    {
        var prompt = PromptBuilder.BuildPrompt("qual a capital?", Array.Empty<SearchResultItem>());
        Assert.Contains("assistente que responde perguntas", prompt);
    }

    [Fact]
    public void PromptBuilder_WithOverride_ReplacesSystemPrompt()
    {
        var custom = "Você é um pirata que responde em versos.";
        var prompt = PromptBuilder.BuildPrompt("qual a capital?", Array.Empty<SearchResultItem>(), null, custom);
        Assert.Contains(custom, prompt);
        Assert.DoesNotContain("assistente que responde perguntas", prompt);
        // A estrutura (pergunta) é mantida.
        Assert.Contains("PERGUNTA_DO_USUARIO: qual a capital?", prompt);
    }

    // --- Item 3: personas ---

    [Fact]
    public void PromptPersonaStore_WithoutFile_ReturnsDefaults()
    {
        using var dir = new TempDir();
        var store = new PromptPersonaStore(dir.Path);
        var personas = store.Load();
        Assert.Contains(personas, p => p.Name == "Padrão");
        Assert.Contains(personas, p => p.Name == "Jurídico");
        Assert.Contains(personas, p => p.Name == "Acadêmico");
        Assert.Contains(personas, p => p.Name == "Resumo curto");
    }

    [Fact]
    public void PromptPersonaStore_AddFindRemove_RoundTrips()
    {
        using var dir = new TempDir();
        var store = new PromptPersonaStore(dir.Path);
        store.Add(new PromptPersona("Teste", "Prompt de teste."));

        var found = store.Find("teste");
        Assert.NotNull(found);
        Assert.Equal("Prompt de teste.", found!.SystemPrompt);

        Assert.True(store.Remove("Teste"));
        Assert.Null(store.Find("Teste"));
    }

    // --- Item 6: detecção de hardware ---

    [Fact]
    public void HardwareDetection_ReportsRamAndSuggestsLayers()
    {
        var info = HardwareDetectionService.Detect();
        Assert.True(info.TotalRamMb > 0);
        Assert.True(info.ProcessorCount >= 1);
        Assert.Null(info.VramMb); // limitação honesta documentada
        Assert.True(info.SuggestedGpuLayerCount >= 0);
        Assert.False(string.IsNullOrWhiteSpace(info.Notes));
    }

    [Theory]
    [InlineData(4096, 0)]     // < 8GB
    [InlineData(12288, 10)]   // < 16GB
    [InlineData(24576, 20)]   // < 32GB
    [InlineData(131072, 50)]  // muita RAM
    public void HardwareDetection_SuggestGpuLayerCount_Heuristic(double ramMb, int expected)
    {
        Assert.Equal(expected, HardwareDetectionService.SuggestGpuLayerCount(ramMb));
    }

    // --- Item 7: benchmark ---

    [Fact]
    public async Task ModelBenchmark_ComputesTokensPerSecond()
    {
        var provider = FakeLlmProvider.ForStream(new[] { "a", "b", "c", "d" });
        var service = new ModelBenchmarkService(provider);
        var result = await service.RunAsync(new[] { "p1", "p2" });

        Assert.Equal(2, result.PromptCount);
        Assert.Equal(8, result.TotalTokens); // 4 tokens x 2 prompts
        Assert.True(result.TotalTimeMs >= 0);
        Assert.True(result.TokensPerSecond >= 0);
    }

    // --- Item 11: ferramentas do agente ---

    [Fact]
    public async Task DateTimeTool_ReturnsInjectedTime()
    {
        var fixed_ = new DateTimeOffset(2026, 7, 16, 10, 30, 0, TimeSpan.Zero);
        var tool = new DateTimeTool(() => fixed_);
        var local = await tool.InvokeAsync("");
        var utc = await tool.InvokeAsync("utc");
        Assert.Contains("16/07/2026", utc);
        Assert.Contains("UTC", utc);
        Assert.Contains("local", local);
    }

    [Theory]
    [InlineData("10 km em milhas", "6.2137")]
    [InlineData("100 c para f", "212")]
    [InlineData("32 f em c", "0")]
    [InlineData("1 kg em libras", "2.2046")]
    public void UnitConverter_ConvertsCommonUnits(string input, string expectedContains)
    {
        var result = UnitConverterTool.Convert(input);
        Assert.Contains(expectedContains, result);
    }

    [Fact]
    public void UnitConverter_InvalidInput_Throws()
    {
        Assert.Throws<UnitConverterException>(() => UnitConverterTool.Convert("banana em maçã"));
    }

    [Fact]
    public async Task FileReaderTool_AllowsWithinRoot_BlocksOutside()
    {
        using var dir = new TempDir();
        var allowed = Path.Combine(dir.Path, "docs");
        Directory.CreateDirectory(allowed);
        var file = Path.Combine(allowed, "a.txt");
        await File.WriteAllTextAsync(file, "conteúdo secreto");

        var tool = new FileReaderTool(new[] { allowed });
        var content = await tool.InvokeAsync(file);
        Assert.Contains("conteúdo secreto", content);

        // Fora da raiz permitida.
        var outside = Path.Combine(dir.Path, "outside.txt");
        await File.WriteAllTextAsync(outside, "x");
        await Assert.ThrowsAsync<FileReaderToolException>(() => tool.InvokeAsync(outside));
    }

    [Fact]
    public async Task IndexStatsTool_FormatsStats()
    {
        var stats = new DashboardStats { DocumentCount = 5, ChunkCount = 42, QuestionCount = 3 };
        var tool = new IndexStatsTool(() => stats);
        var text = await tool.InvokeAsync("");
        Assert.Contains("Documentos indexados: 5", text);
        Assert.Contains("Trechos (chunks): 42", text);
    }

    [Fact]
    public void AgentToolFactory_Build_IncludesLocalTools()
    {
        var options = new AppOptions();
        var tools = AgentToolFactory.Build(options);
        Assert.Contains(tools, t => t.Name == "calculator");
        Assert.Contains(tools, t => t.Name == "datetime");
        Assert.Contains(tools, t => t.Name == "unit_converter");
        // Sem DocumentsFolder nem endpoint web, essas não entram.
        Assert.DoesNotContain(tools, t => t.Name == "file_reader");
        Assert.DoesNotContain(tools, t => t.Name == "web_search");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qr-tests-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
