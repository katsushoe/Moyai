using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Persistence;

/// <summary>許可リストにある非機密項目だけを記録します。</summary>
public sealed class SqliteAssertionAudit(SqliteDatabaseOptions options, TimeProvider clock) : IAssertionAudit
{
    public async Task WriteAsync(AssertionContext context, string keyId, string resultCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO assertion_audit(operation_id,provider_id,project_id,repository_id,scopes_json,key_id,result_code,created_at) VALUES($operation,$provider,$project,$repository,$scopes,$kid,$code,$utc);";
            command.Parameters.AddWithValue("$operation", context.OperationId);
            command.Parameters.AddWithValue("$provider", context.Provider);
            command.Parameters.AddWithValue("$project", context.Project.ToString("D"));
            string resource = context.ProtocolVersion == "2"
                ? $"{context.ResourceKind}:{context.Resource}"
                : context.Repository ?? throw new ProviderAuthenticationException("auth_project_mismatch");
            command.Parameters.AddWithValue("$repository", resource);
            command.Parameters.AddWithValue("$scopes", JsonSerializer.Serialize(context.Scopes));
            command.Parameters.AddWithValue("$kid", keyId);
            command.Parameters.AddWithValue("$code", resultCode);
            command.Parameters.AddWithValue("$utc", clock.GetUtcNow().ToUnixTimeSeconds());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException) { throw new ProviderAuthenticationException("authentication_unavailable"); }
    }
}
