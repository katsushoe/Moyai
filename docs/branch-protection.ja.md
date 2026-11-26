# ホスティング側ブランチ保護

[English](branch-protection.md) | [日本語](branch-protection.ja.md)

開発候補2.0.3.0では、Githubieのclassic exact-branch保護契約に沿うMCPとサービス接続CLIを追加します。list_projectsで登録Project名を確認し、Provider Repository IDの登録に従って対象を解決します。稼働Providerの対応、実GitHubの権限/プラン、Desktop画面操作、実相互受入は別の受入事項です。

| MCP | CLI | 入力例 |
| --- | --- | --- |
| branch_protection_get | moyaictl branch-protection-get | --project Example --branch main |
| branch_protection_update | moyaictl branch-protection-update | --project Example --branch main --edit '{"enforce_admins":true}' |
| branch_protection_delete | moyaictl branch-protection-delete | --project Example --branch main |

例は構文説明であり、実リポジトリの操作承認ではありません。変更には具体的な本人指示とGithubieのDesktop承認が必要で、自動承認フラグはありません。新規作成を許可する場合は `--create-if-missing true` を明示します。既存保護が不明・参照不能な状態を上書きする許可ではなく、安全に作成できるかはProviderがブランチ状態と設定を照合します。

editはsnake_caseで、require_pull_request、required_approving_reviews（0～6）、allow_force_pushes、allow_deletions、enforce_admins、require_status_checks、status_check_contexts、strict_status_checksです。省略/nullは保持、false/0は明示変更です。未知項目、空edit、不正/wildcard branch、矛盾入力、重複/空/制御文字を含むcheck名は拒否します。check有効化にはcontexts配列が必要で、レビュー/checkを無効化しながら従属項目を編集できません。新規作成ではレビュー/承認数・force・削除・管理者・checkの判断をProviderへ明示する必要があります。既存のGitHub App bindingをcontexts置換で失う変更はProviderが拒否します。

共通結果はok/operation/project/provider/protection/error、protectionKind=classic_exact_branchです。読取はbranch/canUpdateと共通policy項目を返し、未知/再現不能な設定ではcanUpdate=false・policy=nullとなります。変更はbranch/changed/policyを返し、no-op成功はchanged=falseです。成功やno-opを、自分がremoteを変更した証明や承認画面表示の証明に読み替えません。approvedフラグは捏造しません。Provider完全policy、生のAPI診断、principal一覧、資格は返さず、読取結果を完全置換の本文として使えません。

branch_protection_read/writeの明示能力が必要です。所有者方式ではowner操作一覧とtools/listも照合します。署名方式ではgetにrepository.read、update/deleteに専用provider.githubie.branch_protection.writeと登録Project bindingを要求します。旧service tokenや一般repository.writeへ代替せず、通常設定・scope・grantを自動更新しません。Buckettie等の非対応Providerは能力false・操作送信前拒否です。Provider更新だけで操作の承認を得たことにはなりません。

変更前後のDesktop承認、対応済み既存項目の保持、認証付き再読取による競合検知、GitHub Administration権限・プランの検査はGithubieが担当します。403はbranch_protection_access_deniedとして権限とプランの両方の確認を案内し、断定的に分離しません。404はbranch_protection_not_found_or_inaccessibleで、無保護の証明ではありません。未知/保持不能なsecurity設定は送信前拒否し、再読取と書込の間の外部変更をRESTで原子的に防ぐ保証はありません。

Moyaiは変更要求前の永続intentと、その後の結果をrepository_operation_audit（schema8）へ保存します。項目はoperation ID・Project UUID・正規Provider/操作・branch SHA256・安全なoutcome/code・UTC時刻だけで、policy/要求本文/資格は保存しません。pending intentだけでは実行・未実行のどちらも証明できません。事前監査不能なら未送信です。結果監査に失敗した場合はoperation_audit_result_unknownとして独立照合を要求し、監査を補うために変更要求を再送してはいけません。

update/deleteは署名期限切れも含め自動再送しません。通信断、不正/矛盾応答、不確定な変更はoutcome=unknown・retryable=falseの非成功です。pendingも成功扱いにしません。後続の本人承認操作を決める前にgetで独立照合してください。errorは安全なcode/outcome/suggestedAction/correlationだけを保持し、生の診断文を返しません。業務失敗時のCLI終了コードは1です。

Githubieのprotected_branchesはgatewayのpush方針で、ホスティング側classic保護とrulesetは別です。classic ruleを削除してもrulesetやProviderのpush方針は変更しません。ローカル実装によって実ホスト・grant・保護設定・Repository・MSI・公開状態を変更していません。設計正本: [ADR0025](adr/0025-hosting-branch-protection.md)。
