using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Progresso de estudo por flashcard (agendamento SM-2 e histórico de acertos/erros), persistido
/// num sidecar JSON <c>study-progress.json</c> ao lado do índice. Cada card é keyed por
/// <see cref="SpacedRepetitionCard.CardId"/> (hash de pergunta + documento), então o mesmo card
/// mantém seu agendamento entre sessões e reindexações.
/// </summary>
public sealed class StudyProgressStore
{
    public const string FileName = "study-progress.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private StudyProgressData _data;

    public StudyProgressStore(string indexPath)
    {
        _filePath = Path.Combine(indexPath, FileName);
        _data = Load();
    }

    private StudyProgressData Load()
    {
        if (!File.Exists(_filePath)) return new StudyProgressData();
        try
        {
            return JsonSerializer.Deserialize<StudyProgressData>(File.ReadAllText(_filePath), SerializerOptions)
                   ?? new StudyProgressData();
        }
        catch { return new StudyProgressData(); }
    }

    public void Save()
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, SerializerOptions));
    }

    /// <summary>Registra (criando se necessário) um card para acompanhamento, sem revisá-lo.</summary>
    public SpacedRepetitionCard GetOrCreate(string sourcePath, string question)
    {
        var id = SpacedRepetitionCard.ComputeCardId(sourcePath, question);
        if (!_data.Cards.TryGetValue(id, out var card))
        {
            card = new SpacedRepetitionCard
            {
                CardId = id,
                SourcePath = sourcePath,
                Question = question,
                NextReviewDate = DateTime.UtcNow.Date
            };
            _data.Cards[id] = card;
        }
        return card;
    }

    /// <summary>Aplica uma nota SM-2 a um card e registra a revisão no histórico diário.</summary>
    public SpacedRepetitionCard RecordReview(string cardId, int quality, DateTime? today = null)
    {
        if (!_data.Cards.TryGetValue(cardId, out var card))
            throw new KeyNotFoundException($"Nenhum flashcard com id '{cardId}' está sendo acompanhado.");

        SpacedRepetition.Apply(card, quality, today);

        var day = (today ?? DateTime.UtcNow).Date.ToString("yyyy-MM-dd");
        if (!_data.DailyStats.TryGetValue(day, out var stat))
        {
            stat = new DailyStudyStat();
            _data.DailyStats[day] = stat;
        }
        if (quality >= 3) stat.Correct++;
        else stat.Incorrect++;

        Save();
        return card;
    }

    /// <summary>Cards vencidos (NextReviewDate &lt;= hoje), ordenados pelos mais atrasados primeiro.</summary>
    public IReadOnlyList<SpacedRepetitionCard> GetDueCards(DateTime? today = null)
    {
        var reference = (today ?? DateTime.UtcNow).Date;
        return _data.Cards.Values
            .Where(c => c.NextReviewDate.Date <= reference)
            .OrderBy(c => c.NextReviewDate)
            .ToList();
    }

    public IReadOnlyList<SpacedRepetitionCard> GetAllCards() => _data.Cards.Values.ToList();

    /// <summary>
    /// Estatísticas agregadas: contagem por dia dos últimos <paramref name="days"/> dias e taxa de
    /// acerto geral.
    /// </summary>
    public StudyStats ComputeStats(int days = 30, DateTime? today = null)
    {
        var reference = (today ?? DateTime.UtcNow).Date;
        var daily = new List<DailyStudyStatPoint>();
        for (var i = days - 1; i >= 0; i--)
        {
            var d = reference.AddDays(-i);
            var key = d.ToString("yyyy-MM-dd");
            _data.DailyStats.TryGetValue(key, out var stat);
            daily.Add(new DailyStudyStatPoint
            {
                Date = key,
                Correct = stat?.Correct ?? 0,
                Incorrect = stat?.Incorrect ?? 0
            });
        }

        var totalCorrect = _data.DailyStats.Values.Sum(s => s.Correct);
        var totalIncorrect = _data.DailyStats.Values.Sum(s => s.Incorrect);
        var total = totalCorrect + totalIncorrect;

        return new StudyStats
        {
            TotalCards = _data.Cards.Count,
            DueToday = GetDueCards(reference).Count,
            TotalCorrect = totalCorrect,
            TotalIncorrect = totalIncorrect,
            OverallAccuracy = total == 0 ? 0 : (double)totalCorrect / total,
            Daily = daily
        };
    }

    private sealed class StudyProgressData
    {
        public Dictionary<string, SpacedRepetitionCard> Cards { get; set; } = new();
        public Dictionary<string, DailyStudyStat> DailyStats { get; set; } = new();
    }

    private sealed class DailyStudyStat
    {
        public int Correct { get; set; }
        public int Incorrect { get; set; }
    }
}

public sealed class StudyStats
{
    public int TotalCards { get; set; }
    public int DueToday { get; set; }
    public int TotalCorrect { get; set; }
    public int TotalIncorrect { get; set; }
    public double OverallAccuracy { get; set; }
    public IReadOnlyList<DailyStudyStatPoint> Daily { get; set; } = Array.Empty<DailyStudyStatPoint>();
}

public sealed class DailyStudyStatPoint
{
    public string Date { get; set; } = string.Empty;
    public int Correct { get; set; }
    public int Incorrect { get; set; }
}
