using System.Reflection;
using System.Runtime.Loader;

namespace QuestResume.Core.Extraction;

/// <summary>
/// Descobre e carrega dinamicamente extratores de arquivo de terceiros ("plugins") a partir de
/// DLLs colocadas em <see cref="DefaultPluginsFolder"/>. Cada DLL é carregada em seu próprio
/// <see cref="AssemblyLoadContext"/> isolado (collectible), para que um plugin com dependências
/// conflitantes não derrube o processo host nem colida com os tipos já carregados. Falhas de
/// carregamento (DLL corrompida, incompatível, sem tipos elegíveis etc.) são registradas via
/// <paramref name="log"/> e ignoradas — um plugin ruim nunca impede os demais de carregar nem
/// derruba a aplicação.
/// </summary>
public static class PluginLoader
{
    /// <summary>Pasta padrão onde plugins (.dll) são procurados no início da aplicação.</summary>
    public static string DefaultPluginsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuestResume", "plugins");

    /// <summary>
    /// Varre <paramref name="pluginsFolder"/> (ou <see cref="DefaultPluginsFolder"/>) em busca de
    /// DLLs, carrega cada uma em um <see cref="AssemblyLoadContext"/> isolado e instancia todo
    /// tipo público, concreto, com construtor sem parâmetros que implemente
    /// <see cref="IFileExtractor"/>.
    /// </summary>
    public static IReadOnlyList<IFileExtractor> LoadPlugins(
        string? pluginsFolder = null,
        Action<string>? log = null)
    {
        pluginsFolder ??= DefaultPluginsFolder;
        log ??= _ => { };

        var extractors = new List<IFileExtractor>();

        if (!Directory.Exists(pluginsFolder))
        {
            return extractors;
        }

        string[] dllFiles;
        try
        {
            dllFiles = Directory.GetFiles(pluginsFolder, "*.dll", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            log($"Não foi possível ler a pasta de plugins '{pluginsFolder}': {ex.Message}");
            return extractors;
        }

        foreach (var dllPath in dllFiles)
        {
            try
            {
                var loaded = LoadFromDll(dllPath, log);
                extractors.AddRange(loaded);
            }
            catch (Exception ex)
            {
                // Qualquer falha (assembly corrompido, dependência ausente, versão incompatível
                // do runtime etc.) é isolada a este plugin específico: registra e continua.
                log($"Falha ao carregar plugin '{Path.GetFileName(dllPath)}': {ex.Message}");
            }
        }

        return extractors;
    }

    private static List<IFileExtractor> LoadFromDll(string dllPath, Action<string> log)
    {
        var result = new List<IFileExtractor>();
        var contextName = $"QuestResumePlugin_{Path.GetFileNameWithoutExtension(dllPath)}";
        var context = new AssemblyLoadContext(contextName, isCollectible: true);

        Assembly assembly;
        try
        {
            using var stream = File.OpenRead(dllPath);
            assembly = context.LoadFromStream(stream);
        }
        catch (Exception ex)
        {
            log($"Falha ao carregar assembly do plugin '{Path.GetFileName(dllPath)}': {ex.Message}");
            return result;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            log($"Plugin '{Path.GetFileName(dllPath)}' carregado parcialmente (alguns tipos não puderam ser resolvidos).");
        }

        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract || !typeof(IFileExtractor).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                log($"Tipo '{type.FullName}' do plugin '{Path.GetFileName(dllPath)}' ignorado: não possui construtor sem parâmetros.");
                continue;
            }

            try
            {
                if (Activator.CreateInstance(type) is IFileExtractor extractor)
                {
                    result.Add(extractor);
                    log($"Plugin carregado: '{type.FullName}' de '{Path.GetFileName(dllPath)}' (extensões: {string.Join(", ", extractor.SupportedExtensions)}).");
                }
            }
            catch (Exception ex)
            {
                log($"Falha ao instanciar '{type.FullName}' do plugin '{Path.GetFileName(dllPath)}': {ex.Message}");
            }
        }

        if (result.Count == 0)
        {
            log($"Plugin '{Path.GetFileName(dllPath)}' não contém nenhum extrator válido (IFileExtractor).");
        }

        return result;
    }
}
