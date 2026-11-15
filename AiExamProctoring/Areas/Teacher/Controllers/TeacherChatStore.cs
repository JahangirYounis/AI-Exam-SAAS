using System.Data;
using AiExamProctoring.Data;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Teacher.Controllers;

// Chat reply links and reactions are additive: the existing message tables are unchanged.
// Supporting tables are installed by the TeacherCollaborationSupport migration.
public sealed class TeacherChatEntry
{
    public string Kind { get; set; } = "";
    public int RoomId { get; set; }
    public int EntryId { get; set; }
    public string Body { get; set; } = "";
    public string Sender { get; set; } = "Teacher";
    public string SenderId { get; set; } = "";
    public DateTime SentAtUtc { get; set; }
    public int? ReplyToId { get; set; }
    public string? ReplyBody { get; set; }
    public string? ReplySender { get; set; }
    public List<TeacherReactionCount> Reactions { get; set; } = new();
}
public sealed class TeacherReactionCount
{
    public string Emoji { get; set; } = "";
    public int Count { get; set; }
    public bool Mine { get; set; }
}
public static class TeacherChatStore
{
    private static void Add(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        cmd.Parameters.Add(parameter);
    }

    public static async Task EnrichAsync(ApplicationDbContext db, string kind, int roomId, string actor, List<TeacherChatEntry> entries)
    {
        if (entries.Count == 0) return;
        var map = entries.ToDictionary(e => e.EntryId);
        var ids = entries.Select(e => e.EntryId).Distinct().ToArray();
        var placeholders = string.Join(",", ids.Select((_, i) => "@id" + i));
        await db.Database.OpenConnectionAsync();
        try
        {
            await using (var cmd = db.Database.GetDbConnection().CreateCommand())
            {
                cmd.CommandText = $"SELECT MessageId, ReplyToId FROM dbo.TeacherChatReplyLinks WHERE Kind=@kind AND RoomId=@room AND MessageId IN ({placeholders})";
                Add(cmd,"@kind",kind); Add(cmd,"@room",roomId);
                for (var i=0; i<ids.Length; i++) Add(cmd,"@id"+i,ids[i]);
                await using var reader = await cmd.ExecuteReaderAsync();
                while(await reader.ReadAsync())
                    if(map.TryGetValue(reader.GetInt32(0), out var message)) message.ReplyToId = reader.GetInt32(1);
            }
            await using (var cmd = db.Database.GetDbConnection().CreateCommand())
            {
                cmd.CommandText = $"SELECT MessageId, UserId, Emoji FROM dbo.TeacherChatReactions WHERE Kind=@kind AND RoomId=@room AND MessageId IN ({placeholders})";
                Add(cmd,"@kind",kind); Add(cmd,"@room",roomId);
                for (var i=0; i<ids.Length; i++) Add(cmd,"@id"+i,ids[i]);
                var reactions = new Dictionary<(int,string),List<string>>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while(await reader.ReadAsync())
                {
                    var id = reader.GetInt32(0); var who = reader.GetString(1); var emoji = reader.GetString(2);
                    if (!reactions.TryGetValue((id,emoji), out var users)) reactions[(id,emoji)] = users = new();
                    users.Add(who);
                }
                foreach(var group in reactions)
                    if(map.TryGetValue(group.Key.Item1, out var message))
                        message.Reactions.Add(new TeacherReactionCount { Emoji=group.Key.Item2, Count=group.Value.Count, Mine=group.Value.Contains(actor) });
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    // Caller has already checked membership and that the referenced message exists in this room.
    public static Task<int> SaveReplyAsync(ApplicationDbContext db,string kind,int room,int entry,int parent) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.TeacherChatReplyLinks (Kind,RoomId,MessageId,ReplyToId) VALUES ({kind},{room},{entry},{parent})");

    public static Task<int> ToggleReactionAsync(ApplicationDbContext db,string kind,int room,int entry,string actor,string emoji) =>
        db.Database.ExecuteSqlInterpolatedAsync($@"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.TeacherChatReactions WITH (UPDLOCK, HOLDLOCK) WHERE Kind={kind} AND RoomId={room} AND MessageId={entry} AND UserId={actor})
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.TeacherChatReactions WHERE Kind={kind} AND RoomId={room} AND MessageId={entry} AND UserId={actor} AND Emoji={emoji})
                    DELETE FROM dbo.TeacherChatReactions WHERE Kind={kind} AND RoomId={room} AND MessageId={entry} AND UserId={actor};
                ELSE UPDATE dbo.TeacherChatReactions SET Emoji={emoji} WHERE Kind={kind} AND RoomId={room} AND MessageId={entry} AND UserId={actor};
            END
            ELSE INSERT INTO dbo.TeacherChatReactions(Kind,RoomId,MessageId,UserId,Emoji) VALUES ({kind},{room},{entry},{actor},{emoji});
            COMMIT TRANSACTION;");
}
