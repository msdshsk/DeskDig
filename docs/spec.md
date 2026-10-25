# DeskDig 仕様（MVP）

用語は [CONTEXT.md](../CONTEXT.md) の批准語のみを使う。本書の用語集節は抜粋であり、正は CONTEXT.md。

## 1. 目的

4K の大型ディスプレイのように広い画面で、全画面化せずに散らばった多数のウインドウを、マウス操作ひとつで呼び出した一覧から見つけて Activate する。あわせて、記録した Placement の復元と、タスクバー固定アプリ（PinnedApp）の起動を同じ Palette 上で完結させる。

## 2. 対象環境

- Windows 11。マルチモニタ・モニタ間 DPI 差がある環境を含む。
- .NET 9 / WPF（net9.0-windows）。管理者権限は要求しない。
- 単一ユーザーの常駐アプリ。多重起動は禁止（2 つ目は既存を前面に出して終了）。

## 3. 用語集（抜粋）

| 語 | 意味 |
|---|---|
| Palette | Trigger で開く本体 UI。上段 Launcher、下段 WindowEntry 一覧の単一の面 |
| Trigger / TriggerSource | Palette を開く契機 / それを検出する差し替え可能な実装 |
| WindowEntry | 一覧上のウインドウ 1 件（サムネイル・アプリ名・タイトル） |
| Activate | 選んだウインドウを前面に出して入力可能にする |
| RecentActivation | Palette 経由で Activate した順序の記録（実行中のみ） |
| Favorite | Palette 内で上位固定した WindowEntry（WindowMatcher で識別） |
| WindowMatcher | 実行ファイル + 任意のタイトル条件からなる照合規則 |
| Placement / PlacementProfile | 位置・サイズ・モニタの組 / WindowMatcher に紐づけて記録したもの |
| Launcher / PinnedApp | PinnedApp を一覧して起動する機能 / タスクバー固定アプリ 1 件 |

コード・UI 文言に使わない語: ゾーン, 画角, レイアウト, MRU, 履歴, Pin（Palette 内固定の意味で）。詳細は CONTEXT.md の各語の Avoid 欄を参照。

## 4. 機能仕様

### 4.1 Trigger

- TriggerSource は開始・停止・発火通知だけを持つ共通契約に従い、設定で複数を同時に有効化できる。
- MVP の TriggerSource:
  - **DesktopDoubleClick**: デスクトップ余白のダブルクリック。デスクトップアイコン上のダブルクリックは発火しない。どのモニタの壁紙でも同じ。
  - **Hotkey**: グローバルホットキー。既定値は設定で定義し、変更可能。
- Palette 表示中に Trigger が発火したら Palette を閉じる（トグル）。
- 発火位置（マウス座標）を Palette に渡す。Hotkey の場合は現在のマウス座標。

### 4.2 Palette の表示と終了

- 表示位置は発火位置の近傍。画面外にはみ出す場合は同じモニタ内に収まるよう補正する。
- 表示形式は設定で **タイル格子**（既定、4 列）と **縦リスト** を切り替えられる。
- 閉じる条件: WindowEntry を Activate した / PinnedApp を起動した / Palette の外をクリックした / Esc / Trigger 再発火。
- Palette 自身はタスクバーに出さず、Alt+Tab にも出ない。

### 4.3 WindowEntry 一覧

- **列挙対象**: Alt+Tab に表示される条件を満たすトップレベルウインドウ。Palette 自身・タスクバー・デスクトップ・ツールウインドウは除外。
- **表示要素**: ライブサムネイル、アプリアイコン、アプリ名、ウインドウタイトル。最小化中でサムネイルが取れない場合は大きなアプリアイコンで代替する。
- **並び順**（上から）:
  1. Favorite（Favorite 同士は登録順）
  2. 現在の仮想デスクトップにある WindowEntry を、RecentActivation の新しい順。RecentActivation が無いものは OS の重なり順
  3. 区切りを置いて「他のデスクトップ」にある WindowEntry（2 と同じ規則）
  - Favorite は所属デスクトップに関わらず最上段に出る。
- **操作**:
  - シングルクリックで Activate。
  - 矢印キーで選択移動、Enter で Activate。
  - 文字を入力するとアプリ名・タイトルによる部分一致で絞り込む（PinnedApp も同じ文字列で絞り込む）。
  - 右クリックメニュー: 「Favorite に追加 / 解除」「現在の配置を記録」「配置を解除」。
