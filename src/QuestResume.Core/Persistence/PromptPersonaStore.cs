using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Persiste as personas de prompt (item 3) como um sidecar JSON (<c>personas.json</c>) dentro da
/// pasta do índice, seguindo o mesmo padrão de <see cref="WebhookStore"/>. Quando o arquivo ainda
/// não existe, <see cref="Load"/> devolve um conjunto de personas pré-definidas em PT-BR
/// (<see cref="Defaults"/>) sem gravar nada em disco — o usuário só materializa o arquivo ao
/// salvar/editar personas.
/// </summary>
public sealed class PromptPersonaStore
{
    public const string FileName = "personas.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _indexPath;

    public PromptPersonaStore(string indexPath)
    {
        _indexPath = indexPath;
    }

    private string FilePath => Path.Combine(_indexPath, FileName);

    /// <summary>Personas pré-definidas usadas quando nenhum <c>personas.json</c> foi salvo ainda.</summary>
    public static IReadOnlyList<PromptPersona> Defaults { get; } = new List<PromptPersona>
    {
        new("Padrão",
            "Você é um assistente que responde perguntas com base apenas no conteúdo dos documentos " +
            "fornecidos, de forma clara e objetiva. Cite o nome do arquivo de onde veio a informação. " +
            "Se a resposta não estiver nos documentos, diga que não encontrou. Responda no idioma da pergunta."),
        new("Jurídico",
            "Você é um assistente jurídico rigoroso. Responda apenas com base no conteúdo dos documentos " +
            "fornecidos, usando linguagem formal e técnica do Direito. Cite explicitamente o arquivo e, quando " +
            "possível, artigos, cláusulas ou seções mencionadas. Nunca extrapole além do que os documentos " +
            "afirmam nem invente fundamentação legal. Se a informação não constar, declare que não há amparo " +
            "nos documentos. Responda no idioma da pergunta."),
        new("Acadêmico",
            "Você é um assistente acadêmico. Responda com base apenas no conteúdo dos documentos fornecidos, " +
            "de forma estruturada e precisa, explicando o raciocínio e definindo termos técnicos quando útil. " +
            "Cite o arquivo de origem de cada afirmação. Diferencie o que está explícito nos documentos do que " +
            "é inferência. Se a informação não constar, diga claramente. Responda no idioma da pergunta."),
        new("Resumo curto",
            "Você é um assistente que responde de forma extremamente concisa. Com base apenas no conteúdo dos " +
            "documentos fornecidos, dê a resposta mais curta possível (idealmente uma ou duas frases), citando o " +
            "arquivo de origem. Se a informação não constar, responda apenas 'Não encontrado nos documentos.' " +
            "Responda no idioma da pergunta."),
    };

    /// <summary>
    /// Carrega as personas salvas ou, na ausência do arquivo, devolve uma cópia de
    /// <see cref="Defaults"/>.
    /// </summary>
    public List<PromptPersona> Load()
    {
        if (!File.Exists(FilePath))
        {
            return Defaults.Select(p => new PromptPersona(p.Name, p.SystemPrompt)).ToList();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<PersonaStoreData>(json, SerializerOptions)?.Personas
                   ?? Defaults.Select(p => new PromptPersona(p.Name, p.SystemPrompt)).ToList();
        }
        catch
        {
            return Defaults.Select(p => new PromptPersona(p.Name, p.SystemPrompt)).ToList();
        }
    }

    public void Save(List<PromptPersona> personas)
    {
        Directory.CreateDirectory(_indexPath);
        var json = JsonSerializer.Serialize(new PersonaStoreData { Personas = personas }, SerializerOptions);
        File.WriteAllText(FilePath, json);
    }

    public void Add(PromptPersona persona)
    {
        var personas = Load();
        personas.RemoveAll(p => p.Name.Equals(persona.Name, StringComparison.OrdinalIgnoreCase));
        personas.Add(persona);
        Save(personas);
    }

    public bool Remove(string name)
    {
        var personas = Load();
        var removed = personas.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            Save(personas);
        }

        return removed;
    }

    /// <summary>Busca uma persona pelo nome (case-insensitive), ou <c>null</c> se não existir.</summary>
    public PromptPersona? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Load().FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PersonaStoreData
    {
        public List<PromptPersona> Personas { get; set; } = new();
    }
}
