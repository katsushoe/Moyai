# Repository Provider Contract

状態: 確定（2026-09-27、ユーザー承認）
対象: Moyaiから委譲されるRepository Provider（Githubie、Buckettie、今後追加するProvider）
関連: [Provider Consumer Contract](provider-authentication-consumer-contract.md)、`src/Moyai.Infrastructure/Providers/RepositoryProviderContract.cs`、CR `CR-2026-09-27-provider-git-remote-resolution`

本書は、Providerの認証以外の共通契約を定めます。認証・認可はProvider Consumer Contractを正本とします。

## Gitリモートの解決

### 1. 解決規則

Providerは、ローカルリポジトリのGit設定（`remote.<name>.url`）から、Projectの`repositoryUrl`と一致するリモートを自動で選びます。

- URLの比較は、正規化した値の完全一致とします。正規化はMoyaiのAssertion Repository Claimと同じ規則です。
  - `git@host:owner/repo` 形式は `ssh://git@host/owner/repo` とみなします。
  - 接続方式（`https`／`ssh`）、`.git`接尾辞、末尾の`/`は比較対象から除きます。ホスト名は小文字化し、既定以外のポートは残します。パスの大文字小文字は維持します。
  - 資格情報（`user:password@`）、query、fragmentを含むURLは、比較せずに拒否します。
  - 例: `https://bitbucket.org/stk2k/cupperpro.git` と `git@bitbucket.org:stk2k/cupperpro.git` は、どちらも `bitbucket.org/stk2k/cupperpro` として一致します。
- Providerが自身のRepository登録でリモート名を保持している場合は、`gitRemoteName`の指定がないときに限り、そのリモートを最初の候補にしてよいものとします。この場合も下記3と同じ検証を行い、URLが一致しなければ`provider_remote_mismatch`、存在しなければ`provider_remote_not_found`で拒否します。自動解決や`origin`へは退避しません（2026-09-27追記、Buckettie 1.3.36.0の実装に合わせて明確化）。
- 一致するリモートが複数ある場合は、次の順に選びます。
  1. `gitRemoteName`が指定されていれば、その名前（下記3の検証を行います）。
  2. 下記2の命名規則に合う名前が1つだけあれば、その名前。
  3. それでも一意に決まらなければ、`provider_remote_ambiguous`で拒否します。
- 一致するリモートがない場合は、`provider_remote_not_found`で拒否します。既定値`origin`への暗黙の退避は行いません。
- 解決は、操作ごとにProviderが実行直前に行います。解決結果（リモート名）は、Providerの応答に含めてよいものとします。

### 2. リモート名の命名規則（推奨）

`<ホスト>-origin-<接続方式>` とします。ホストはサービス名の短縮形です。

- 例: `bitbucket-origin-https`、`bitbucket-origin-ssh`、`github-origin-https`
- 既存リポジトリの改名は任意です。上記1の自動解決により、既存の`origin`もURLが一致すれば利用できます。
- Providerが対応しない接続方式（例: BuckettieのSSH）のリモートは、解決候補から除外してよいものとします。除外によって候補がなくなった場合や、指定名のリモートが未対応の接続方式だった場合は、`provider_remote_not_found`です。Provider固有の詳細（例: `ssh_remote_not_supported`）は、`error.provider.code`で示してよいものとします。

### 3. `gitRemoteName`の扱い

- 省略可能とします。省略時は、上記1の自動解決を行います。
- 指定された場合は、その名前のリモートを使います。そのリモートの正規化URLが`repositoryUrl`と一致しなければ、`provider_remote_mismatch`で拒否します。指定名のリモートが存在しない場合は、`provider_remote_not_found`です。

### 4. 対応の表明

各Providerは、`*_provider_capabilities`の`data`に次を含めて対応を表明します。

```json
{ "remote_resolution": { "version": 1, "mode": "repository_url" } }
```

- `remote_resolution`がないProviderは、本規則に未対応とみなします。Moyaiは未対応Providerにも従来どおり委譲しますが、利用者には未対応であることを示します。
- 本規則の改訂時は、`version`を上げます。

### 共通エラーコード

| コード | 意味 |
|---|---|
| `provider_remote_not_found` | `repositoryUrl`と一致するリモート、または指定名のリモートがない |
| `provider_remote_ambiguous` | 一致するリモートが複数あり、命名規則でも一意に決まらない |
| `provider_remote_mismatch` | 指定された`gitRemoteName`のURLが`repositoryUrl`と一致しない |

## Moyai側の実装方針

- 現行のMoyaiは`gitRemoteName`を保持していますが、Provider呼び出しの引数には含めていません（Providerへ渡すのはRepository IDのみ）。リモートの選択は、現状も各ProviderがRepository登録に基づいて行っています。
- `Project.GitRemoteName`を省略可能（`null`許容）に変更し、新規Projectの既定値`origin`を廃止します。
- 既存Projectの`git_remote_name='origin'`は、利用者が明示した値と区別できないため、移行時に`null`（自動解決）へ変更します。`origin`以外の値は、明示指定として保持します。
- `gitRemoteName`が設定されている場合だけ、Provider呼び出しの引数`remote`として渡します。未対応Providerには送りません（`remote_resolution`で判定します）。
- 共通エラーコード3種を、`RepositoryProviderContract`の既知コードへ追加し、そのまま利用者へ返します。
- 実装はMoyaiの次版で行います（2026-09-27時点で未実装）。
