namespace QuestResume.Core.Rag.Evaluation;

public sealed class EvaluationCase
{
        public required string Question { get; init; }

        public List<string> ExpectedSourcePaths { get; init; } = new();

        public List<string>? ExpectedAnswerContains { get; init; }
}
