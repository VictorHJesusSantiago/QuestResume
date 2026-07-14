namespace QuestResume.Core.Rag.Agent;

/// <summary>
/// A named, externally invokable capability that <see cref="AgentOrchestrator"/> can offer to
/// the LLM (ex.: busca web, calculadora). Implementações devem ser tolerantes a entrada
/// inválida, lançando uma <see cref="Exception"/> com mensagem clara em vez de travar o agente.
/// </summary>
public interface ITool
{
    /// <summary>Identificador curto usado pelo LLM para escolher a ferramenta (ex.: "calculator").</summary>
    string Name { get; }

    /// <summary>Descrição curta (em PT-BR) incluída no prompt para orientar o LLM sobre quando usar a ferramenta.</summary>
    string Description { get; }

    /// <summary>Executa a ferramenta com o argumento livre escolhido pelo LLM, retornando um texto simples com o resultado.</summary>
    Task<string> InvokeAsync(string input, CancellationToken cancellationToken = default);
}
