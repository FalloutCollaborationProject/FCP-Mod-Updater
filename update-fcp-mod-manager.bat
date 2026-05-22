@echo off
setlocal

if /I not "%~1"=="--from-temp" (
    set "TEMP_SCRIPT=%TEMP%\update-fcp-mod-manager-%RANDOM%%RANDOM%.bat"
    copy "%~f0" "%TEMP_SCRIPT%" >nul
    call "%TEMP_SCRIPT%" --from-temp
    set "EXIT_CODE=%ERRORLEVEL%"
    del "%TEMP_SCRIPT%" >nul 2>nul
    exit /b %EXIT_CODE%
)

set "APP_NAME=FCPModUpdater.exe"
set "ARCHIVE=FCPModUpdater-win-x64-selfcontained.zip"
set "BASE_URL=https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/latest/download"
set "RELEASES_API=https://api.github.com/repos/FalloutCollaborationProject/FCP-Mod-Updater/releases?per_page=50"
set "STAGING=%TEMP%\FCPModUpdater-update-%RANDOM%%RANDOM%"

if not exist ".\%APP_NAME%" (
    echo Run this script from the existing FCP Mod Manager install folder.
    exit /b 1
)

tasklist /FI "IMAGENAME eq %APP_NAME%" 2>nul | find /I "%APP_NAME%" >nul
if "%ERRORLEVEL%"=="0" (
    echo FCP Mod Manager is still running. Close it before updating.
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
    echo Downloading latest stable FCP Mod Manager release...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest -Uri '%BASE_URL%/%ARCHIVE%' -OutFile '%STAGING%\%ARCHIVE%'; Invoke-WebRequest -Uri '%BASE_URL%/checksums.txt' -OutFile '%STAGING%\checksums.txt'"
) else if "%CHANNEL_CHOICE%"=="2" (
    echo Finding latest FCP Mod Manager pre-release...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; $headers = @{ 'User-Agent' = 'FCPModUpdater' }; $releases = Invoke-RestMethod -Headers $headers -Uri '%RELEASES_API%'; $release = $releases | Where-Object { $_.prerelease -and -not $_.draft } | Select-Object -First 1; if ($null -eq $release) { throw 'No pre-release was found.' }; $archive = $release.assets | Where-Object { $_.name -eq '%ARCHIVE%' } | Select-Object -First 1; $checksums = $release.assets | Where-Object { $_.name -eq 'checksums.txt' } | Select-Object -First 1; if ($null -eq $archive) { throw ('No asset named %ARCHIVE% was found on ' + $release.tag_name + '.') }; if ($null -eq $checksums) { throw ('No checksums.txt asset was found on ' + $release.tag_name + '.') }; Invoke-WebRequest -Headers $headers -Uri $archive.browser_download_url -OutFile '%STAGING%\%ARCHIVE%'; Invoke-WebRequest -Headers $headers -Uri $checksums.browser_download_url -OutFile '%STAGING%\checksums.txt'"
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

powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Force -Path '%STAGING%\%ARCHIVE%' -DestinationPath '%STAGING%'"
if not "%ERRORLEVEL%"=="0" (
    echo Could not extract update archive.
    rmdir /S /Q "%STAGING%" >nul 2>nul
    exit /b 1
)

if not exist "%STAGING%\win-x64-sc" (
    echo Downloaded archive did not contain the expected win-x64-sc folder.
    rmdir /S /Q "%STAGING%" >nul 2>nul
    exit /b 1
)

echo Installing update...
robocopy "%STAGING%\win-x64-sc" "%CD%" /E /COPY:DAT /R:3 /W:1 /NFL /NDL
set "COPY_EXIT=%ERRORLEVEL%"
rmdir /S /Q "%STAGING%" >nul 2>nul

if %COPY_EXIT% GEQ 8 (
    echo Update copy failed.
    exit /b 1
)

echo Update complete. Start FCP Mod Manager normally.
exit /b 0
