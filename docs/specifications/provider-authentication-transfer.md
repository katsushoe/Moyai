# Provider認証仕様の受け取り結果

- 受領日: 2026-09-06
- CR: `CR-2026-09-06-provider-authentication-secret-storage`
- 依頼元: Obsidian
- 状態: 2026-09-06に実装承認を受領。Moyai側の共通認証・保存・通信・鍵管理を実装し、2026-09-07にGithubie／BuckettieへProvider組み込みの着手を依頼しました。環境横断の検証とProvider側完了回答待ちで、CR全体は未完了です。
- 移管先: [Provider_Authentication_Specification.md](Provider_Authentication_Specification.md)
- SHA-256: `87C4F0D93A984E9961C2554CAF36083D89B10FDF2DC81D81FCCE851D671B3E99`
- 原文はバイト単位で保持し、CR指定Hashとの一致を確認しました。
- 文書索引: RepositoryルートのREADME.mdとREADME.ja.mdのDocumentation。
- 正本の優先順位: ADR 0001とSECURITY.mdから移管仕様を参照し、旧Service Token方式を移行前の現行実装として区別しました。
- 実装Commit: なし。
- 認証テスト: 67件合格（共通Protocol、Storage、Rotation、CNG、隔離MCP通信、移行期限、監査）。
- 最終回帰検証: 全303件合格（Domain 40件、Infrastructure 198件、MCP 65件）、失敗0件・スキップ0件。
- 全体Build: 警告0・エラー0。
- 文書検証: Markdown 12ファイル、JSON 10例のパーサー検証に合格。移管仕様のHash一致を再確認しました。

## 既存v1仕様との関係

関連する`Moyai_v1_Specification.md`はObsidian管理であり、このRepositoryには存在しません。原文の「関連仕様」はProvider認証仕様を正本として既に参照しています。Obsidian側の文書は変更せず、移管通知で参照先の更新を依頼します。移管仕様にある同ファイル名は既存文書への名称参照です。

## 承認と残作業

CRの完了条件は「実装、MSI作成、インストール、サービス更新、Releaseは別途承認を要する工程として分離し、承認前に実行しない」と定めています。実装に続き、2026-09-07にユーザーからバージョンアップと実機インストールの承認を受け、1.3.0.0のWiX MSI作成・インストール・サービス更新を完了しました。公開Releaseは実行していません。

Release構成の303テスト、MSI構造・隔離起動検証、サービス接続CLIの57項目が合格しました。実機のMSI終了コードは0、サービスはRunning、サービス応答と実行ファイルのVersionは1.3.0.0です。設定ファイルのSHA-256は更新前後で一致し、DB整合性検査に合格、既存23テーブルの件数を保持してSchema 5へ移行しました。更新前の設定・DBはインストール先の`data/backups/upgrade-1.3.0.0-20260906T151916Z`へ保存しました。

ローカルMSIは`artifacts/v1.3.0.0/installer/Moyai-1.3.0.0-x64.msi`、SHA-256は`E80032F843FA9A72D2AB0BD2B18032BD4B573D471FD56FB8BEFECDB014BA12C8`です。インストール時点の実機設定には`providerAuthentication`がなく、設定を保持しました。Repository操作は新認証の構成まで拒否されます。Provider間の実結合検証・Trust設定完了を意味しません。

バージョン更新したXMLとMarkdownはパーサー検証済みです。CIのYAMLはバージョン文字列のみ更新しましたが、ローカル環境にYAMLパーサーがなく構文解析は未検証です。

Assertion発行・共通検証、SQLite Replay Cache、Signing Key管理、Envelope Store、CNG/Keychain/Secret Service/Broker Adapter、期限付きLegacy移行設定、監査、MCPとCLI共通の鍵管理6操作を追加しました。詳細は[運用Contract](provider-authentication-operations.md)を参照してください。

GithubieとBuckettieへ`CR-2026-09-06-moyai-assertion-validation`を送信済みです（Itoguruma Message ID: Githubie `de6d8339e4a0436eb0f077e5bd6b954c`、Buckettie `95735cac28c947579b1a286f5b8614ac`）。Provider側UUID対応、共通Validator組み込み、dual-accept/旧Token拒否、実サービス隔離結合テストが残ります。

2026-09-07にユーザーがこの残作業を選択したため、両Project Inboxへ着手依頼を送信しました（Itoguruma Message ID: Githubie `3a99de4a1368487eb848ae5aec7fa1c`、Buckettie `8e277380eb104156806d1ab0d0d559ad`）。次のMoyai側作業は、各ProviderからUUID対応方式・テスト結果・実サービス結合条件の回答を受領してから行います。

