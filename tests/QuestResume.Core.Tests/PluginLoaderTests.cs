using QuestResume.Core.Extraction;

namespace QuestResume.Core.Tests;

public class PluginLoaderTests
{
    private static string CreateTempPluginsFolder() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"plugins-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public void LoadPlugins_ValidPlugin_IsLoadedAndRegistered()
    {
        var pluginsFolder = CreateTempPluginsFolder();

        try
        {
            var sampleDllPath = typeof(SamplePlugin.LogFileExtractor).Assembly.Location;
            File.Copy(sampleDllPath, Path.Combine(pluginsFolder, Path.GetFileName(sampleDllPath)));

            var extractors = PluginLoader.LoadPlugins(pluginsFolder);

            Assert.Contains(extractors, e => e.SupportedExtensions.Contains(".log"));
        }
        finally
        {
            Directory.Delete(pluginsFolder, recursive: true);
        }
    }

    [Fact]
    public void LoadPlugins_CorruptedDll_IsIgnoredAndDoesNotThrow()
    {
        var pluginsFolder = CreateTempPluginsFolder();

        try
        {
            File.WriteAllBytes(Path.Combine(pluginsFolder, "corrupted.dll"), new byte[] { 1, 2, 3, 4, 5 });

            var extractors = PluginLoader.LoadPlugins(pluginsFolder);

            Assert.Empty(extractors);
        }
        finally
        {
            Directory.Delete(pluginsFolder, recursive: true);
        }
    }

    [Fact]
    public void LoadPlugins_CorruptedDllAlongsideValidOne_StillLoadsTheValidPlugin()
    {
        var pluginsFolder = CreateTempPluginsFolder();

        try
        {
            File.WriteAllBytes(Path.Combine(pluginsFolder, "corrupted.dll"), new byte[] { 9, 9, 9, 9 });
            var sampleDllPath = typeof(SamplePlugin.LogFileExtractor).Assembly.Location;
            File.Copy(sampleDllPath, Path.Combine(pluginsFolder, Path.GetFileName(sampleDllPath)));

            var messages = new List<string>();
            var extractors = PluginLoader.LoadPlugins(pluginsFolder, messages.Add);

            Assert.Contains(extractors, e => e.SupportedExtensions.Contains(".log"));
            Assert.Contains(messages, m => m.Contains("corrupted.dll"));
        }
        finally
        {
            Directory.Delete(pluginsFolder, recursive: true);
        }
    }

    [Fact]
    public void LoadPlugins_NonExistentFolder_ReturnsEmpty()
    {
        var extractors = PluginLoader.LoadPlugins(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        Assert.Empty(extractors);
    }

    [Fact]
    public void ExtractorRegistry_RegisterPlugins_OverridesAndExposesLoadedPlugins()
    {
        var pluginsFolder = CreateTempPluginsFolder();

        try
        {
            var sampleDllPath = typeof(SamplePlugin.LogFileExtractor).Assembly.Location;
            File.Copy(sampleDllPath, Path.Combine(pluginsFolder, Path.GetFileName(sampleDllPath)));

            var registry = new ExtractorRegistry();
            registry.RegisterPlugins(PluginLoader.LoadPlugins(pluginsFolder));

            Assert.True(registry.IsSupported(".log"));
            Assert.Single(registry.LoadedPlugins);
        }
        finally
        {
            Directory.Delete(pluginsFolder, recursive: true);
        }
    }
}
