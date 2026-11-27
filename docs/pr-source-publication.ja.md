# PR sourceの限定公開

[English](pr-source-publication.md)

開発候補2.0.4.0で合意済みGithubie Consumer契約を実装しました。未導入・未リリースです。設計判断は[ADR0026](adr/0026-pr-source-publication-consumer.md)を参照してください。

list_projectsから登録Projectを選びます。専用MCP pull_request_publish_createの必須入力はproject、sourceBranch、destinationBranch、expectedSourceSha（40桁hex）、title、operationId（D形式UUID）、明示booleanのdraftです。descriptionは任意で省略時空文字です。titleは空白だけ/制御文字を拒否し256文字以内、descriptionは65536文字以内です。source/destinationは異なる明示local branch名で、HEAD・revision・full ref・refspecを受けません。branchの大小文字を保持し、UUID/SHAだけ小文字化します。

CLIは同じホストの同じschemaを使います。
```text
moyaictl pull-request-publish-create --project <登録名> --source-branch <local branch> --destination-branch <target> --expected-source-sha <40桁hex> --title <text> --operation-id <D形式UUID> --draft false [--description <text>]
moyaictl pull-request-publish-status --project <登録名> --operation-id <同じD形式UUID>
```

切断/取消後も元UUIDを保持します。未解決操作を新UUIDで回避しません。statusは記録の読取照合だけでref/PRを作成しません。既存pull_request_createのbranch非公開動作は変えません。

createには公開用の両capability/toolを要求します。所有者方式は検証済みowner sessionを使い署名不要です。Assertion方式はrepository.pushとprovider.githubie.pull_request.writeの両scope、statusは専用能力とrepository.readを要求します。未対応Provider・legacy token・能力不足は送信前に拒否します。ProviderのDesktop承認、protected source拒否、exact PR route、clean-tree方針、固定SHAの新規remote ref限定条件は保持し、通常push権限を拡張しません。

共通JSONはrequest_contextと実測Provider dataを区別します。publication_state/pull_request_stateはnot_attempted/created/already_present/failed/unknownを保持します。already_presentは今回作成の証明ではありません。失敗時も検証済み部分結果、PR番号/URL/head SHA、観測remote SHA/時刻、stageを保持します。未取得/不正なdataはprovider_data_available=false、data=nullです。結果不明はrequires_reconciliation=true、retryable=false、check_statusとしてstatus確認を先に行い、createを自動再送しません。CLIは同じJSONを返して失敗時に非0終了します。PR不明時に公開branchを削除してrollbackしません。

ローカルmock成功と候補hashは実GitHub/Provider/native-client受入の代わりになりません。専用fixture、対応候補/pin、現在の認証、具体的な承認対象と回復観測を合意して別途受入します。