同日にKelpieSSHのLifecycle Provider通信もAssertionへ移行する追加指示を受け、共有CR `CR-2026-09-07-moyai-lifecycle-assertion`を起票しました。現行Moyaiの単一`server_deploy`契約とKelpieSSHの段階Deploy契約に差があるため、Target Resource Claim、正規Audience、Tool Scope、段階実行の合意を完了条件に含めています。2026-09-07にItogurumaへ正規Project Inbox `kelpiessh`を登録し、正式なchange requestとして送信しました（Message ID: `5ec7f520933b464d8d2579d751a9dbc5`）。

Githubie、Buckettie、KelpieSSHから実装前の契約確認を受領し、[Provider Consumer Contract](provider-authentication-consumer-contract.md)へ回答を集約しました。契約回答を送信し（Itoguruma Message ID: Githubie `0c3c9c58faf648a895f219be3d4ed037`、Buckettie `60a989b426904b7a8b8f71e434eadd0a`、KelpieSSH `1a90785a3f5f43e2a44190ce8c05558d`）、受信3件をack済みです。MoyaiはProvider用共通NuGetの分離と、KelpieSSH用Protocol v2・段階Deploy Adapterを担当します。Provider側は共通パッケージの配布後に組み込みを開始します。

3 ProviderからContract受領完了と共通NuGet配布待ちの通知を受領しました（Itoguruma Message ID: Githubie `7cde3e897b8c44f0b6fc7d33d1bc1b1e`、Buckettie `dd4d4b1ef2d2499bb2d368dcb4126bd0`、KelpieSSH `4f695166d0984fbbb92f1fb0ef3291cc`）。GithubieとBuckettieはRepository Providerの隔離結合条件を確認済みで、KelpieSSHは状態照会Tool名を`deploy_status`とする予定です。Moyaiで`Moyai.ProviderAuthentication` 1.0.0をローカル実装し、専用テスト6件、全体回帰309件、Release Build、NuGet生成、ProjectReferenceを持たない独立ConsumerでのPackageReference復元・実行に合格しました。生成物は`artifacts/provider-authentication/Moyai.ProviderAuthentication.1.0.0.nupkg`、SHA-256は`55683B499606C0F60EC2B2A35CBB1A8169A311A9E3F8A46D488360BCCEAA58CC`です。

2026-09-07に同一HashのNuGetとchecksumを共有CR領域の各Provider Inboxへ配置し、既存CRを`対応中（共通NuGet配布済み）`へ更新しました。Itoguruma配布通知Message IDはGithubie `4785ae8a8491490689e49577a9bde946`、Buckettie `b34f8f947c634e229ca7872d483ad01d`、KelpieSSH `10f813b442bf49709981cba27df13477`です。Moyai側ではKelpieSSH用Protocol v2・段階Deploy Adapterもローカル実装し、段階順序、ToolごとのAssertion、Bootstrap分離、Assertion期限切れ時の1回だけの再試行、既知失敗時の停止、通信失敗・Timeout時の状態照合、Rollback ID維持をテストしました。追加後の全317件（Domain 40件、Provider Authentication 6件、Infrastructure 206件、MCP 65件）はRelease構成で合格しました。Provider側の共通パッケージ組み込み、永続Storeと`deploy_status`、隔離結合テストが残ります。

Githubieの隔離受け入れテストで、Replay予約中にAssertionが期限を越えた場合にPrincipalが返る競合を確認しました。共通ValidatorでReplay予約後に時刻を再検証する1.0.1へ更新し、再現テストを追加しました。専用7件、全318件（Domain 40件、Provider Authentication 7件、Infrastructure 206件、MCP 65件）が合格し、ProjectReferenceを持たない独立Consumerで1.0.1.0の読込・実行を確認しました。NuGet SHA-256は`44C3CAB4464011922973829D9C060E3BD0807692C0AF31FD6040A8E4A3155C3E`です。

1.0.1とchecksumを3 Providerの共有CR Inboxへ再配布し、各CRを更新しました。再検証依頼のItoguruma Message IDはGithubie `971aa19be8894f43a68b34c7e2e662c1`、Buckettie `35c8f96f023e486aa8f45bbf39ecbfc9`、KelpieSSH `db55344b1a404e008ed46cc7fa1728c1`です。Provider側の1.0.1受け入れ結果を待っています。

