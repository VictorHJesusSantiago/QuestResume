using QuestResume.Core.Auth;
using QuestResume.Core.Indexing;

namespace QuestResume.Core.Tests;

/// <summary>
/// Proves that resolving a per-user index path via <see cref="UserIndexPathResolver"/> (as done
/// end-to-end by the API in Program.cs for every authenticated request) actually isolates search
/// results between two different users: indexing distinct content into each user's resolved
/// subfolder must never let one user's <see cref="SearchService"/> see the other's documents.
/// </summary>
public class UserIndexIsolationTests
{
    [Fact]
    public async Task TwoUsers_ResolvedIndexPaths_AreFullyIsolatedFromEachOther()
    {
        var baseIndexPath = Path.Combine(Path.GetTempPath(), $"user-isolation-index-{Guid.NewGuid()}");
        var folderA = Path.Combine(Path.GetTempPath(), $"user-isolation-docs-a-{Guid.NewGuid()}");
        var folderB = Path.Combine(Path.GetTempPath(), $"user-isolation-docs-b-{Guid.NewGuid()}");

        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);

        try
        {
            var userAId = "user-a-" + Guid.NewGuid().ToString("N");
            var userBId = "user-b-" + Guid.NewGuid().ToString("N");

            var indexPathA = UserIndexPathResolver.Resolve(baseIndexPath, userAId);
            var indexPathB = UserIndexPathResolver.Resolve(baseIndexPath, userBId);

            Assert.NotEqual(indexPathA, indexPathB);
            Assert.StartsWith(baseIndexPath, indexPathA);
            Assert.StartsWith(baseIndexPath, indexPathB);

            await File.WriteAllTextAsync(Path.Combine(folderA, "segredo-da-usuaria-a.txt"), "Conteudo confidencial pertencente somente a usuaria A.");
            await File.WriteAllTextAsync(Path.Combine(folderB, "segredo-do-usuario-b.txt"), "Conteudo confidencial pertencente somente ao usuario B.");

            var indexerA = new DocumentIndexer();
            var indexerB = new DocumentIndexer();
            await indexerA.IndexFolderAsync(folderA, indexPathA);
            await indexerB.IndexFolderAsync(folderB, indexPathB);

            var searchA = new SearchService(indexPathA);
            var searchB = new SearchService(indexPathB);

            var resultsAFromA = searchA.Search("confidencial", topK: 10);
            var resultsBFromB = searchB.Search("confidencial", topK: 10);

            Assert.Single(resultsAFromA);
            Assert.Contains("segredo-da-usuaria-a.txt", resultsAFromA[0].FileName);

            Assert.Single(resultsBFromB);
            Assert.Contains("segredo-do-usuario-b.txt", resultsBFromB[0].FileName);

            // The core isolation guarantee: user A's search service, backed by user A's resolved
            // index path, must never surface user B's file (and vice-versa).
            Assert.DoesNotContain(resultsAFromA, r => r.FileName.Contains("usuario-b", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(resultsBFromB, r => r.FileName.Contains("usuaria-a", StringComparison.OrdinalIgnoreCase));

            // Also confirm at the filesystem level that the two indexes physically live in
            // different folders under the shared base path.
            Assert.True(Directory.Exists(indexPathA));
            Assert.True(Directory.Exists(indexPathB));
            Assert.NotEqual(Path.GetFullPath(indexPathA), Path.GetFullPath(indexPathB));
        }
        finally
        {
            if (Directory.Exists(baseIndexPath)) Directory.Delete(baseIndexPath, recursive: true);
            if (Directory.Exists(folderA)) Directory.Delete(folderA, recursive: true);
            if (Directory.Exists(folderB)) Directory.Delete(folderB, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ProducesDeterministicSubfolderPerUser()
    {
        const string baseIndexPath = @"C:\data\index";
        var resolved1 = UserIndexPathResolver.Resolve(baseIndexPath, "alice");
        var resolved2 = UserIndexPathResolver.Resolve(baseIndexPath, "alice");
        var resolvedOther = UserIndexPathResolver.Resolve(baseIndexPath, "bob");

        Assert.Equal(resolved1, resolved2);
        Assert.NotEqual(resolved1, resolvedOther);
        Assert.Equal(Path.Combine(baseIndexPath, "alice"), resolved1);
    }
}
