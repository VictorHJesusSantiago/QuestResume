using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class LlmJsonExtractorTests
{
    [Fact]
    public void ExtractJsonBlock_FencedJson_ReturnsInnerContent()
    {
        var raw = "Aqui está:\n```json\n[{\"a\":1}]\n```\nEspero que ajude.";
        var result = LlmJsonExtractor.ExtractJsonBlock(raw);
        Assert.Equal("[{\"a\":1}]", result);
    }

    [Fact]
    public void ExtractJsonBlock_PlainFence_ReturnsInnerContent()
    {
        var raw = "```\n{\"a\":1}\n```";
        var result = LlmJsonExtractor.ExtractJsonBlock(raw);
        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public void ExtractJsonBlock_ArrayWithSurroundingText_ExtractsArray()
    {
        var raw = "Segue o resultado: [{\"a\":1},{\"b\":2}] — isso é tudo.";
        var result = LlmJsonExtractor.ExtractJsonBlock(raw);
        Assert.Equal("[{\"a\":1},{\"b\":2}]", result);
    }

    [Fact]
    public void ExtractJsonBlock_ObjectWithSurroundingText_ExtractsObject()
    {
        var raw = "Resposta: {\"a\":1} obrigado";
        var result = LlmJsonExtractor.ExtractJsonBlock(raw);
        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public void ExtractJsonBlock_NoJson_ReturnsTrimmedOriginal()
    {
        var raw = "  não há json aqui  ";
        var result = LlmJsonExtractor.ExtractJsonBlock(raw);
        Assert.Equal("não há json aqui", result);
    }
}
