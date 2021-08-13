using System;
using System.Linq;
using System.Collections.Generic;
using FromCSharp.Types;
using LiteDB;

namespace FromCSharp
{
    public static class Database
    {
        private static ILiteDatabase GetDatabase() => new LiteDatabase("Filename=./migrondiui.db");

        private static ILiteCollection<MigrondiWorkspace> Workspaces(ILiteDatabase db)
        {
            var collection = db.GetCollection<MigrondiWorkspace>();
            collection.EnsureIndex(ws => ws.DisplayName);
            return collection;
        }


        public static MigrondiWorkspace[] GetWorkspaces()
        {
            using var db = GetDatabase();
            var collection = Workspaces(db);
            var results = collection.FindAll();
            return results.ToArray();
        }

        public static ObjectId AddWorkspace(string path, string? displayName = null)
        {
            var workspace = MigrondiWorkspace.Create(path, displayName);
            using var db = GetDatabase();
            var collection = Workspaces(db);
            var id = collection.Insert(workspace);
            return id.AsObjectId;
        }

        public static bool RemoveWorkspace(ObjectId id)
        {
            using var db = GetDatabase();
            var collection = Workspaces(db);
            return collection.Delete(id);
        }
    }
}
