namespace QuestResume.Core.Auth;

/// <summary>Um usuário cadastrado do QuestResume, persistido em <see cref="UserStore"/>.</summary>
public sealed class User
{
    public required string Id { get; init; }

    public required string Username { get; set; }

    /// <summary>Hash PBKDF2 da senha, no formato <c>"{iterações}.{saltBase64}.{hashBase64}"</c>. Nunca a senha em si.</summary>
    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}
