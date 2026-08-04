namespace QuestResume.Core.CloudSync;

public sealed class CloudSyncResult
{
        public string LocalFolder { get; set; } = string.Empty;

        public int FilesDownloaded { get; set; }

        public int FoldersSkipped { get; set; }

        public List<string> Errors { get; set; } = new();
}
