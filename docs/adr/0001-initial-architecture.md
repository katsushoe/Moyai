# ADR 0001: Moyai v1初期アーキテクチャ

## Status

Accepted

## Context

Moyaiは複数クライアントから共有されるProject Stateの正本であり、SQLite、CLI、MCP、外部Providerを明確に分離する必要があります。

## Decision

.NET 8 / C# 12を使用し、Domain、Application、Infrastructure、実行アプリの依存を内向きにします。Domainは外部ライブラリへ依存せず、Applicationが境界インターフェースを定義し、InfrastructureがSQLiteと外部Providerを実装します。CLIと将来のMCP Serverは同じApplication Serviceを使用します。

Moyai管理Projectのライフサイクル変更はMoyaiを優先入口とします。Provider固有機能と読み取り操作はProviderを直接利用できます。MoyaiからGithubbie、Buckettie、KelpieSSHへの正規呼び出しにはProvider別Service TokenをBearer Tokenとして付与し、ProviderはMoyaiの`auth_introspect`でaudience、scope、期限を検証します。MoyaiまたはIntrospectionを利用できない場合、管理Repositoryへの変更操作はFail Closedとします。

## Alternatives

- 単一プロジェクト構成: 境界が曖昧になりCLIとMCPでロジックが分岐するため不採用です。
- ORM: 手書きSQLを求める共通実装規約と、トランザクション境界の明示性を優先して不採用です。

## Impact

プロジェクト数は増えますが、DB、通信、実行形態をDomainから分離できます。

## Security Conditions

Repository ProviderおよびSSHの利用者認証SecretをMoyai DBへ保存しません。SQLiteはローカルパスを外部設定から受け取り、外部公開するMCP Transportはlocalhostへ限定します。

RepositoryやSSHの利用者Credentialは保存しません。内部Service TokenのみをCredentialとして中央管理し、CSPRNGによる256-bit以上の値をProviderごとに発行します。TokenをTool引数、ログ、Event Historyへ公開しません。失効・期限切れTokenは物理削除し、Lifecycle EventにはToken ID、audience、actor、時刻だけを記録します。AIが指定できるフラグはMoyai由来の証明として使用しません。

## Operational Conditions

SQLiteではforeign_keys、WAL、busy_timeoutを起動時に設定します。Migration前BackupはMigration実装時に必須とします。

Token RotationにはGrace Periodを設けず、新Tokenの使用開始後に旧Tokenを即時削除します。起動時、発行・Rotation時、保守Cleanup時に期限切れTokenを削除します。

## Implementation, Tests, and Documentation

DomainのWorkflowを単体テストし、Infrastructureのスキーマ初期化を実SQLiteで検証します。CLIとMCPの組み立ては各実行アプリのコンポジションルートに限定します。
