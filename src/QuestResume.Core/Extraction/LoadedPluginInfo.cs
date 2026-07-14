namespace QuestResume.Core.Extraction;

/// <summary>Metadados de um plugin de extração carregado com sucesso, para exibição em CLI/API/Desktop.</summary>
public sealed class LoadedPluginInfo
{
    public required string AssemblyFileName { get; init; }

    public required string ExtractorTypeName { get; init; }

    public required IReadOnlyCollection<string> SupportedExtensions { get; init; }
}
