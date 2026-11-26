@echo off
setlocal

if /I not "%~1"=="--from-temp" (
    set "TEMP_SCRIPT=%TEMP%\update-collab-project-updater-%RANDOM%%RANDOM%.bat"
    copy "%~f0" "%TEMP_SCRIPT%" >nul
    call "%TEMP_SCRIPT%" --from-temp
    set "EXIT_CODE=%ERRORLEVEL%"
    del "%TEMP_SCRIPT%" >nul 2>nul
    exit /b %EXIT_CODE%
)

rem Updates the CLI (CollabProjectUpdater-CLI) in this folder. A plain GUI folder (CollabProjectUpdater.exe) is switched to the
rem self-updating Velopack portable build, which then updates itself from the app's Updates page.
rem Installs from before the rename (FCPModManager / FCPModUpdater) are migrated: the old files are removed after updating.
set "OLD_APP="
set "IS_GUI="
if exist ".\Update.exe" if exist ".\.portable" (
    echo This copy updates itself: open the app's Updates page and click Update now.
    exit /b 0
)
if exist ".\CollabProjectUpdater.exe" (
    set "APP_NAME=CollabProjectUpdater.exe"
    set "OLD_APP=CollabProjectUpdater.exe"
    set "IS_GUI=1"
    set "ARCHIVE=CollabProjectUpdater-win-x64-Portable.zip"
    set "ARCHIVE_DIR="
) else if exist ".\CollabProjectUpdater-CLI.exe" (
    set "APP_NAME=CollabProjectUpdater-CLI.exe"
    set "ARCHIVE=CollabProjectUpdater-CLI-win-x64-selfcontained.zip"
    set "ARCHIVE_DIR=win-x64-sc"
) else if exist ".\FCPModManager.exe" (
    set "APP_NAME=FCPModManager.exe"
    set "OLD_APP=FCPModManager.exe"
    set "IS_GUI=1"
    set "ARCHIVE=CollabProjectUpdater-win-x64-Portable.zip"
    set "ARCHIVE_DIR="
) else (
    set "APP_NAME=FCPModUpdater.exe"
    set "OLD_APP=FCPModUpdater.exe"
    set "NEW_APP=CollabProjectUpdater-CLI.exe"
    set "ARCHIVE=CollabProjectUpdater-CLI-win-x64-selfcontained.zip"
    set "ARCHIVE_DIR=win-x64-sc"
)
set "BASE_URL=https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/latest/download"
set "RELEASES_API=https://api.github.com/repos/FalloutCollaborationProject/FCP-Mod-Updater/releases?per_page=50"
set "STAGING=%TEMP%\CollabProjectUpdater-update-%RANDOM%%RANDOM%"

if not exist ".\%APP_NAME%" (
    echo Run this script from the existing Collaboration Project Updater install folder.
    exit /b 1
)

tasklist /FI "IMAGENAME eq %APP_NAME%" 2>nul | find /I "%APP_NAME%" >nul
if "%ERRORLEVEL%"=="0" (
    echo Collaboration Project Updater is still running. Close it before updating.
    exit /b 1
)

mkdir "%STAGING%" >nul 2>nul
if not exist "%STAGING%" (
    echo Could not create update staging folder.
    exit /b 1
)

echo Select update channel:
echo   1^) Latest stable release
echo   2^) Latest pre-release
set /P "CHANNEL_CHOICE=Choice [1]: "
if "%CHANNEL_CHOICE%"=="" set "CHANNEL_CHOICE=1"

