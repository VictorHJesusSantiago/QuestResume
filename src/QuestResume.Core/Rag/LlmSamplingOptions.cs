namespace QuestResume.Core.Rag;

public sealed record LlmSamplingOptions(double Temperature = 0.8, double TopP = 0.9, int? Seed = null)
{
        public static readonly LlmSamplingOptions Default = new();
}
