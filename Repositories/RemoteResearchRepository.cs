using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

/// <summary>Local researcher-owned cache of normalized remote records. It is not an analytics store and preserves remote identities.</summary>
public static class RemoteResearchRepository
{
    private static bool _initialized;

    public static async Task ImportAsync(RemoteSyncPullResult pull)
    {
        await EnsureSchemaAsync();
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        foreach (var session in pull.Sessions)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO RemoteParticipantSessions(SessionId,ParticipantId,StudyId,DeploymentId,ConditionId,GroupId,RunType,StartedAtUtc,FeedEndedAtUtc,PostCompletedAtUtc,CompletedAtUtc,CompletionState,LifecycleState,LastSyncedAtUtc) VALUES($session,$participant,$study,$deployment,$condition,$group,$runType,$started,$feed,$post,$completed,$state,$lifecycle,$sync) ON CONFLICT(SessionId) DO UPDATE SET ParticipantId=excluded.ParticipantId,StudyId=excluded.StudyId,DeploymentId=excluded.DeploymentId,ConditionId=excluded.ConditionId,GroupId=excluded.GroupId,RunType=excluded.RunType,StartedAtUtc=excluded.StartedAtUtc,FeedEndedAtUtc=excluded.FeedEndedAtUtc,PostCompletedAtUtc=excluded.PostCompletedAtUtc,CompletedAtUtc=excluded.CompletedAtUtc,CompletionState=excluded.CompletionState,LifecycleState=excluded.LifecycleState,LastSyncedAtUtc=excluded.LastSyncedAtUtc;";
            Add(command,"$session",session.SessionId); Add(command,"$participant",session.ParticipantId); Add(command,"$study",session.StudyId); Add(command,"$deployment",session.DeploymentId); Add(command,"$condition",session.ConditionId); Add(command,"$group",session.GroupId); Add(command,"$runType",RunTypeName(session.RunType)); Add(command,"$started",session.StartedAtUtc); Add(command,"$feed",session.FeedEndedAtUtc); Add(command,"$post",session.PostQuestionnaireCompletedAtUtc); Add(command,"$completed",session.CompletedAtUtc); Add(command,"$state",session.CompletionState.ToString()); Add(command,"$lifecycle",session.LifecycleState.ToString()); Add(command,"$sync",DateTime.UtcNow); await command.ExecuteNonQueryAsync();
        }
        foreach (var item in pull.Events)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT OR IGNORE INTO RemoteBehavioralEvents(EventId,SessionId,ParticipantId,DeploymentId,ConditionId,ContentId,EventType,ClientTimestampUtc,RelativeMilliseconds,PayloadJson,SchemaVersion) VALUES($id,$session,$participant,$deployment,$condition,$content,$type,$timestamp,$relative,$payload,$schema);";
            Add(command,"$id",item.EventId); Add(command,"$session",item.SessionId); Add(command,"$participant",item.ParticipantId); Add(command,"$deployment",item.DeploymentId); Add(command,"$condition",item.ConditionId); Add(command,"$content",item.ContentId); Add(command,"$type",item.EventType); Add(command,"$timestamp",item.ClientTimestampUtc); Add(command,"$relative",item.ClientRelativeMilliseconds); Add(command,"$payload",item.PayloadJson); Add(command,"$schema",item.SchemaVersion); await command.ExecuteNonQueryAsync();
        }
        foreach (var item in pull.QuestionnaireResponses ?? Array.Empty<RemoteQuestionnaireResponseContract>())
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT OR IGNORE INTO RemoteQuestionnaireResponses(ResponseId,DeploymentId,ParticipantId,SessionId,QuestionnaireId,QuestionnaireVersionId,Stage,ResponseJson,SubmittedAtUtc) VALUES($id,$deployment,$participant,$session,$questionnaire,$version,$stage,$response,$submitted);";
            Add(command,"$id",item.ResponseId); Add(command,"$deployment",item.DeploymentId); Add(command,"$participant",item.ParticipantId); Add(command,"$session",item.SessionId); Add(command,"$questionnaire",item.QuestionnaireId); Add(command,"$version",item.QuestionnaireVersionId); Add(command,"$stage",item.Stage.ToString()); Add(command,"$response",item.ResponseJson); Add(command,"$submitted",item.SubmittedAtUtc); await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public static async Task<RemoteSyncCursor> GetCursorAsync(string consumerId = "desktop")
    {
        await EnsureSchemaAsync(); await using var connection = await DatabaseService.OpenConnectionAsync(); await using var command=connection.CreateCommand(); command.CommandText="SELECT Cursor,UpdatedAtUtc FROM RemoteSyncState WHERE ConsumerId=$id;"; Add(command,"$id",consumerId); await using var reader=await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new RemoteSyncCursor(reader.IsDBNull(0)?null:reader.GetString(0), Date(reader,1)) : new RemoteSyncCursor();
    }

    public static async Task SaveCursorAsync(RemoteSyncCursor cursor, string consumerId = "desktop")
    {
        await EnsureSchemaAsync(); await using var connection=await DatabaseService.OpenConnectionAsync(); await using var command=connection.CreateCommand(); command.CommandText="INSERT INTO RemoteSyncState(ConsumerId,Cursor,UpdatedAtUtc) VALUES($id,$cursor,$updated) ON CONFLICT(ConsumerId) DO UPDATE SET Cursor=excluded.Cursor,UpdatedAtUtc=excluded.UpdatedAtUtc;"; Add(command,"$id",consumerId); Add(command,"$cursor",cursor.Checkpoint); Add(command,"$updated",DateTime.UtcNow); await command.ExecuteNonQueryAsync();
    }

    public static async Task<IReadOnlyList<RemoteParticipantSessionContract>> GetSessionsAsync(string? conditionId = null, bool completedOnly = false, string? studyId = null, ExperimentRunType? runType = null)
    {
        await EnsureSchemaAsync(); await using var connection = await DatabaseService.OpenConnectionAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SessionId,ParticipantId,StudyId,DeploymentId,ConditionId,GroupId,COALESCE(RunType,'Main'),StartedAtUtc,FeedEndedAtUtc,PostCompletedAtUtc,CompletedAtUtc,CompletionState,LifecycleState FROM RemoteParticipantSessions WHERE ($condition IS NULL OR ConditionId=$condition) AND ($study IS NULL OR StudyId=$study) AND ($runType IS NULL OR COALESCE(RunType,'Main')=$runType) AND ($completed=0 OR CompletionState='CompletedEligible') ORDER BY StartedAtUtc DESC;";
        Add(command,"$condition",conditionId); Add(command,"$study",studyId); Add(command,"$runType",runType is null ? null : RunTypeName(runType.Value)); Add(command,"$completed",completedOnly); var rows = new List<RemoteParticipantSessionContract>(); await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(new RemoteParticipantSessionContract { SessionId=reader.GetString(0),ParticipantId=reader.GetString(1),StudyId=reader.IsDBNull(2)?string.Empty:reader.GetString(2),DeploymentId=reader.GetString(3),ConditionId=reader.GetString(4),GroupId=reader.IsDBNull(5)?null:reader.GetString(5),RunType=ParseRunType(reader.IsDBNull(6)?null:reader.GetString(6)),StartedAtUtc=Date(reader,7),FeedEndedAtUtc=Date(reader,8),PostQuestionnaireCompletedAtUtc=Date(reader,9),CompletedAtUtc=Date(reader,10),CompletionState=Enum.TryParse<RemoteParticipantCompletionState>(reader.GetString(11),out var state)?state:RemoteParticipantCompletionState.Incomplete,LifecycleState=Enum.TryParse<RemoteParticipantLifecycleState>(reader.GetString(12),out var lifecycle)?lifecycle:RemoteParticipantLifecycleState.Incomplete });
        return rows;
    }

    public static async Task<RemoteDashboardMetrics> GetMetricsAsync(string? conditionId = null)
    {
        var sessions = await GetSessionsAsync(conditionId); var completed = sessions.Count(item => item.CompletionState == RemoteParticipantCompletionState.CompletedEligible); var durations = sessions.Where(item => item.CompletedAtUtc.HasValue && item.StartedAtUtc.HasValue && item.CompletionState == RemoteParticipantCompletionState.CompletedEligible).Select(item => (item.CompletedAtUtc!.Value-item.StartedAtUtc!.Value).TotalSeconds).ToArray();
        return new RemoteDashboardMetrics(sessions.Count, completed, sessions.Count - completed, sessions.Count == 0 ? 0 : Math.Round(completed * 100d / sessions.Count, 1), durations.Length == 0 ? null : Math.Round(durations.Average(), 1));
    }

    public static async Task<IReadOnlyList<RemoteTelemetryEvent>> GetEventsAsync(string? conditionId = null, bool completedOnly = false, string? studyId = null, ExperimentRunType? runType = null)
    {
        await EnsureSchemaAsync(); await using var connection = await DatabaseService.OpenConnectionAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT e.EventId,e.ParticipantId,e.SessionId,e.DeploymentId,e.ConditionId,e.ContentId,e.EventType,e.ClientTimestampUtc,e.RelativeMilliseconds,e.PayloadJson,e.SchemaVersion,COALESCE(s.RunType,'Main') FROM RemoteBehavioralEvents e JOIN RemoteParticipantSessions s ON s.SessionId=e.SessionId WHERE ($condition IS NULL OR e.ConditionId=$condition) AND ($study IS NULL OR s.StudyId=$study) AND ($runType IS NULL OR COALESCE(s.RunType,'Main')=$runType) AND ($completed=0 OR s.CompletionState='CompletedEligible') ORDER BY e.SessionId,e.RelativeMilliseconds,e.EventId;";
        Add(command,"$condition",conditionId); Add(command,"$study",studyId); Add(command,"$runType",runType is null ? null : RunTypeName(runType.Value)); Add(command,"$completed",completedOnly); var rows = new List<RemoteTelemetryEvent>(); await using var reader=await command.ExecuteReaderAsync();
        while(await reader.ReadAsync()) rows.Add(new RemoteTelemetryEvent { EventId=reader.GetString(0),ParticipantId=reader.GetString(1),SessionId=reader.GetString(2),DeploymentId=reader.GetString(3),ConditionId=reader.GetString(4),ContentId=reader.IsDBNull(5)?null:reader.GetString(5),EventType=reader.GetString(6),ClientTimestampUtc=Date(reader,7)??DateTime.UnixEpoch,ClientRelativeMilliseconds=reader.GetInt64(8),PayloadJson=reader.IsDBNull(9)?null:reader.GetString(9),SchemaVersion=reader.GetString(10),RunType=ParseRunType(reader.IsDBNull(11)?null:reader.GetString(11)) });
        return rows;
    }

    public static async Task<IReadOnlyList<RemoteQuestionnaireResponseContract>> GetQuestionnaireResponsesAsync(QuestionnaireStage stage, string? conditionId = null, bool completedOnly = false, string? studyId = null, ExperimentRunType? runType = null)
    {
        await EnsureSchemaAsync(); await using var connection = await DatabaseService.OpenConnectionAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT q.ResponseId,q.DeploymentId,q.ParticipantId,q.SessionId,s.ConditionId,s.GroupId,q.QuestionnaireId,q.QuestionnaireVersionId,q.Stage,q.ResponseJson,q.SubmittedAtUtc,COALESCE(s.RunType,'Main') FROM RemoteQuestionnaireResponses q LEFT JOIN RemoteParticipantSessions s ON s.SessionId=q.SessionId OR (q.SessionId IS NULL AND s.ParticipantId=q.ParticipantId) WHERE q.Stage=$stage AND ($condition IS NULL OR s.ConditionId=$condition) AND ($study IS NULL OR s.StudyId=$study) AND ($runType IS NULL OR COALESCE(s.RunType,'Main')=$runType) AND ($completed=0 OR s.CompletionState='CompletedEligible') ORDER BY q.SubmittedAtUtc,q.ResponseId;";
        Add(command,"$stage",stage.ToString()); Add(command,"$condition",conditionId); Add(command,"$study",studyId); Add(command,"$runType",runType is null ? null : RunTypeName(runType.Value)); Add(command,"$completed",completedOnly); var rows=new List<RemoteQuestionnaireResponseContract>(); await using var reader=await command.ExecuteReaderAsync();
        while(await reader.ReadAsync()) rows.Add(new RemoteQuestionnaireResponseContract { ResponseId=reader.GetString(0),DeploymentId=reader.GetString(1),ParticipantId=reader.GetString(2),SessionId=reader.IsDBNull(3)?null:reader.GetString(3),ConditionId=reader.IsDBNull(4)?null:reader.GetString(4),GroupId=reader.IsDBNull(5)?null:reader.GetString(5),QuestionnaireId=reader.GetString(6),QuestionnaireVersionId=reader.GetString(7),Stage=Enum.TryParse<QuestionnaireStage>(reader.GetString(8),true,out var value)?value:stage,ResponseJson=reader.GetString(9),SubmittedAtUtc=Date(reader,10),RunType=ParseRunType(reader.IsDBNull(11)?null:reader.GetString(11)) });
        return rows;
    }

    public static async Task EnsureSchemaAsync()
    {
        if (_initialized) return; await using var connection = await DatabaseService.OpenConnectionAsync();
        foreach (var sql in new[] {
            "CREATE TABLE IF NOT EXISTS RemoteParticipantSessions(SessionId TEXT PRIMARY KEY,ParticipantId TEXT NOT NULL,StudyId TEXT,DeploymentId TEXT NOT NULL,ConditionId TEXT NOT NULL,GroupId TEXT,RunType TEXT NOT NULL DEFAULT 'Main',StartedAtUtc TEXT,FeedEndedAtUtc TEXT,PostCompletedAtUtc TEXT,CompletedAtUtc TEXT,CompletionState TEXT NOT NULL,LifecycleState TEXT NOT NULL,LastSyncedAtUtc TEXT NOT NULL);",
            "CREATE TABLE IF NOT EXISTS RemoteBehavioralEvents(EventId TEXT PRIMARY KEY,SessionId TEXT NOT NULL,ParticipantId TEXT NOT NULL,DeploymentId TEXT NOT NULL,ConditionId TEXT NOT NULL,ContentId TEXT,EventType TEXT NOT NULL,ClientTimestampUtc TEXT NOT NULL,RelativeMilliseconds INTEGER NOT NULL,PayloadJson TEXT,SchemaVersion TEXT NOT NULL);",
            "CREATE TABLE IF NOT EXISTS RemoteQuestionnaireResponses(ResponseId TEXT PRIMARY KEY,DeploymentId TEXT NOT NULL,ParticipantId TEXT NOT NULL,SessionId TEXT,QuestionnaireId TEXT NOT NULL,QuestionnaireVersionId TEXT NOT NULL,Stage TEXT NOT NULL,ResponseJson TEXT NOT NULL,SubmittedAtUtc TEXT);",
            "CREATE TABLE IF NOT EXISTS RemoteSyncState(ConsumerId TEXT PRIMARY KEY,Cursor TEXT,UpdatedAtUtc TEXT NOT NULL);" }) { await using var command=connection.CreateCommand(); command.CommandText=sql; await command.ExecuteNonQueryAsync(); }

        // Existing researcher databases predate these cache columns. Additive columns
        // must exist before any index or query references them.
        await EnsureColumnAsync(connection, "GroupId", "TEXT");
        await EnsureColumnAsync(connection, "StudyId", "TEXT");
        await EnsureColumnAsync(connection, "RunType", "TEXT NOT NULL DEFAULT 'Main'");

        foreach (var sql in new[] {
            "CREATE INDEX IF NOT EXISTS IX_RemoteSessions_Condition ON RemoteParticipantSessions(ConditionId,CompletionState);",
            "CREATE INDEX IF NOT EXISTS IX_RemoteSessions_RunType ON RemoteParticipantSessions(RunType,CompletionState);",
            "CREATE INDEX IF NOT EXISTS IX_RemoteEvents_Session ON RemoteBehavioralEvents(SessionId,EventType);" }) { await using var command=connection.CreateCommand(); command.CommandText=sql; await command.ExecuteNonQueryAsync(); }
        _initialized=true;
    }
    private static async Task EnsureColumnAsync(SqliteConnection connection, string columnName, string definition)
    {
        await using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = "PRAGMA table_info(RemoteParticipantSessions);";
            await using var reader = await inspect.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase)) return;
        }
        await using var migrate = connection.CreateCommand();
        migrate.CommandText = $"ALTER TABLE RemoteParticipantSessions ADD COLUMN {columnName} {definition};";
        await migrate.ExecuteNonQueryAsync();
    }
    private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
    private static DateTime? Date(SqliteDataReader reader,int index)=>reader.IsDBNull(index)?null:DateTime.TryParse(reader.GetString(index),out var value)?value:null;
    private static ExperimentRunType ParseRunType(string? value) => string.Equals(value,"Pilot",StringComparison.OrdinalIgnoreCase) ? ExperimentRunType.Pilot : ExperimentRunType.Main;
    private static string RunTypeName(ExperimentRunType value) => value == ExperimentRunType.Pilot ? "Pilot" : "Main";
}

public sealed record RemoteDashboardMetrics(int Started, int Completed, int Incomplete, double CompletionRatePercent, double? MeanCompletedDurationSeconds);
