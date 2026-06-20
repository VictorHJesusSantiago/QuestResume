using QuestResume.Core.Models;

namespace QuestResume.Api.Contracts;

public sealed record IndexRequest(string? FolderPath);

public sealed record SearchRequest(string Query, int? TopK, string? Extension = null, string? FolderPath = null, string? Tag = null);

public sealed record SetTagsRequest(string Path, List<string>? Tags);

public sealed record AskRequest(string Question, int? TopK, IReadOnlyList<ChatTurn>? History = null);

public sealed record CompareRequest(string PathA, string PathB, string? Question);
