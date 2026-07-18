namespace QuestResume.Core.Auth;

/// <summary>Papel de um usuário do QuestResume, usado para autorização (ex.: endpoints administrativos).</summary>
public enum UserRole
{
    User,
    Admin,

    /// <summary>Perfil somente-leitura: pode buscar, perguntar e listar documentos, mas nunca escrever (indexar, remover, tags, backup/restore, config).</summary>
    ReadOnly
}
