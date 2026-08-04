using System.Reflection;
using System.Runtime.Loader;

namespace QuestResume.Core.Extraction;

public static class PluginLoader
{
        public static string DefaultPluginsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuestResume", "plugins");

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
