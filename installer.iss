; WallArt Installer Script for Inno Setup 6+
; Download Inno Setup from: https://jrsoftware.org/isdl.php
; Compile this script with: ISCC.exe installer.iss

#define AppName "WallArt"
#define AppVersion "1.0.0"
#define AppPublisher "WallArt"
#define AppExeName "WallArt.exe"

[Setup]
AppId={{B2E7F8A1-4C3D-4E5F-9A1B-2C3D4E5F6A7B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=installer_output
OutputBaseFilename=WallArt_Setup
SetupIconFile=Wallart.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "autostart"; Description: "Start WallArt with Windows"; GroupDescription: "Startup:"

[Files]
; Include all published files from the publish directory
Source: "bin\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
; Create autostart shortcut in the Startup folder
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "--autostart"; Tasks: autostart

[Registry]
; Clean up legacy autostart registry key to prevent duplicate startup entries
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "WallArt"; Flags: uninsdeletevalue deletevalue;

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch WallArt"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Run the built-in uninstall cleanup before files are removed
Filename: "{app}\{#AppExeName}"; Parameters: "--uninstall"; Flags: waituntilterminated skipifdoesntexist

[UninstallDelete]
; Clean up any remaining files
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  PicturesPath, AppDataPath: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Remove wallpaper cache from Pictures
    PicturesPath := ExpandConstant('{userpictures}\Wallpaper Art');
    if DirExists(PicturesPath) then
    begin
      if MsgBox('Do you want to delete your saved wallpapers in' + #13#10 + PicturesPath + '?',
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(PicturesPath, True, True, True);
    end;

    // Remove config from AppData
    AppDataPath := ExpandConstant('{userappdata}\WallArt');
    if DirExists(AppDataPath) then
      DelTree(AppDataPath, True, True, True);
  end;
end;
