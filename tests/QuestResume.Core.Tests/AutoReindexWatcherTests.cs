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
            using var watcher = new AutoReindexWatcher(
                folder,
                _ =>
                {
                    Interlocked.Increment(ref callCount);
                    tcs.TrySetResult();
                    return Task.CompletedTask;
                },
                debounce: TimeSpan.FromMilliseconds(300));

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

            // Give a little extra time to make sure no second callback fires from the batch.
            await Task.Delay(500);
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
}
