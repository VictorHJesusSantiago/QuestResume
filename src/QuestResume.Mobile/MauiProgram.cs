using Microsoft.Extensions.Logging;
using QuestResume.Mobile.Pages;
using QuestResume.Mobile.Services;

namespace QuestResume.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Session/ApiClient são singletons: uma única sessão de login e um único HttpClient
		// reutilizado por toda a aplicação. Páginas são transient (uma nova instância por navegação).
		builder.Services.AddSingleton<SessionService>();
		builder.Services.AddSingleton<ApiClient>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<SearchPage>();
		builder.Services.AddTransient<AskPage>();
		builder.Services.AddTransient<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
