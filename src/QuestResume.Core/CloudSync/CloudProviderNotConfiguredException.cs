namespace QuestResume.Core.CloudSync;

public sealed class CloudProviderNotConfiguredException : Exception
{
    public CloudProviderNotConfiguredException(string message) : base(message)
    {
    }
}
