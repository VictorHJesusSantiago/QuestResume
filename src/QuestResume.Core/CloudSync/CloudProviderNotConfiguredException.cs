namespace QuestResume.Core.CloudSync;

/// <summary>
/// Lançada quando um comando/endpoint de integração com nuvem é usado sem o Client ID do
/// provedor configurado em <see cref="QuestResume.Core.Configuration.AppOptions"/>.
/// </summary>
public sealed class CloudProviderNotConfiguredException : Exception
{
    public CloudProviderNotConfiguredException(string message) : base(message)
    {
    }
}
