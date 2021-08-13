using System;
using LiteDB;
using System.IO;

namespace FromCSharp.Types
{
    public record MigrondiWorkspace(
        [BsonId] ObjectId Id,
        string Path,
        string DisplayName,
        DateTime CreatedAt
    )
    {
        public static MigrondiWorkspace Create(string path, string? displayName = null)
        {
            var dName = displayName ?? System.IO.Path.GetDirectoryName(path) ?? "Workspace";
            return new MigrondiWorkspace(
                ObjectId.NewObjectId(),
                path,
                dName,
                DateTime.Now
            );
        }
    }

    public record WorkspaceOperation(
        [BsonId] ObjectId Id,
        string OperationName,
        int PresentMigrations,
        int PendingMigrations,
        DateTime CreatedAt
    )
    {
        static WorkspaceOperation Create(string operationName, int presentMigrations, int pendingMigrations)
        {
            return new WorkspaceOperation(
                ObjectId.NewObjectId(),
                operationName,
                presentMigrations,
                pendingMigrations,
                DateTime.Now
            );
        }
    }
}