if "%CHANNEL_CHOICE%"=="1" (
    echo Downloading latest stable Collaboration Project Updater release...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest -Uri '%BASE_URL%/%ARCHIVE%' -OutFile '%STAGING%\%ARCHIVE%'; Invoke-WebRequest -Uri '%BASE_URL%/checksums.txt' -OutFile '%STAGING%\checksums.txt'"
) else if "%CHANNEL_CHOICE%"=="2" (
    echo Finding latest Collaboration Project Updater pre-release...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; $headers = @{ 'User-Agent' = 'CollabProjectUpdater' }; $releases = Invoke-RestMethod -Headers $headers -Uri '%RELEASES_API%'; $release = $releases | Where-Object { $_.prerelease -and -not $_.draft } | Select-Object -First 1; if ($null -eq $release) { throw 'No pre-release was found.' }; $archive = $release.assets | Where-Object { $_.name -eq '%ARCHIVE%' } | Select-Object -First 1; $checksums = $release.assets | Where-Object { $_.name -eq 'checksums.txt' } | Select-Object -First 1; if ($null -eq $archive) { throw ('No asset named %ARCHIVE% was found on ' + $release.tag_name + '.') }; if ($null -eq $checksums) { throw ('No checksums.txt asset was found on ' + $release.tag_name + '.') }; Invoke-WebRequest -Headers $headers -Uri $archive.browser_download_url -OutFile '%STAGING%\%ARCHIVE%'; Invoke-WebRequest -Headers $headers -Uri $checksums.browser_download_url -OutFile '%STAGING%\checksums.txt'"
) else (
    echo Invalid selection.
    rmdir /S /Q "%STAGING%" >nul 2>nul
    exit /b 1
)

if not "%ERRORLEVEL%"=="0" (
    echo Download failed.
    rmdir /S /Q "%STAGING%" >nul 2>nul
    exit /b 1
)

set "EXPECTED="
for /f "tokens=1" %%A in ('findstr /I /C:" %ARCHIVE%" "%STAGING%\checksums.txt"') do set "EXPECTED=%%A"

if "%EXPECTED%"=="" (
    echo Could not find checksum for %ARCHIVE%.
    rmdir /S /Q "%STAGING%" >nul 2>nul
    exit /b 1
)

set "ACTUAL="
for /f %%A in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-FileHash -Algorithm SHA256 '%STAGING%\%ARCHIVE%').Hash.ToLowerInvariant()"') do set "ACTUAL=%%A"

if /I not "%ACTUAL%"=="%EXPECTED%" (
    echo Checksum verification failed. Update was not installed.
    rmdir /S /Q "%STAGING%" >nul 2>nul
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Force -Path '%STAGING%\%ARCHIVE%' -DestinationPath '%STAGING%\extract'"
if not "%ERRORLEVEL%"=="0" (
    echo Could not extract update archive.
    rmdir /S /Q "%STAGING%" >nul 2>nul
    exit /b 1
)

rem The CLI archive has a top-level folder; the portable zip does not.
set "SOURCE=%STAGING%\extract"
if not defined IS_GUI set "SOURCE=%STAGING%\extract\%ARCHIVE_DIR%"
if defined IS_GUI if not exist "%SOURCE%\Update.exe" (
    echo Downloaded archive did not contain the expected files.
    rmdir /S /Q "%STAGING%" >nul 2>nul
    exit /b 1
)
if not exist "%SOURCE%" (
    echo Downloaded archive did not contain the expected %ARCHIVE_DIR% folder.
    rmdir /S /Q "%STAGING%" >nul 2>nul
    exit /b 1
)

echo Installing update...
robocopy "%SOURCE%" "%CD%" /E /COPY:DAT /R:3 /W:1 /NFL /NDL
set "COPY_EXIT=%ERRORLEVEL%"
rmdir /S /Q "%STAGING%" >nul 2>nul

if %COPY_EXIT% GEQ 8 (
    echo Update copy failed.
    exit /b 1
)

if defined IS_GUI (
    del ".\%OLD_APP%" >nul 2>nul
    del ".\update-fcp-mod-manager.bat" >nul 2>nul
    echo Switched to the self-updating version: start "Collaboration Project Updater.exe" from now on ^(settings are kept^).
    echo It updates itself from the app's Updates page, so this script is no longer needed for it.
    exit /b 0
)

if defined OLD_APP (
    del ".\%OLD_APP%" >nul 2>nul
    del ".\update-fcp-mod-manager.bat" >nul 2>nul
    echo Renamed: start %NEW_APP% from now on.
)

echo Update complete. Start Collaboration Project Updater normally.
exit /b 0
