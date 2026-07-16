using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class TableHeuristicsTests
{
    [Fact]
    public void DetectAndFormatTables_GridWithConsistentColumns_BecomesMarkdownTable()
    {
        // Item 16: linhas consecutivas com o mesmo número de "colunas" (separadas por 2+
        // espaços ou tab) devem virar uma tabela Markdown com cabeçalho + separador.
        var pageText = "Nome        Idade       Cidade\nAna         30          Recife\nBruno       25          Natal";

        var result = TableHeuristics.DetectAndFormatTables(pageText);

        Assert.Contains("| Nome | Idade | Cidade |", result);
        Assert.Contains("|---|---|---|", result);
        Assert.Contains("| Ana | 30 | Recife |", result);
        Assert.Contains("| Bruno | 25 | Natal |", result);
    }

    [Fact]
    public void DetectAndFormatTables_TabSeparatedRows_BecomesMarkdownTable()
    {
        var pageText = "Produto\tPreço\nCaneta\t2,50\nCaderno\t15,00";

        var result = TableHeuristics.DetectAndFormatTables(pageText);

        Assert.Contains("| Produto | Preço |", result);
        Assert.Contains("|---|---|", result);
        Assert.Contains("| Caneta | 2,50 |", result);
    }

    [Fact]
    public void DetectAndFormatTables_PlainProse_IsLeftUnchanged()
    {
        var pageText = "Este é um parágrafo comum de um relatório.\nNão contém nenhuma tabela, apenas texto corrido.";

        var result = TableHeuristics.DetectAndFormatTables(pageText);

        Assert.Equal(pageText, result);
    }

    [Fact]
    public void DetectAndFormatTables_SingleTableLikeLine_IsNotEnoughToFormAsTable()
    {
        // Uma única linha com colunas não é suficiente (mínimo de 2 linhas consecutivas
        // com a mesma contagem de colunas) — evita falsos positivos em texto isolado.
        var pageText = "Chave        Valor";

        var result = TableHeuristics.DetectAndFormatTables(pageText);

        Assert.Equal(pageText, result);
    }

    [Fact]
    public void DetectAndFormatTables_EmptyOrNull_ReturnsInputUnchanged()
    {
        Assert.Equal(string.Empty, TableHeuristics.DetectAndFormatTables(string.Empty));
    }
}
