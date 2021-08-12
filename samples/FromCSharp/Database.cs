using System;
using System.Collections.Generic;
using FromCSharp.Types;
using LiteDB;

namespace FromCSharp
{
    public static class Database
    {
        private static LiteDatabase GetDatabase() => new LiteDatabase("./migrondiui.db");


        public static IEnumerable<MigrondiWorkspace> GetWorkspaces()
        {
            using var db = GetDatabase();
            var collection = db.GetCollection<MigrondiWorkspace>();
            return collection.FindAll();
        }

        public static ObjectId AddWorkspace(string path, string? displayName)
        {
            var workspace = MigrondiWorkspace.Create(path, displayName);
            using var db = GetDatabase();
            var collection = db.GetCollection<MigrondiWorkspace>();
            var id = collection.Insert(workspace);
            return id.AsObjectId;
        }

        public static bool RemoveWorkspace(ObjectId id)
        {
            using var db = GetDatabase();
            var collection = db.GetCollection<MigrondiWorkspace>();
            return collection.Delete(id);
        }
    }
}
