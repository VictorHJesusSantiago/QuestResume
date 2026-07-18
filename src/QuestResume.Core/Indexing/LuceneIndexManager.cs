using Lucene.Net.Index;
using Lucene.Net.Store;
using QuestResume.Core.Models;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Singleton that keeps a single <see cref="DirectoryReader"/> open across requests and
/// refreshes it via <see cref="DirectoryReader.OpenIfChanged"/> so subsequent searches after
/// a write pick up the latest index segment without paying the open/close cost every time.
///
/// Also caches the <see cref="TagStore"/> (read from disk on first use, invalidated after
/// any write) so repeated tag-filter searches don't deserialize the JSON file once per call.
///
/// Register as a singleton in the API DI container and inject into <see cref="SearchService"/>
/// via the constructor overload that accepts this manager.
/// </summary>
public sealed class LuceneIndexManager : IDisposable
{
    private readonly object _readerLock = new();
    private string? _currentIndexPath;
    private FSDirectory? _directory;
    private DirectoryReader? _reader;

    private readonly object _tagLock = new();
    private TagStore? _cachedTagStore;
    private string? _tagStorePath;

    /// <summary>
    /// Returns a shared <see cref="DirectoryReader"/> for <paramref name="indexPath"/>,
    /// opening it on first use and refreshing it via <c>OpenIfChanged</c> on every subsequent
    /// call. Returns <c>null</c> when the path does not exist or contains no Lucene index.
    /// Never dispose the returned reader — its lifetime is managed by this class.
    /// </summary>
    public DirectoryReader? AcquireReader(string indexPath)
    {
        lock (_readerLock)
        {
            if (indexPath != _currentIndexPath)
            {
                RebuildReader(indexPath);
            }
            else if (_reader is not null)
            {
                try
                {
                    var refreshed = DirectoryReader.OpenIfChanged(_reader);
                    if (refreshed is not null)
                    {
                        _reader.Dispose();
                        _reader = refreshed;
                        InvalidateTagStoreInternal();
                    }
                }
                catch
                {
                    // Reader refresh failed — attempt a full rebuild on the next call
                    // rather than propagating an exception during a read operation.
                    RebuildReader(indexPath);
                }
            }

            return _reader;
        }
    }

    /// <summary>
    /// Returns whether a Lucene index exists at <paramref name="indexPath"/> without
    /// opening a per-call FSDirectory when a shared reader is already available.
    /// </summary>
    public bool IndexExists(string indexPath)
    {
        lock (_readerLock)
        {
            if (_currentIndexPath == indexPath && _reader is not null)
            {
                return true;
            }
        }

        if (!IODirectory.Exists(indexPath)) return false;

        try
        {
            using var dir = FSDirectory.Open(indexPath);
            return DirectoryReader.IndexExists(dir);
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns the <see cref="TagStore"/> for <paramref name="indexPath"/>, loading it from
    /// disk only when the cache is cold or the index path has changed.
    /// </summary>
    public TagStore GetTagStore(string indexPath)
    {
        lock (_tagLock)
        {
            if (_cachedTagStore is null || _tagStorePath != indexPath)
            {
                _cachedTagStore = TagStore.Load(indexPath);
                _tagStorePath = indexPath;
            }

            return _cachedTagStore;
        }
    }

    /// <summary>
    /// Drops the cached <see cref="TagStore"/> so the next call to <see cref="GetTagStore"/>
    /// reads the updated file. Call after any <see cref="TagStore.Save"/> to ensure
    /// subsequent searches see the new tags.
    /// </summary>
    public void InvalidateTagStore()
    {
        lock (_tagLock)
        {
            InvalidateTagStoreInternal();
        }
    }

    private void InvalidateTagStoreInternal()
    {
        _cachedTagStore = null;
    }

    private void RebuildReader(string indexPath)
    {
        _reader?.Dispose();
        _directory?.Dispose();
        _reader = null;
        _directory = null;
        _currentIndexPath = null;
        InvalidateTagStoreInternal();

        if (!IODirectory.Exists(indexPath)) return;

        try
        {
            // Item 14 (memory-mapped): FSDirectory.Open já escolhe automaticamente a melhor
            // implementação por SO/arquitetura — em processos 64-bit isso é MMapDirectory (índice
            // mapeado em memória), que é o comportamento desejado. Não forçamos MMapDirectory
            // explicitamente de propósito: no Windows ele mantém os arquivos mapeados travados até
            // o GC liberar, o que impediria apagar/recriar o índice (quebrando reindexação e os
            // testes de ciclo de vida). Deixar o Lucene decidir dá o mmap sem esse efeito colateral.
            var dir = FSDirectory.Open(indexPath);
            if (!DirectoryReader.IndexExists(dir))
            {
                dir.Dispose();
                return;
            }

            _directory = dir;
            _reader = DirectoryReader.Open(_directory);
            _currentIndexPath = indexPath;
        }
        catch
        {
            // Index may be mid-swap — leave reader null, next call will retry.
        }
    }

    public void Dispose()
    {
        lock (_readerLock)
        {
            _reader?.Dispose();
            _directory?.Dispose();
        }
    }
}
