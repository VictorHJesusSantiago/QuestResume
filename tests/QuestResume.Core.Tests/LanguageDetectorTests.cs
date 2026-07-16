using QuestResume.Core.Extraction;

namespace QuestResume.Core.Tests;

public class LanguageDetectorTests
{
    [Fact]
    public void Detect_PortugueseText_ReturnsPt()
    {
        var text = "O sistema não é apenas para você, mas também para todos que estão aqui. " +
                    "Isso é muito importante porque ajuda a organizar os documentos que você tem.";

        Assert.Equal(LanguageDetector.Portuguese, LanguageDetector.Detect(text));
    }

    [Fact]
    public void Detect_EnglishText_ReturnsEn()
    {
        var text = "This is the system that you were looking for, and it is not just for you but " +
                    "also for everyone who will need it when they have questions about their documents.";

        Assert.Equal(LanguageDetector.English, LanguageDetector.Detect(text));
    }

    [Fact]
    public void Detect_SpanishText_ReturnsEs()
    {
        var text = "Este es el sistema que usted estaba buscando, y no es solo para usted sino " +
                    "también para todos los que lo necesiten cuando tengan preguntas sobre sus documentos.";

        Assert.Equal(LanguageDetector.Spanish, LanguageDetector.Detect(text));
    }

    [Fact]
    public void Detect_FrenchText_ReturnsFr()
    {
        var text = "C'est le système que vous cherchiez, et ce n'est pas seulement pour vous mais " +
                    "aussi pour tous ceux qui en auront besoin quand ils auront des questions sur leurs documents.";

        Assert.Equal(LanguageDetector.French, LanguageDetector.Detect(text));
    }

    [Fact]
    public void Detect_EmptyText_ReturnsUnknown()
    {
        Assert.Equal(LanguageDetector.Unknown, LanguageDetector.Detect(string.Empty));
        Assert.Equal(LanguageDetector.Unknown, LanguageDetector.Detect(null));
    }

    [Fact]
    public void Detect_TooShortToDecide_ReturnsUnknown()
    {
        // Only a single stopword match ("de") — below the minimum-matches threshold.
        Assert.Equal(LanguageDetector.Unknown, LanguageDetector.Detect("QuestResume 12345 xyz de"));
    }
}
