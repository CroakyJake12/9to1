using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class KnowledgeLibraryService
{
    // SAME maintained SQL and parameter values used by both ordinary UpsertAsync and
    // the protected source writer. These helpers configure commands; they grant no access.
    private static void ConfigureCanonicalRecordUpsert(SqliteCommand command, KnowledgeRecord record, string sourcesJson)
    {
        command.CommandText = """
            INSERT INTO knowledge_records(id,category,topic,title,summary,privacy_class,confidence,is_pinned,created_at,updated_at,expires_at,learned_because,sources_json)
            VALUES($id,$category,$topic,$title,$summary,$privacy,$confidence,$pinned,$created,$updated,$expires,$because,$sources)
            ON CONFLICT(id) DO UPDATE SET category=excluded.category,topic=excluded.topic,title=excluded.title,summary=excluded.summary,
              privacy_class=excluded.privacy_class,confidence=excluded.confidence,is_pinned=excluded.is_pinned,updated_at=excluded.updated_at,
              expires_at=excluded.expires_at,learned_because=excluded.learned_because,sources_json=excluded.sources_json;
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$category", (int)record.Category);
        command.Parameters.AddWithValue("$topic", record.Topic);
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$summary", record.Summary);
        command.Parameters.AddWithValue("$privacy", (int)record.PrivacyClass);
        command.Parameters.AddWithValue("$confidence", record.Confidence);
        command.Parameters.AddWithValue("$pinned", record.IsPinned ? 1 : 0);
        command.Parameters.AddWithValue("$created", record.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", record.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$expires", record.ExpiresAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$because", record.LearnedBecause);
        command.Parameters.AddWithValue("$sources", sourcesJson);
    }
    private static void ConfigureCanonicalRecordDetailsUpsert(SqliteCommand details, KnowledgeRecord record)
    {
        details.CommandText = """
            INSERT INTO knowledge_record_details(id,freshness,last_confirmed_at,scope,status,origin,user_correction,supersedes_id,
                knowledge_bank_id,last_reinforced_at,last_used_at,is_user_locked,app_id,project_id,agent_id)
            VALUES($id,$freshness,$confirmed,$scope,$status,$origin,$correction,$supersedes,
                $bank,$reinforced,$used,$locked,$app,$project,$agent)
            ON CONFLICT(id) DO UPDATE SET freshness=excluded.freshness,last_confirmed_at=excluded.last_confirmed_at,
              scope=excluded.scope,status=excluded.status,origin=excluded.origin,user_correction=excluded.user_correction,
              supersedes_id=excluded.supersedes_id,knowledge_bank_id=excluded.knowledge_bank_id,
              last_reinforced_at=excluded.last_reinforced_at,last_used_at=excluded.last_used_at,
              is_user_locked=excluded.is_user_locked,app_id=excluded.app_id,project_id=excluded.project_id,agent_id=excluded.agent_id;
            """;
        details.Parameters.AddWithValue("$id", record.Id.ToString());
        details.Parameters.AddWithValue("$freshness", (int)record.Freshness);
        details.Parameters.AddWithValue("$confirmed", record.LastConfirmedAt?.ToString("O") ?? (object)DBNull.Value);
        details.Parameters.AddWithValue("$scope", record.Scope);
        details.Parameters.AddWithValue("$status", (int)record.Status);
        details.Parameters.AddWithValue("$origin", (int)record.Origin);
        details.Parameters.AddWithValue("$correction", record.UserCorrection ?? (object)DBNull.Value);
        details.Parameters.AddWithValue("$supersedes", record.SupersedesId?.ToString() ?? (object)DBNull.Value);
        details.Parameters.AddWithValue("$bank", record.KnowledgeBankId?.ToString() ?? (object)DBNull.Value);
        details.Parameters.AddWithValue("$reinforced", record.LastReinforcedAt?.ToString("O") ?? (object)DBNull.Value);
        details.Parameters.AddWithValue("$used", record.LastUsedAt?.ToString("O") ?? (object)DBNull.Value);
        details.Parameters.AddWithValue("$locked", record.IsUserLocked ? 1 : 0);
        details.Parameters.AddWithValue("$app", record.AppId ?? (object)DBNull.Value);
        details.Parameters.AddWithValue("$project", record.ProjectId ?? (object)DBNull.Value);
        details.Parameters.AddWithValue("$agent", record.AgentId ?? (object)DBNull.Value);
    }
}
