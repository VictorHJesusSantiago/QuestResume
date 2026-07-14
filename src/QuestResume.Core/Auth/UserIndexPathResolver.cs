namespace QuestResume.Core.Auth;

/// <summary>
/// Resolve o caminho de índice isolado por usuário: <c>&lt;baseIndexPath&gt;/&lt;userId&gt;/</c>.
/// Usado pela API e pela CLI para que cada usuário tenha seu próprio índice Lucene/vectors.db,
/// evitando que um usuário veja documentos indexados por outro.
/// </summary>
public static class UserIndexPathResolver
{
    public static string Resolve(string baseIndexPath, string userId)
        => Path.Combine(baseIndexPath, userId);
}
