namespace QuestResume.Core.Auth;

public sealed class User
{
    public required string Id { get; init; }

    public required string Username { get; set; }

        public required string PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

        public string? TotpSecret { get; set; }

        public bool TotpEnabled { get; set; }

        public List<string>? AllowedCollections { get; set; }
}
