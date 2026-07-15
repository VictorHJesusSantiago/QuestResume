using QuestResume.Mobile.Models;
using QuestResume.Mobile.Services;

namespace QuestResume.Mobile.Pages;

public partial class AskPage : ContentPage
{
    private readonly ApiClient _apiClient;
    private readonly List<ChatTurn> _history = new();

    public AskPage(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void OnAskClicked(object? sender, EventArgs e)
    {
        var question = QuestionEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        AskButton.IsEnabled = false;
        AnswerLabel.IsVisible = false;
        SourcesHeaderLabel.IsVisible = false;
        SourcesView.ItemsSource = null;

        try
        {
            var result = await _apiClient.AskAsync(question, _history);

            AnswerLabel.Text = result.Answer;
            AnswerLabel.IsVisible = true;

            SourcesView.ItemsSource = result.Sources;
            SourcesHeaderLabel.IsVisible = result.Sources.Count > 0;

            _history.Add(new ChatTurn { Question = question, Answer = result.Answer });
            QuestionEditor.Text = string.Empty;
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Erro ao perguntar", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro ao perguntar", $"Falha ao perguntar: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            AskButton.IsEnabled = true;
        }
    }
}
