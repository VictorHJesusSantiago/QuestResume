using System.Diagnostics.Metrics;

namespace QuestResume.Api.Services;

public sealed class QuestResumeMetrics
{
    public const string MeterName = "QuestResume.Api";

    private readonly Meter _meter;
    private readonly Counter<long> _questionsAnswered;
    private readonly Histogram<double> _llmResponseTimeMs;
    private readonly Counter<long> _documentsIndexed;
    private readonly Counter<long> _indexingErrors;

    private long _lastIndexSizeBytes;

    public QuestResumeMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _questionsAnswered = _meter.CreateCounter<long>(
            "questresume_questions_answered_total",
            unit: "{question}",
            description: "Número total de perguntas respondidas com sucesso via /api/ask ou /api/ask/stream.");

        _llmResponseTimeMs = _meter.CreateHistogram<double>(
            "questresume_llm_response_time_ms",
            unit: "ms",
            description: "Tempo de resposta do LLM (retrieval + geração) por pergunta.");

        _documentsIndexed = _meter.CreateCounter<long>(
            "questresume_documents_indexed_total",
            unit: "{document}",
            description: "Número total de documentos processados com sucesso durante indexações.");

        _indexingErrors = _meter.CreateCounter<long>(
            "questresume_indexing_errors_total",
            unit: "{error}",
            description: "Número total de erros de indexação de documentos.");

        _meter.CreateObservableGauge(
            "questresume_index_size_bytes",
            () => Volatile.Read(ref _lastIndexSizeBytes),
            unit: "By",
            description: "Tamanho aproximado (em bytes) do índice Lucene/vetorial atual, em disco.");
    }

    public void RecordQuestionAnswered(double elapsedMs)
    {
        _questionsAnswered.Add(1);
        _llmResponseTimeMs.Record(elapsedMs);
    }

    public void RecordIndexingCompleted(int documentsIndexed, int errors)
    {
        if (documentsIndexed > 0) _documentsIndexed.Add(documentsIndexed);
        if (errors > 0) _indexingErrors.Add(errors);
    }

        public void SetIndexSizeBytes(long sizeBytes) => Volatile.Write(ref _lastIndexSizeBytes, sizeBytes);
}
