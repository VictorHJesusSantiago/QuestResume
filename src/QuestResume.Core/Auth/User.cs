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

    /// <summary>Segredo TOTP em Base32 (RFC 4648) quando o 2FA está habilitado; null caso contrário. Nunca exposto após o cadastro inicial.</summary>
    public string? TotpSecret { get; set; }

    /// <summary>Indica se o segundo fator (TOTP) é exigido no login deste usuário.</summary>
    public bool TotpEnabled { get; set; }

    /// <summary>
    /// Coleções que este usuário pode acessar/listar. Null ou vazio = acesso a todas as coleções
    /// (comportamento padrão, compatível com o modelo atual). Preenchida = restringe o acesso.
    /// </summary>
    public List<string>? AllowedCollections { get; set; }
}
