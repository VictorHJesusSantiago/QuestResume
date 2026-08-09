using Lucene.Net.Index;
using Lucene.Net.Store;
using QuestResume.Core.Models;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

public sealed class LuceneIndexManager : IDisposable
{
    private readonly object _readerLock = new();
    private string? _currentIndexPath;
    private FSDirectory? _directory;
    private DirectoryReader? _reader;

    private readonly object _tagLock = new();
    private TagStore? _cachedTagStore;
    private string? _tagStorePath;

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
                    
                    
                    RebuildReader(indexPath);
                }
            }

            return _reader;
        }
    }

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
