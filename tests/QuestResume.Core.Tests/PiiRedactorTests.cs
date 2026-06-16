using QuestResume.Core.Models;

namespace QuestResume.Core.Tests;

public class PiiRedactorTests
{
    [Fact]
    public void Redact_MasksCpf()
    {
        var result = PiiRedactor.Redact("Meu CPF é 123.456.789-09, guarde-o.");

        Assert.Contains("[CPF]", result);
        Assert.DoesNotContain("123.456.789-09", result);
    }

    [Fact]
    public void Redact_MasksCnpj()
    {
        var result = PiiRedactor.Redact("CNPJ: 12.345.678/0001-95");

        Assert.Contains("[CNPJ]", result);
        Assert.DoesNotContain("12.345.678/0001-95", result);
    }

    [Fact]
    public void Redact_MasksEmail()
    {
        var result = PiiRedactor.Redact("Contato: joao.silva@exemplo.com.br");

        Assert.Contains("[EMAIL]", result);
        Assert.DoesNotContain("joao.silva@exemplo.com.br", result);
    }

    [Fact]
    public void Redact_MasksPhone()
    {
        var result = PiiRedactor.Redact("Ligue para (11) 98765-4321 em horário comercial.");

        Assert.Contains("[TELEFONE]", result);
        Assert.DoesNotContain("98765-4321", result);
    }

    [Fact]
    public void Redact_LeavesPlainTextUnchanged()
    {
        const string text = "Este é um texto comum sem dados pessoais.";

        Assert.Equal(text, PiiRedactor.Redact(text));
    }
}
