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
