namespace QuestResume.Core.Configuration;

public sealed class AppOptionsValidationException : Exception
{
    public AppOptionsValidationException(string message) : base(message)
    {
    }
}
