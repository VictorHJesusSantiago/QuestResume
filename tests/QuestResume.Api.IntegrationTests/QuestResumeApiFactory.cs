using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuestResume.Core.Auth;
using QuestResume.Core.Configuration;

namespace QuestResume.Api.IntegrationTests;

public sealed class QuestResumeApiFactory : WebApplicationFactory<Program>
{
    public string RootFolder { get; } = Path.Combine(Path.GetTempPath(), $"questresume-apitests-{Guid.NewGuid():N}");

    public string ConfigPath => Path.Combine(RootFolder, "config.json");
    public string UsersPath => Path.Combine(RootFolder, "users.json");
    public string IndexPath => Path.Combine(RootFolder, "index");
    public string DocumentsFolder => Path.Combine(RootFolder, "documents");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(IndexPath);
        Directory.CreateDirectory(DocumentsFolder);

        
        
        
        var initialOptions = new AppOptions
        {
            IndexPath = IndexPath,
            DocumentsFolder = DocumentsFolder
        };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(initialOptions, new JsonSerializerOptions { WriteIndented = true }));

        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ConfigService>();
            services.AddSingleton(new ConfigService(ConfigPath));

            services.RemoveAll<UserStore>();
            services.AddSingleton(new UserStore(UsersPath));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            if (Directory.Exists(RootFolder))
            {
                Directory.Delete(RootFolder, recursive: true);
            }
        }
        catch (IOException)
        {
            
            
        }
    }
}
