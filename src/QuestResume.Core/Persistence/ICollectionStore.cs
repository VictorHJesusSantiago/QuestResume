using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Catálogo de coleções conhecidas (nome, caminho físico do índice, data de criação),
/// persistido como sidecar JSON (<c>collections.json</c>) na raiz do índice base
/// (<c>&lt;baseIndexPath&gt;/</c> ou <c>&lt;baseIndexPath&gt;/&lt;userId&gt;/</c> em modo multiusuário).
/// Segue o mesmo padrão de <see cref="ITagStoreRepository"/>.
/// </summary>
public interface ICollectionStore
{
    /// <summary>Lista todas as coleções conhecidas, incluindo "default" mesmo se ainda não persistida.</summary>
    IReadOnlyList<Collection> List();

    /// <summary>Cria uma nova coleção com o nome informado. Lança se já existir.</summary>
    Collection Create(string name);

    /// <summary>Remove a coleção informada (não remove fisicamente os arquivos do índice). Retorna true se removida.</summary>
    bool Delete(string name);

    /// <summary>Resolve o caminho físico do índice para a coleção informada (cria a entrada implicitamente se for "default").</summary>
    string ResolvePath(string name);
}
