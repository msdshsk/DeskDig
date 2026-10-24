# DeskDig

大型高解像度ディスプレイ上に散らばったウインドウを、マウス操作ひとつで呼び出した一覧から見つけてアクティブ化し、あわせて配置の復元とタスクバー固定アプリの起動を同じ面で完結させるデスクトップツール。この文書は用語集であり、仕様は [docs/spec.md](docs/spec.md) にある。

## Language

### 呼び出し

**Palette**:
Trigger によって表示される本体のポップアップ UI。上段に Launcher、下段に WindowEntry の一覧を持つ単一の面。将来の拡張もここに載る。
_Avoid_: Switcher, Overlay, ポップアップ, Win+Tab 画面

**Trigger**:
Palette を開く契機となる事象。デスクトップダブルクリックが既定。
_Avoid_: ホットキー（Trigger の一種に過ぎない）, Activator

**TriggerSource**:
特定の Trigger を検出する差し替え可能な実装。複数を同時に有効化できる。
_Avoid_: TriggerDriver, ドライバ, フック

### ウインドウ切替

**WindowEntry**:
Palette に並ぶウインドウ 1 件。サムネイル・アプリ名・タイトルを持つ。OS 上の実在ウインドウの読み取り表現。
_Avoid_: WindowItem, Task, タスク, hwnd（実装詳細）

**Activate**:
選んだ WindowEntry を前面に出し入力を受け取れる状態にすること。他の仮想デスクトップにあれば移動を伴う。
_Avoid_: Focus, BringToFront, Switch, 切り替える

**RecentActivation**:
この Palette 経由で Activate した順序の記録。並び順の最優先材料のひとつ。
_Avoid_: MRU, History, 履歴

**Favorite**:
ユーザーが Palette 内で WindowEntry を上位に固定したもの。RecentActivation より優先される。
_Avoid_: Pin, ピン留め（Pin はタスクバー側の語として予約）, Sticky

**WindowMatcher**:
「どのウインドウを指すか」を表す照合規則。実行ファイルの同一性と任意のタイトル条件からなる値オブジェクト。Favorite と PlacementProfile はこれをキーに持ち、再起動後も同じウインドウを指し続ける。
_Avoid_: AppIdentity, WindowRule, hwnd, キー

### 配置

**Placement**:
ウインドウ 1 件の位置・サイズ・所属モニタの組。
_Avoid_: Zone, Layout, 画角, Position, Rect

**PlacementProfile**:
あるアプリ（およびタイトル条件）に対して記録された Placement。Palette から Activate するたびに適用される。
_Avoid_: ゾーンプロファイル, レイアウト, プリセット

### 起動

**Launcher**:
Palette 内で PinnedApp を一覧し起動する機能。起動済みのアプリは Activate に振り替える。
_Avoid_: ランチャーメニュー, AppList, スタートメニュー

**PinnedApp**:
Windows のタスクバーに固定されているアプリ 1 件。ユーザーがこのツールに登録するものではなく、OS の固定状態を読み取ったもの。
_Avoid_: Shortcut, .lnk（実装詳細）, 登録アプリ, Favorite

## Flagged ambiguities

- **「固定」**: タスクバーの固定は **Pin / PinnedApp**、Palette 内の固定は **Favorite**。日本語で「固定」とだけ言われたら確認する。
- **「履歴」**: RecentActivation のこと。将来クリップボード機能を足す場合、その履歴には別語を与える。
- **比喩として退けた語**: 構想段階で既存ツールの名前や「ゾーン」「画角」といった比喩で機能を説明していたが、これらは正式な用語ではない。コード・ファイル名・UI 文言には上記の批准語だけを使う。

## Modeling notes（実装はしない。概念の性質のメモ）

- **値オブジェクト**: WindowMatcher（実行ファイル + 任意のタイトル条件。等価性は値で決まる）、Placement（位置・サイズ・モニタ。負のサイズや存在しないモニタ参照は不変条件違反）。
- **エンティティ**: PlacementProfile（WindowMatcher を識別子とし、Placement を持つ。同じ WindowMatcher の PlacementProfile は 1 件のみ）、Favorite（WindowMatcher を識別子とする。同じ WindowMatcher の Favorite は 1 件のみ）、PinnedApp（OS 上の固定ショートカットを識別子とし、ユーザー定義の並び順だけをこちらで持つ）。
- **読み取り専用の投影**: WindowEntry は OS の状態を映したもので、Palette が開くたびに作り直す。永続化しない。RecentActivation も実行中のみ有効で、WindowEntry の寿命に従う。
- **不変条件**: 「Pin」はこのツールから変更できない（読むだけ）。Favorite と PlacementProfile は独立で、どちらか一方だけ存在してよい。Launcher から起動したアプリも、ウインドウが出た後は通常の WindowEntry として扱い、特別扱いしない。
- **起きてはいけない遷移**: Palette 経由でない操作が RecentActivation を更新すること。Placement の適用が Activate より先に起きること（配置してから前面に出す、ではなく、前面に出す一連の動作の中で配置する）。PinnedApp の並び順をタスクバー側の変更が上書きすること（新規固定は末尾追加、固定解除は除去のみ）。
- **primitive obsession 候補**: 実行ファイルパスの文字列（WindowMatcher に閉じ込める）、矩形 4 整数 + モニタ名（Placement に閉じ込める）、hwnd の生ポインタ（WindowEntry の内部に閉じ込め、外へ漏らさない）。

## Example dialogue

> **Dev**: デスクトップをダブルクリックしたら何が出るんでしたっけ。
> **Expert**: Palette が出る。ダブルクリックは Trigger のひとつで、検出しているのは DesktopDoubleClick という TriggerSource。
> **Dev**: Palette の WindowEntry 一覧の並び順は？
> **Expert**: Favorite が最上段。次に RecentActivation の新しい順。残りは OS の重なり順。
> **Dev**: ブラウザを選んだら左半分に飛びましたけど。
> **Expert**: そのアプリに PlacementProfile が記録されているから。Activate のたびに Placement を適用している。
> **Dev**: Launcher から Excel を押したら新しく起動せずに既存のが前に出ました。
> **Expert**: PinnedApp に対応するウインドウが既にあれば Launcher は起動ではなく Activate に振り替える。タスクバーと同じ振る舞い。
