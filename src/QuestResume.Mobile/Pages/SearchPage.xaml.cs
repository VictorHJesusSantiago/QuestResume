using QuestResume.Mobile.Models;
using QuestResume.Mobile.Services;

namespace QuestResume.Mobile.Pages;

public partial class SearchPage : ContentPage
{
    private readonly ApiClient _apiClient;

    public SearchPage(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void OnSearchPressed(object? sender, EventArgs e)
    {
        var query = QuerySearchBar.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        ResultsView.ItemsSource = null;

        try
        {
            var results = await _apiClient.SearchAsync(query);
            ResultsView.ItemsSource = results;

            if (results.Count == 0)
            {
                await DisplayAlert("Busca", "Nenhum resultado encontrado.", "OK");
            }
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Erro na busca", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro na busca", $"Falha ao buscar: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }
}