隔離結合用Moyaiを既存サービスとは別のloopback port、DB、CNG namespace、Issuerで構成しました。公開調整情報は`artifacts/provider-authentication/integration/coordination.json`、公開Trust Bundleは同領域の`trust/moyai-integration-trust.json`です。2 Project UUIDとBuckettie正規化Repository、KelpieSSH Target IDを登録し、秘密鍵は専用CNG鍵でWrapしたまま隔離DBへ保持します。BuckettieとKelpieSSHへ公開値を回答しました（Itoguruma Message ID: Buckettie `0fe0575d883a438a83adcd0af7d76d32`、KelpieSSH `3aeb1c90ae084780854f02bb4220527c`）。Githubieへ1.0.1の実行直前検証契約を再回答しました（Message ID: `8e84921a211245379e62701bb9f30341`）。Provider所有のReplay／Deployment DBと外部CredentialはMoyaiへ複製しません。

1.0.1のProvider再検証は、Githubie 5件、Buckettie 166件、KelpieSSH 825件がすべて合格しました。GithubieからPrincipal返却後に承認待ち等が入る場合の実行直前期限確認APIについて追加確認を受領しており、製品HTTP/Gateway結合完了には含めていません。Buckettie／KelpieSSHには隔離Moyaiの公開値を回答済みで、各隔離Endpointへの反映と実サービス結合が次工程です。

承認待機後のProvider処理直前にReplayを再予約せず、期限・最新Trust鍵状態・Capabilityを再確認する`IAssertionExecutionValidator.EnsureCurrentAsync`を共通パッケージ1.0.2へ追加しました。専用9件、全320件（Domain 40件、Provider Authentication 9件、Infrastructure 206件、MCP 65件）が合格し、独立Consumerで1.0.2.0の公開API読込・実行を確認しました。NuGet SHA-256は`BBBCD73CFD1CA1D005921AEC5208CFEF4E3891587DE2AC1838C3FBCB3C8802A0`です。3 Providerの共有CR Inboxへ配布し、再検証を依頼しました（Itoguruma Message ID: Githubie `4c8cff03e18c45ba92d54ce345f4bfc6`、Buckettie `3e8c6106a1e048279a0e4519937dc24f`、KelpieSSH `0b75d475dccf4227ad26531331c73a1c`）。

Githubieは1.0.2のHash一致を確認し、隔離受け入れ8件を警告・エラーなしで完了しました。`EnsureCurrentAsync`の正常系、待機後期限切れ、待機中鍵失効を含み、拒否2件ではGateway未実行、全3件でReplay予約は初回の1回だけです。共通層の確認事項は解消しましたが、製品HTTP/Gateway、実Trust File/SQLite、Codex／Claude両MCP Clientの結合は未完了です。Moyaiから製品組み込みの続行を依頼しました（Itoguruma Message ID: 進捗回答 `3a9b4eefc72a47f693cf9c8b75f0d80d`、続行依頼 `9574df9962554735b1567fabdc93291d`）。KelpieSSHはLifecycle dispatch・状態応答・キャッシュ返却の直前へ1.0.2の実行時検証を組み込み、期限・鍵失効・Replay・予約解除を扱う追加12件とRelease全837件に合格しました。隔離MCPも1.0.2で再起動し、initialize HTTP 200を確認済みです（Itoguruma Message ID: `6a79577724c842e9a2c0b0dc0da0e39b`）。実サービス結合は次工程です。Buckettieには進捗確認を送信しました（Message ID: `8a26cf05ba9a4e4ba67ca9f0864affc2`）。

2026-09-24にKotodama復旧CR `CR-2026-09-19-kotodama-provider-authentication-recovery`の手順1としてユーザー承認を受け、内部routing名`githubbie`からAssertion正規ID`githubie`への解決修正を含む1.3.1.0のWiX MSIを作成・実機インストールしました。Release全322件合格、MSI SHA-256は`ADE02681EC601B359192BD0A10247B854E34A38C5AC818A016A9278205625D0C`、終了コード0、サービス応答Versionは1.3.1.0です。設定SHA-256は更新前後で一致し、DBはintegrity_check ok・28テーブル件数一致でした。更新前の設定・DBは`data/backups/upgrade-1.3.1.0-20260924T054844Z`へ保存しました。稼働設定の`providerAuthentication`追加、署名鍵初期化、Trust配布は未実施です。公開Releaseは実行していません。

macOS KeychainとLinux Secret Serviceのネイティブ動作・ACL、3 OSでの同一Test Suite、実Broker接続は未検証です。Windows CNGは一時テスト鍵で生成・Rotation・再読込・復号・削除を検証しましたが、別サービスユーザーによる拒否は未検証です。Passphrase fallbackは未提供です。Trust配布と確認は管理操作で、Provider全台への配布を自動検証する仕組みはありません。KelpieSSH Lifecycle通信はMoyai側でProtocol v2へ移行しましたが、Provider側受け入れ完了まで実結合は未検証です。

共有CRの受け取り結果欄とObsidian側の原文・リンクは変更していません。依頼元へ本結果のパスと未実施項目を通知します。実装完了通知としては扱いません。
