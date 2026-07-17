using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class Fb2ExtractorTests
{
    [Fact]
    public async Task ExtractAsync_Fb2File_ExtractsTitleAndParagraphs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.fb2");
        const string fb2 = """
            <?xml version="1.0" encoding="UTF-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">
              <description>
                <title-info>
                  <book-title>Livro de Teste</book-title>
                </title-info>
              </description>
              <body>
                <section>
                  <p>Primeiro parágrafo.</p>
                  <p>Segundo parágrafo.</p>
                </section>
              </body>
            </FictionBook>
            """;

        try
        {
            await File.WriteAllTextAsync(path, fb2);

            var extractor = new Fb2Extractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Livro de Teste", document.Text);
            Assert.Contains("Primeiro parágrafo.", document.Text);
            Assert.Contains("Segundo parágrafo.", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
