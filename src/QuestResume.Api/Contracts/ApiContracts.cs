using QuestResume.Core.Models;

namespace QuestResume.Api.Contracts;

public sealed record IndexRequest(string? FolderPath);

public sealed record SearchRequest(
    string Query,
    int? TopK,
    string? Extension = null,
    string? FolderPath = null,
    string? Tag = null,
    bool Fuzzy = false,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    long? MinSizeBytes = null,
    long? MaxSizeBytes = null,
    string SortBy = "relevance",
    bool SortDescending = true,
    int Page = 1,
    int PageSize = 0);

public sealed record SetTagsRequest(string Path, List<string>? Tags);

public sealed record AskRequest(string Question, int? TopK, IReadOnlyList<ChatTurn>? History = null, string? Persona = null);

public sealed record BatchAskRequest(List<string> Questions, int? TopK);

public sealed record CompareRequest(string PathA, string PathB, string? Question, List<string>? Paths = null);

public sealed record SummarizeMultiRequest(List<string> Paths);

public sealed record ChatExportFormatRequest(List<ChatExportTurnRequest> Turns, string? Format);

public sealed record ConfigImportRequest(string Json);

public sealed record AnnotationRequest(string Path, int StartOffset, int EndOffset, string Text, string? Note);

public sealed record SearchExportRequest(List<SearchResultItem>? Results, string? Format);

public sealed record AnkiExportRequest(List<QuestResume.Core.Models.Flashcard> Flashcards);

public sealed record ExtractTableRequest(string Path, string? Instruction, string? Format);

/// <summary>Body of <c>POST /api/documents/reindex</c> (item 12): reindexes a single file without rebuilding the whole index.</summary>
public sealed record ReindexFileRequest(string Path);

public sealed record FlashcardsRequest(string Path, int? Count);

public sealed record QuizRequest(string Path, int? Count);

public sealed record TranslateRequest(string Text, string TargetLanguage);

public sealed record ChatExportTurnRequest(string Question, string Answer, List<string>? Sources);

public sealed record ChatExportRequest(List<ChatExportTurnRequest> Turns);

public sealed record LoginRequest(string Username, string Password);

public sealed record CreateUserRequest(string Username, string Password, string Role);

public sealed record CreateCollectionRequest(string Name);

public sealed record CloudSyncRequest(string RemoteFolderId);

public sealed record DocumentPreviewResponse(string FileName, string Content, int Page, int TotalPages);

public sealed record IndexingProgressResponse(bool Running, string? Message, int? Current, int? Total);
