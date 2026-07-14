using QuestResume.Core.Auth;

namespace QuestResume.Core.Tests;

public class UserStoreTests
{
    private static UserStore CreateStore(out string filePath)
    {
        filePath = Path.Combine(Path.GetTempPath(), $"users-{Guid.NewGuid():N}.json");
        return new UserStore(filePath);
    }

    [Fact]
    public void CreateUser_HashesPassword_NeverStoresPlainText()
    {
        var store = CreateStore(out var filePath);

        try
        {
            var user = store.CreateUser("alice", "senha-secreta-123", UserRole.Admin);

            Assert.NotEqual("senha-secreta-123", user.PasswordHash);
            Assert.DoesNotContain("senha-secreta-123", File.ReadAllText(filePath));
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public void CreateUser_DuplicateUsername_Throws()
    {
        var store = CreateStore(out var filePath);

        try
        {
            store.CreateUser("bob", "senha123", UserRole.User);
            Assert.Throws<InvalidOperationException>(() => store.CreateUser("bob", "outrasenha", UserRole.User));
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public void ValidateCredentials_CorrectPassword_ReturnsUser()
    {
        var store = CreateStore(out var filePath);

        try
        {
            store.CreateUser("carol", "correcthorse", UserRole.User);

            var result = store.ValidateCredentials("carol", "correcthorse");

            Assert.NotNull(result);
            Assert.Equal("carol", result!.Username);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public void ValidateCredentials_IncorrectPassword_ReturnsNull()
    {
        var store = CreateStore(out var filePath);

        try
        {
            store.CreateUser("dave", "correctpassword", UserRole.User);

            var result = store.ValidateCredentials("dave", "wrongpassword");

            Assert.Null(result);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public void ValidateCredentials_UnknownUser_ReturnsNull()
    {
        var store = CreateStore(out var filePath);

        try
        {
            Assert.Null(store.ValidateCredentials("nobody", "whatever"));
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public void DeleteUser_RemovesUser()
    {
        var store = CreateStore(out var filePath);

        try
        {
            store.CreateUser("erin", "senha123", UserRole.User);

            var removed = store.DeleteUser("erin");

            Assert.True(removed);
            Assert.Empty(store.ListUsers());
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public void HasAnyUser_ReflectsRegisteredUsers()
    {
        var store = CreateStore(out var filePath);

        try
        {
            Assert.False(store.HasAnyUser());
            store.CreateUser("frank", "senha123", UserRole.User);
            Assert.True(store.HasAnyUser());
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }
}

public class UserIndexPathResolverTests
{
    [Fact]
    public void Resolve_ProducesDistinctPathsPerUser()
    {
        var pathA = UserIndexPathResolver.Resolve(@"C:\index", "user-a");
        var pathB = UserIndexPathResolver.Resolve(@"C:\index", "user-b");

        Assert.NotEqual(pathA, pathB);
        Assert.StartsWith(@"C:\index", pathA);
    }
}
