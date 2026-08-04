namespace QuestResume.Core.Auth;

public static class UserIndexPathResolver
{
    public static string Resolve(string baseIndexPath, string userId)
        => Path.Combine(baseIndexPath, userId);
}
