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

    public User CreateUser(string username, string password, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Nome de usuário não pode ser vazio.");
        }

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
