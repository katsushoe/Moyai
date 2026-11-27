# 実行時診断とトレイのホスト操作

[English](runtime-diagnostics.md) | [日本語](runtime-diagnostics.ja.md)

開発候補2.0.4.1では、同じWindowsユーザー・ログオンセッションのトレイを1個に制限します。追加起動はアイコン作成やホスト接続の前に正常終了し、`--autostart` の追加起動でもホストを操作しません。トレイ終了後は再起動でき、異常終了で古いロックファイルが残ることもありません。既存のホスト認証と自動起動設定は保持します。旧版トレイは新しい起動判定に参加しないため、初回更新時は旧版を閉じてから修正版を起動してください。

2.0.3.2ではトレイの状態表示を、タスクバーとAlt+Tabから選択できる通常の画面に変更しました。状態画面を表示したままホストを再起動でき、操作後は表示を更新します。「閉じる」・Esc・ウィンドウの閉じる操作は状態画面だけを閉じます。トレイの終了は専用メニューから行います。操作メニューを無効にするのはCLI処理の実行中だけです。

開発候補2.0.2.0ではMCP `runtime_diagnostics` とCLI `moyaictl runtime-diagnostics [--project <登録名>]` を追加します。稼働中ホストが実際に読み込んだ設定を診断します。既存のMCP HTTP認証は引き続き必要です。Project選択前に `list_projects` で登録名を確認してください。

出力はconfiguration（設定/DBパス、読込日時、読込時/現在の更新日時、status、nullableなrestartRequired）、Provider名・prefix・endpointのorigin・接続/認証方式・repository対応・最小認証状態、任意のProject経路診断です。endpointのpath/query/userinfo、token、scope辞書、比較hash、署名鍵/KEK一覧、設定全体は返しません。Provider接続、discovery、署名、鍵作成、設定再読込は行いません。

所有者方式は `signature_not_required`（署名不要）です。署名方式では既存DBの公開状態だけを読み、issuer未設定、有効鍵なし/複数/期限切れ/開始前、protectorなし、metadata取得不能を区別します。`configured` は公開状態とCNG鍵の存在だけを確認した意味で、署名成功やProvider側の信頼を保証しません。他方式のprotectorは `configured_not_verified` です。legacyはtokenを読まず、期間の有効性だけを表示します。鍵管理APIは追加しません。

設定の内容が一致すれば `unchanged`、異なれば `restart_required` です。更新日時だけでは判定しません。ファイル欠落、読取不能、4 MiB超過ではmissing/unavailable・restartRequired=nullとし、反映済みとは扱いません。ホストは読込済み設定を保持し、自動再読込・再起動しません。

`moyaictl host status` にも、同一Windowsユーザー専用pipeを通して設定状態とホスト起動日時を表示します。旧ホストで項目が欠ける場合は、ホスト更新後に比較できます。Projectの `provider_not_configured` / `provider_repository_disabled` は、読込済み設定の確認と明示的な再起動を案内します。診断結果は操作権限の付与ではありません。

トレイの「MCPホストを再起動」は既存CLIの設定検証→停止ACK→対象PID終了待ち→起動→応答確認を使います。「ホストの状態を表示」で設定表示を更新します。メニューの設定状態は、トレイ開始時・状態表示・ホスト操作時に取得した最終観測値です。ファイル監視ではありません。操作を直列化し、CLI期限を120秒に制限し、自動再送しません。結果不明の場合は状態確認後に再操作してください。「トレイを終了」はホストを停止しません。ログオン時の自動起動設定は独立しています。

Project情報は専用の読取専用DB接続を使います。読取不能時は生のDBエラーを返さず `project_metadata_unavailable` とし、欠落DBを作成しません。ローカルの実装・テスト・publishと、導入後の実ホスト/GUI受入は別です。設計・セキュリティ条件は [ADR0024](adr/0024-runtime-diagnostics-and-tray-host-restart.md) を参照してください。
