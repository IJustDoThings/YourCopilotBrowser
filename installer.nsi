!include "MUI2.nsh"
!include "x64.nsh"

Name "YCB"
OutFile "YCB-Setup.exe"
InstallDir "$PROGRAMFILES64\YCB"
InstallDirRegKey HKLM "Software\YCB" "Install_Dir"
RequestExecutionLevel admin
CRCCheck off

!define MUI_ABORTWARNING
!define MUI_ICON "icon.ico"
!define MUI_UNICON "icon.ico"

; Finish page: checkbox to launch YCB (checked by default)
!define MUI_FINISHPAGE_RUN "$INSTDIR\YCB.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open YCB Browser"
!define MUI_FINISHPAGE_RUN_CHECKED

!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Install"
    ${If} ${RunningX64}
        ${DisableX64FSRedirection}
        SetRegView 64
    ${EndIf}

    ; Kill any running YCB instance so files aren't locked
    nsExec::Exec 'taskkill /F /IM YCB.exe'
    Sleep 1000

    ; Clean up old installs (wrong x86 path and existing install)
    RMDir /r "$PROGRAMFILES32\YCB"
    RMDir /r "$PROGRAMFILES\YCB"
    RMDir /r "$INSTDIR"

    SetOutPath "$INSTDIR"

    ; === WebView2 Runtime ===
    DetailPrint "Checking for WebView2 Runtime..."
    ReadRegStr $0 HKLM "SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
    StrCmp $0 "" 0 webview2_ok
    ReadRegStr $0 HKLM "SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
    StrCmp $0 "" 0 webview2_ok

    DetailPrint "Installing WebView2 Runtime..."
    SetOutPath "$TEMP"
    File "MicrosoftEdgeWebview2Setup.exe"
    nsExec::ExecToLog '"$TEMP\MicrosoftEdgeWebview2Setup.exe" /silent /install'
    Delete "$TEMP\MicrosoftEdgeWebview2Setup.exe"

webview2_ok:
    DetailPrint "WebView2 Runtime ready."
    SetOutPath "$INSTDIR"

    ; === Copy Application Files ===
    DetailPrint "Installing YCB..."
    File /r "publish\*"

    ; === Create persistent user ID in AppData if one doesn't already exist ===
    DetailPrint "Setting up user profile..."
    nsExec::ExecToStack 'powershell -NoProfile -Command "$p=[System.IO.Path]::Combine($env:APPDATA,\"YCB-Browser\"); New-Item -Force -ItemType Directory $p | Out-Null; $f=[System.IO.Path]::Combine($p,\"user_id.txt\"); if (-not (Test-Path $f)) { [System.Guid]::NewGuid().ToString() | Set-Content $f -NoNewline }"'
    Pop $0
    Pop $0

    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateDirectory "$SMPROGRAMS\YCB"
    CreateShortcut "$SMPROGRAMS\YCB\YCB App.lnk" "$INSTDIR\YCB.exe"
    CreateShortcut "$SMPROGRAMS\YCB\Uninstall YCB.lnk" "$INSTDIR\Uninstall.exe"

    Delete "$DESKTOP\YCB Browser.lnk"
    Delete "$DESKTOP\YCB.lnk"
    Delete "$DESKTOP\YCB-Browser.lnk"
    Delete "$DESKTOP\YCB App.lnk"
    CreateShortcut "$DESKTOP\YCB App.lnk" "$INSTDIR\YCB.exe"

    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YCB" "DisplayName" "YCB"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YCB" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YCB" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YCB" "Publisher" "YCB"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YCB" "DisplayVersion" "1.0.0"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YCB" "DisplayIcon" "$INSTDIR\YCB.exe"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YCB" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YCB" "NoRepair" 1

    ${If} ${RunningX64}
        ${EnableX64FSRedirection}
    ${EndIf}
SectionEnd

Section "Uninstall"
    ${If} ${RunningX64}
        ${DisableX64FSRedirection}
        SetRegView 64
    ${EndIf}

    RMDir /r "$INSTDIR"
    Delete "$SMPROGRAMS\YCB\*.*"
    RMDir "$SMPROGRAMS\YCB"
    Delete "$DESKTOP\YCB App.lnk"
    Delete "$DESKTOP\YCB.lnk"
    Delete "$DESKTOP\YCB Browser.lnk"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YCB"
    DeleteRegKey HKLM "Software\YCB"

    ${If} ${RunningX64}
        ${EnableX64FSRedirection}
    ${EndIf}
SectionEnd