using QuestResume.Core.Indexing;

namespace QuestResume.Core.Tests;

public class AutoReindexWatcherTests
{
    [Fact]
    public async Task Watcher_DebouncesRapidChanges_TriggersReindexOnce()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"watcher-docs-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        var callCount = 0;
        var tcs = new TaskCompletionSource();

        try
        {
            var debounce = TimeSpan.FromMilliseconds(500);

            using var watcher = new AutoReindexWatcher(
                folder,
                _ =>
                {
                    Interlocked.Increment(ref callCount);
                    tcs.TrySetResult();
                    return Task.CompletedTask;
                },
                debounce: debounce);

            watcher.Start();

            // Simulate a batch of rapid changes (like a folder copy) — should still only
            // trigger one reindex once things go quiet for the debounce window.
            for (var i = 0; i < 5; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, $"file{i}.txt"), "conteúdo");
                await Task.Delay(30);
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(tcs.Task, completed);

            // Give generous extra quiet time (several debounce windows) to absorb any
            // OS-delayed/duplicate FileSystemWatcher events under load, then confirm no
            // second callback fired from the same batch.
            await Task.Delay(TimeSpan.FromMilliseconds(debounce.TotalMilliseconds * 4));
            Assert.Equal(1, callCount);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Start_MissingFolder_DoesNotThrow()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"missing-watcher-{Guid.NewGuid()}");

        using var watcher = new AutoReindexWatcher(folder, _ => Task.CompletedTask);
        var exception = Record.Exception(() => watcher.Start());

        Assert.Null(exception);
    }

    // --- Item 11: monitoramento de múltiplas pastas ---

    [Fact]
    public async Task Watcher_ChangeInAdditionalFolder_TriggersSingleConsolidatedReindex()
    {
        var primaryFolder = Path.Combine(Path.GetTempPath(), $"watcher-primary-{Guid.NewGuid()}");
        var additionalFolder = Path.Combine(Path.GetTempPath(), $"watcher-additional-{Guid.NewGuid()}");
        Directory.CreateDirectory(primaryFolder);
        Directory.CreateDirectory(additionalFolder);

        var callCount = 0;
        var tcs = new TaskCompletionSource();

        try
        {
            var debounce = TimeSpan.FromMilliseconds(500);

            using var watcher = new AutoReindexWatcher(
                primaryFolder,
                _ =>
                {
                    Interlocked.Increment(ref callCount);
                    tcs.TrySetResult();
                    return Task.CompletedTask;
                },
                debounce: debounce,
                additionalFolders: new[] { additionalFolder });

            watcher.Start();

            // Changes spread across BOTH watched folders (like a batch edit touching more than
            // one document root) should still only trigger a single consolidated reindex.
            for (var i = 0; i < 3; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(primaryFolder, $"p{i}.txt"), "conteúdo");
                await Task.Delay(30);
            }
            for (var i = 0; i < 3; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(additionalFolder, $"a{i}.txt"), "conteúdo");
                await Task.Delay(30);
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(tcs.Task, completed);

            await Task.Delay(TimeSpan.FromMilliseconds(debounce.TotalMilliseconds * 4));
            Assert.Equal(1, callCount);
        }
        finally
        {
            Directory.Delete(primaryFolder, recursive: true);
            Directory.Delete(additionalFolder, recursive: true);
        }
    }

    [Fact]
    public void Start_WithAdditionalFolders_WatchesBothWithoutThrowing()
    {
        var primaryFolder = Path.Combine(Path.GetTempPath(), $"watcher-primary2-{Guid.NewGuid()}");
        var additionalFolder = Path.Combine(Path.GetTempPath(), $"watcher-additional2-{Guid.NewGuid()}");
        Directory.CreateDirectory(primaryFolder);
        Directory.CreateDirectory(additionalFolder);

        try
        {
            using var watcher = new AutoReindexWatcher(
                primaryFolder,
                _ => Task.CompletedTask,
                additionalFolders: new[] { additionalFolder });

            var exception = Record.Exception(() => watcher.Start());
            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(primaryFolder, recursive: true);
            Directory.Delete(additionalFolder, recursive: true);
        }
    }
}