- 一覧は Palette を開くたびに作り直す。開いている間の差分更新は行わない。

### 4.4 Activate

- 対象を前面に出し、キーボード入力を受け取れる状態にする。最小化中なら復元する。
- 対象が他の仮想デスクトップにある場合は、OS の挙動に従ってそのデスクトップへ切り替わる。デスクトップの列挙・命名といった非公開 API は使わない（ADR-0002）。
- 対象の WindowMatcher に一致する PlacementProfile があれば、同じ一連の動作の中で Placement を適用する（§4.5）。
- 成功したら RecentActivation を更新する。RecentActivation は Palette 経由の Activate 以外では更新しない。

### 4.5 Placement / PlacementProfile

- **記録**: 右クリック「現在の配置を記録」で、その WindowEntry の現在の位置・サイズ・モニタを Placement として、WindowMatcher（既定は実行ファイルのみ）に紐づけて保存する。同じ WindowMatcher の既存 PlacementProfile は上書き。
- **WindowMatcher の編集**: 設定画面でタイトル条件（部分一致）を後から追加できる。実行ファイルのみの規則とタイトル条件付きの規則が両方一致する場合は、タイトル条件付きを優先する。
- **適用**:
  - 対象が最大化・最小化中なら通常状態に戻してから適用する。
  - 記録時のモニタが存在しない場合は、最寄りのモニタ内に収まるよう位置を補正する。
  - アプリがサイズ変更を拒否した場合は位置だけ適用し、通知せず続行する。
  - 見えない余白（DWM の拡張フレーム）を補正し、見た目の矩形が記録どおりになるようにする。
- **解除**: 右クリック「配置を解除」で PlacementProfile を削除する。
- アプリ起動時の自動適用は MVP 外（§7）。

### 4.6 Favorite

- 右クリック「Favorite に追加」で、その WindowEntry の WindowMatcher（既定は実行ファイルのみ）を Favorite として保存する。
- Favorite の WindowMatcher に一致する WindowEntry が複数ある場合は、すべて Favorite 段に出す。
- 一致する WindowEntry が 1 件も無い Favorite は一覧に出さない（Launcher から起動する）。

### 4.7 Launcher / PinnedApp

- **取得元**: タスクバーが保持する固定一覧（レジストリ値）を読み、従来型アプリもパッケージ型（MSIX / ストア）アプリもタスクバーの表示順で得る。読めない場合は固定フォルダのショートカットを予備として使う。このツール独自のアプリ登録機能は持たない（ADR-0003）。
- **件数の想定**: 40〜50 件。Palette 上段に小さなアイコンのみを折り返して並べる（複数行）。アプリ名はホバーまたはキーボード選択時に表示する。
- **並び順**: 既定はタスクバーと同じ順。設定で「自分で並べ替える」を選ぶと、Palette 内でドラッグして並べ替えた順を独自に保存し、新たに固定されたものは末尾に追加、固定解除されたものは除去する。
- **更新のタイミング**: Palette を開くたびに読み直す。固定・解除・並べ替えは次に開いたときに反映される。
- **起動/振り替え**: PinnedApp に対応する WindowEntry が 1 件以上あれば、§4.3 の並び順で最上位の 1 件を Activate する。無ければ起動する。
- 文字入力による絞り込みは PinnedApp にも効く。

### 4.8 SettingsShortcut

- **目的**: 階層が深いわりに頻繁に開く Windows の設定ページへ、Palette からマウス操作ひとつで移動する。
- **候補と既定**: 候補は固定の一覧として持ち、設定画面のチェックで表示するものを選ぶ。任意の URI を登録する機能は持たない。

  | 候補 | 行き先 | 既定 |
  |---|---|---|
  | バージョン情報 | ms-settings:about | 表示 |
  | サウンド | ms-settings:sound | 表示 |
  | ネットワークとインターネット | ms-settings:network | 表示 |
  | Wi-Fi | ms-settings:network-wifi | 非表示 |
  | ディスプレイ | ms-settings:display | 非表示 |
  | Bluetooth とデバイス | ms-settings:bluetooth | 非表示 |
  | サウンド（従来のダイアログ） | mmsys.cpl | 非表示 |

- **表示**: Launcher の段の直下に、PinnedApp と区別できる記号アイコンで並べる。ホバーまたは選択で名前を表示する。
- **操作**: クリックで行き先を開き、Palette を閉じる。文字入力による絞り込みは名前と別名（例: 「音量」「Wi-Fi」「バージョン」）に効く。
- **起動**: 行き先をシェルに渡す。設定アプリが既に開いていればそのウインドウが該当ページに切り替わる（OS の挙動）。

