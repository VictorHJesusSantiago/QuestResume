namespace QuestResume.Core.Extraction;

/// <summary>
/// Contrato estável para extratores de terceiros (plugins). Um plugin nada mais é do que uma
/// implementação de <see cref="IFileExtractor"/> publicada em uma DLL separada e colocada na
/// pasta de plugins (<c>%LOCALAPPDATA%\QuestResume\plugins</c>). Esta interface existe apenas
/// para deixar explícito, no nome, que o tipo é destinado a descoberta dinâmica via
/// <see cref="PluginLoader"/> — qualquer classe pública com construtor sem parâmetros que
/// implemente <see cref="IFileExtractor"/> (direta ou indiretamente, inclusive via esta
/// interface) é elegível para carregamento.
/// </summary>
public interface IExtractorPlugin : IFileExtractor
{
}
