namespace Moyai.Infrastructure.Persistence;

/// <summary>単一Schema VersionへのMigrationを表します。</summary>
public sealed record SqliteMigration(int Version, string Sql);
