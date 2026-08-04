namespace QuestResume.Core.Extraction;

public sealed class LoadedPluginInfo
{
    public required string AssemblyFileName { get; init; }

    public required string ExtractorTypeName { get; init; }

    public required IReadOnlyCollection<string> SupportedExtensions { get; init; }
}