### 4.9 常駐と設定

- タスクトレイアイコンを持つ。メニューは「設定を開く」「終了」。
- Windows サインイン時の自動起動は設定でオン/オフ。実体は HKCU の Run キーで、表示する状態はレジストリ（Windows の「スタートアップ」設定による無効化を含む）に合わせる。オンのまま exe の場所が変わっていたら、起動時に登録先を現在の場所へ書き直す。コマンドライン `--autostart on|off` で登録だけを行って終了できる（インストーラーが使う）。
- **設定画面**: 1 行 1 項目の縦長リスト。項目はコード上の宣言から生成し、追加・削除が設定画面側の改修なしに済む構造にする。MVP の項目:
  - 有効な TriggerSource と各設定（Hotkey のキー）
  - 表示形式（タイル格子 / 縦リスト）、格子の列数
  - 自動起動
  - 表示する SettingsShortcut（候補ごとのオン/オフ）
  - PlacementProfile 一覧（WindowMatcher の編集と削除）
  - Favorite 一覧（削除）
- 設定・PlacementProfile・Favorite・PinnedApp の並び順は `%APPDATA%\DeskDig\` 配下の JSON に保存する。RecentActivation は保存しない。

## 5. 非機能要件

- Trigger 発火から Palette 表示まで体感で遅延を感じないこと（目安 100 ms 以内。ウインドウ列挙とサムネイル登録はこの中に収める）。
- 低レベルマウスフックのコールバックは即座に返し、判定や UI は別スレッド/ディスパッチャで行う。
- モニタごとの DPI 差があっても、Palette の位置補正と Placement の座標が正しいこと（Per-Monitor V2）。
- Win32 呼び出しは単一の層に閉じ込め、並び順・絞り込み・WindowMatcher 一致判定・Placement 補正はその層に依存せず単体テストできること。

## 6. 受け入れシナリオ

1. デスクトップ余白をダブルクリックすると、マウス近傍に Palette が開き、開いているウインドウがサムネイル付きで並ぶ。デスクトップアイコンをダブルクリックしても Palette は開かない。
2. ウインドウ A を Palette から Activate し、次にウインドウ B を Activate したあと Palette を開くと、B、A の順に並ぶ。Palette を使わずに C をクリックしても、B、A の順位は変わらない。
3. ブラウザを Favorite に追加すると、以後ブラウザの全ウインドウが最上段に出る。
4. エディタの配置を記録したあと、エディタを手で動かして最大化し、Palette から Activate すると、通常状態で記録どおりの位置・サイズに戻って前面に出る。
5. 他の仮想デスクトップにあるウインドウは区切りの下に表示され、Activate するとそのデスクトップに切り替わる。
6. Launcher に 40 件以上の PinnedApp が並び、ドラッグで順序を変えると再起動後も保たれる。既に開いている PinnedApp を押すと新規起動せず既存ウインドウが前面に出る。
7. ホットキーで開いた Palette に文字を打つと、一致するウインドウと PinnedApp だけが残る。Enter で先頭を Activate する。
8. 文字入力中に Esc で Palette が閉じ、外をクリックしても閉じる。
9. Palette の SettingsShortcut「サウンド」をクリックすると、設定アプリのサウンドページが開いて Palette が閉じる。「音量」と入力すると「サウンド」だけが残る。設定画面で「Wi-Fi」をオンにすると、次に開いた Palette に Wi-Fi が現れる。

## 7. MVP 外

次段階の候補（実装時期は未定）:

- アプリ起動時や再起動時の Placement 自動適用（ウインドウ生成の監視が必要）。
- 仮想デスクトップの名前表示（非公開 API 依存。ADR-0002 の条件を満たす場合のみ）。
- MouseButton / ScreenCorner など追加の TriggerSource。
- 数値入力による Placement の微調整 UI。

構想段階で挙がったが予定に含めていないもの:

- クリップボード履歴。本ツールの本質ではないため対象外とした。

## 8. 試作後に確定した事項

- Palette の既定表示形式はタイル格子（4 列）。縦リストは設定で選べる形で残す。
- PinnedApp は小さなアイコンのみを複数行で折り返して並べ、アプリ名はホバーまたはキーボード選択時に表示する。

## 9. 未解決の問い

（なし）
