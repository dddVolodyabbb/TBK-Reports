#define AppName "ТБК Отчёт"
#define AppVersion "1.4.0"
#define AppPublisher "ТБК"
#define AppExeName "TbkReport.exe"

[Setup]
AppId={{8A663E3D-A9EE-4D3D-A32D-53D1E4D93F17}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\ТБК Отчёт
DefaultGroupName=ТБК Отчёт
DisableWelcomePage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
ShowLanguageDialog=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=TBK-Report-v1.4-Setup
SetupIconFile=..\src\TbkReport.Windows\Assets\tbk-app-icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Установщик ТБК Отчёт

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\artifacts\windows-installer-app-self-contained\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\ТБК Отчёт"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\ТБК Отчёт"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Check: ShouldCreateDesktopShortcut

[UninstallDelete]
Type: files; Name: "{app}\.tbk-report-install"

[Code]
var
  DesktopShortcutCheckBox: TNewCheckBox;
  DeleteUserData: Boolean;

function ShouldCreateDesktopShortcut: Boolean;
begin
  Result := (not WizardSilent) and DesktopShortcutCheckBox.Checked;
end;

function DirectoryHasEntries(const DirectoryName: String): Boolean;
var
  FindRecord: TFindRec;
begin
  Result := False;
  if not DirExists(DirectoryName) then
    Exit;

  if FindFirst(AddBackslash(DirectoryName) + '*', FindRecord) then
  begin
    try
      repeat
        if (FindRecord.Name <> '.') and (FindRecord.Name <> '..') then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRecord);
    finally
      FindClose(FindRecord);
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  InstallDirectory: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    Exit;

  InstallDirectory := WizardForm.DirEdit.Text;
  if DirectoryHasEntries(InstallDirectory) and
     not FileExists(AddBackslash(InstallDirectory) + '.tbk-report-install') and
     not FileExists(AddBackslash(InstallDirectory) + '{#AppExeName}') then
  begin
    MsgBox('В выбранной папке уже есть посторонние файлы. Выберите пустую папку или существующую папку ТБК Отчёт.',
      mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\.tbk-report-install'), 'TBK Report', False);
end;

function InitializeUninstall: Boolean;
var
  Answer: Integer;
begin
  if UninstallSilent then
  begin
    DeleteUserData := False;
    Result := True;
    Exit;
  end;

  Answer := MsgBox(
    'Сохранить пользовательские данные после удаления программы?' + #13#10 + #13#10 +
    'Да — сохранить историю, настройки, черновик и экспортированные документы.' + #13#10 +
    'Нет — удалить эти данные.' + #13#10 +
    'Отмена — не удалять программу.',
    mbConfirmation, MB_YESNOCANCEL);

  DeleteUserData := Answer = IDNO;
  Result := Answer <> IDCANCEL;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteUserData then
  begin
    DelTree(ExpandConstant('{localappdata}\TBK Report'), True, True, True);
    DelTree(ExpandConstant('{userdocs}\ТБК Отчёт'), True, True, True);

    { Данные старых переносных и установочных версий. }
    DeleteFile(ExpandConstant('{app}\settings.json'));
    DeleteFile(ExpandConstant('{app}\draft.json'));
    DelTree(ExpandConstant('{app}\History'), True, True, True);
    DelTree(ExpandConstant('{app}\Exports'), True, True, True);
  end;
end;

procedure InitializeWizard;
begin
  WizardForm.Caption := 'Установка ТБК Отчёт';
  WizardForm.SelectDirLabel.Caption := 'Папка установки:';
  { The standard text mentions a «Далее» button, which this one-page installer does not have. }
  WizardForm.SelectDirBrowseLabel.Caption := 'Нажмите «Установить», чтобы продолжить. Если нужна другая папка, нажмите «Обзор».';

  DesktopShortcutCheckBox := TNewCheckBox.Create(WizardForm);
  DesktopShortcutCheckBox.Parent := WizardForm.SelectDirPage;
  DesktopShortcutCheckBox.Left := WizardForm.DirEdit.Left;
  DesktopShortcutCheckBox.Top := WizardForm.DirEdit.Top + WizardForm.DirEdit.Height + ScaleY(24);
  DesktopShortcutCheckBox.Width := WizardForm.DirEdit.Width;
  DesktopShortcutCheckBox.Caption := 'Создать ярлык на рабочем столе';
  DesktopShortcutCheckBox.Checked := True;

  WizardForm.NextButton.Caption := 'Установить';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectDir then
  begin
    WizardForm.PageNameLabel.Caption := 'Установка ТБК Отчёт';
    WizardForm.PageDescriptionLabel.Caption := 'Выберите папку и дополнительные параметры.';
    WizardForm.NextButton.Caption := 'Установить';
  end;
end;
