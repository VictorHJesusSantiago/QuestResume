using QuestResume.Core.Models;

namespace QuestResume.Api.Contracts;

public sealed record IndexRequest(string? FolderPath);

public sealed record SearchRequest(string Query, int? TopK, string? Extension = null, string? FolderPath = null, string? Tag = null);

public sealed record SetTagsRequest(string Path, List<string>? Tags);

public sealed record AskRequest(string Question, int? TopK, IReadOnlyList<ChatTurn>? History = null);

public sealed record BatchAskRequest(List<string> Questions, int? TopK);

public sealed record CompareRequest(string PathA, string PathB, string? Question);

public sealed record ExtractTableRequest(string Path, string? Instruction, string? Format);

public sealed record FlashcardsRequest(string Path, int? Count);

public sealed record QuizRequest(string Path, int? Count);

public sealed record TranslateRequest(string Text, string TargetLanguage);

public sealed record ChatExportTurnRequest(string Question, string Answer, List<string>? Sources);

public sealed record ChatExportRequest(List<ChatExportTurnRequest> Turns);

public sealed record LoginRequest(string Username, string Password);

public sealed record CreateUserRequest(string Username, string Password, string Role);

public sealed record CreateCollectionRequest(string Name);
