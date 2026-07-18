using System.Text.Json;
using QuestResume.Core.Security;

namespace QuestResume.Core.Auth;

/// <summary>
/// Persiste usuários do QuestResume em um arquivo JSON sidecar (<c>users.json</c>) em
/// <c>%LOCALAPPDATA%\QuestResume</c>, fora do <c>config.json</c> principal. Senhas nunca são
/// gravadas em texto claro — apenas o verificador PBKDF2 (ver <see cref="MasterKeyManager"/>).
/// Thread-safe via lock em memória; adequado à escala de um punhado de usuários locais/de equipe
/// pequena (não um IdP completo).
/// </summary>
public sealed class UserStore
{
    private readonly object _lock = new();
    private readonly string _filePath;

    public UserStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuestResume", "users.json");
    }

    /// <summary>
    /// Valida uma senha contra a política de senha forte: mínimo 8 caracteres, pelo menos uma
    /// letra maiúscula e pelo menos um número. Lança <see cref="InvalidOperationException"/> com
    /// mensagem clara em PT-BR quando a senha não atende ao critério.
    /// </summary>
    public static void ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            throw new InvalidOperationException("A senha deve ter ao menos 8 caracteres.");
        }

        if (!password.Any(char.IsUpper))
        {
            throw new InvalidOperationException("A senha deve conter ao menos uma letra maiúscula.");
        }

        if (!password.Any(char.IsDigit))
        {
            throw new InvalidOperationException("A senha deve conter ao menos um número.");
        }
    }

    public User CreateUser(string username, string password, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Nome de usuário não pode ser vazio.");
        }

        ValidatePasswordStrength(password);

        lock (_lock)
        {
            var users = Load();
            if (users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Usuário '{username}' já existe.");
            }

            var user = new User
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = username,
                PasswordHash = MasterKeyManager.CreateVerifier(password),
                Role = role
            };

            users.Add(user);
            Save(users);
            return user;
        }
    }

    public User? ValidateCredentials(string username, string password)
    {
        lock (_lock)
        {
            var user = Load().FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user is null)
            {
                return null;
            }

            return MasterKeyManager.VerifyPassword(password, user.PasswordHash) ? user : null;
        }
    }

    public IReadOnlyList<User> ListUsers()
    {
        lock (_lock)
        {
            return Load();
        }
    }

    public bool DeleteUser(string username)
    {
        lock (_lock)
        {
            var users = Load();
            var removed = users.RemoveAll(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                Save(users);
            }

            return removed > 0;
        }
    }

    public bool ChangeRole(string username, UserRole role)
    {
        lock (_lock)
        {
            var users = Load();
            var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user is null)
            {
                return false;
            }

            user.Role = role;
            Save(users);
            return true;
        }
    }

    /// <summary>Busca um usuário pelo nome (case-insensitive); null se não existir.</summary>
    public User? FindByUsername(string username)
    {
        lock (_lock)
        {
            return Load().FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Habilita o 2FA (TOTP) para o usuário, gerando e persistindo um segredo Base32 novo.
    /// Retorna o segredo gerado para ser exibido ao usuário (só nesse momento). Lança se o usuário não existir.
    /// </summary>
    public string EnableTotp(string username)
    {
        lock (_lock)
        {
            var users = Load();
            var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Usuário '{username}' não encontrado.");

            var secret = TotpService.GenerateSecret();
            user.TotpSecret = secret;
            user.TotpEnabled = true;
            Save(users);
            return secret;
        }
    }

    /// <summary>Desabilita o 2FA do usuário, removendo o segredo. Retorna false se o usuário não existir.</summary>
    public bool DisableTotp(string username)
    {
        lock (_lock)
        {
            var users = Load();
            var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user is null)
            {
                return false;
            }

            user.TotpSecret = null;
            user.TotpEnabled = false;
            Save(users);
            return true;
        }
    }

    /// <summary>Define as coleções permitidas para um usuário (null/vazia = todas). Retorna false se não existir.</summary>
    public bool SetAllowedCollections(string username, List<string>? collections)
    {
        lock (_lock)
        {
            var users = Load();
            var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user is null)
            {
                return false;
            }

            user.AllowedCollections = collections is { Count: > 0 } ? collections : null;
            Save(users);
            return true;
        }
    }

    /// <summary>Se não houver nenhum usuário cadastrado, autenticação é dispensada (modo compatibilidade single-user).</summary>
    public bool HasAnyUser()
    {
        lock (_lock)
        {
            return Load().Count > 0;
        }
    }

    private List<User> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new List<User>();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
    }

    private void Save(List<User> users)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
