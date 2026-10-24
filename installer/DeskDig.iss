; DeskDig のインストーラー定義（Inno Setup 6）
;
; ユーザー単位（管理者権限不要）で %LocalAppData%\Programs\DeskDig に配置する。
; 配置するのは自己完結型の単一実行ファイルなので、.NET ランタイムの追加インストールは不要。
; 自動起動の登録はアプリの設定画面が HKCU の Run キーに対して行い、インストーラーはアンインストール時に後始末だけを行う。
;
; ビルド例（リリースワークフローが実行する）:
;   iscc /DMyAppVersion=0.2.0 /DSourceDir=..\out\self-contained /DOutputDir=..\dist installer\DeskDig.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\self-contained"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define MyAppName "DeskDig"
#define MyAppPublisher "msd.shsk"
#define MyAppURL "https://msdshsk.github.io/DeskDig/"
#define MyAppRepo "https://github.com/msdshsk/DeskDig"
#define MyAppExeName "DeskDig.exe"

[Setup]
AppId={{B7D2E4C1-5A6F-4E3B-9C8D-2F1A0E7B6D45}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppRepo}/issues
AppUpdatesURL={#MyAppRepo}/releases
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} Setup
LicenseFile=..\LICENSE
SetupIconFile=..\src\DeskDig\Assets\DeskDig.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; ユーザー単位のインストール。{autopf} は管理者権限なしでは %LocalAppData%\Programs になる
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

OutputDir={#OutputDir}
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; 上書きインストール時に起動中の DeskDig を閉じる
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.AutoStartTask=Windows サインイン時に DeskDig を自動起動する
english.AutoStartTask=Start DeskDig when you sign in to Windows
japanese.KeepSettings=設定ファイル（%APPDATA%\DeskDig）はアンインストール後も残ります。
english.KeepSettings=Your settings in %APPDATA%\DeskDig are kept after uninstall.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStartTask}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; 自動起動の登録（タスクを選んだ場合）。アプリ側の設定画面でも同じ値を読み書きする
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue
; タスクを選ばなかった場合も、アンインストール時には登録を外す
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#MyAppName}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: none; ValueName: "{#MyAppName}"; Flags: uninsdeletevalue

[Run]
; 自動起動を選んだ場合は、アプリ側の設定ファイルにも反映させるための引数を付けて起動する
Filename: "{app}\{#MyAppExeName}"; Parameters: "--autostart on"; Flags: runhidden nowait; Tasks: autostart
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; アンインストール前に常駐中のプロセスを終了する
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "StopDeskDig"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox(CustomMessage('KeepSettings'), mbInformation, MB_OK);
end;
