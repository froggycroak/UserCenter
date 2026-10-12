@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem deploy.remote.bat  (triggered on 206 by udapp_login_deploy task, or run manually)
rem stop -> [archive if app exists] -> deploy new -> generate run.bat -> start -> health check -> rollback on failure -> write result
rem On first deploy (APP_DIR does not exist) archive step is skipped, no separate bootstrap needed.

set "SERVER_ROOT=C:\ProgramCustom\Server"
set "APP_DIR=%SERVER_ROOT%\udapp_login"
set "EXE_NAME=Udapp.UserCenter.exe"
set "OUTVER=%SERVER_ROOT%\.outversion\udapp"
set "STAGING=%SERVER_ROOT%\.staging"
set "STAGE_APP=%STAGING%\udapp_login"
set "LOG_BASE=%APPDATA%\ArchiFrog\UDAppUserCenter\logs"
set "SERVER_LOG=%LOG_BASE%\server.log"
set "RESULT=%STAGING%\deploy.result.txt"
set "HEALTH_URL=http://127.0.0.1:1010/api/health"
set "HEALTH_WAIT=60"

if not exist "%LOG_BASE%" mkdir "%LOG_BASE%"
if not exist "%OUTVER%" mkdir "%OUTVER%"
if not exist "%STAGING%" mkdir "%STAGING%"
if exist "%RESULT%" del "%RESULT%"

echo === deploy.remote start %DATE% %TIME% ===

rem --- 1. stop ---
echo [1] stop %EXE_NAME%
tasklist /fi "imagename eq %EXE_NAME%" 2>nul | find /i "%EXE_NAME%" >nul
if not errorlevel 1 (
  taskkill /im %EXE_NAME% /f >nul 2>&1
  timeout /t 2 /nobreak >nul
)

rem --- 2. archive (skip if first deploy, APP_DIR not exist) ---
set "ARCH="
if exist "%APP_DIR%" (
  echo [2] archive current app
  for /f "delims=" %%i in ('powershell -NoProfile -Command "$d='%APP_DIR%';$o='%OUTVER%';$v='v'+(Get-Item $d).LastWriteTime.ToString('yyMMdd_HHmm');$a=Join-Path $o ('udapp_login_'+$v);$c=2;while(Test-Path $a){$a=Join-Path $o ('udapp_login_'+$v+'_'+$c);$c++};$a"') do set "ARCH=%%i"
  echo     -^> !ARCH!
  move "%APP_DIR%" "!ARCH!" >nul
  if errorlevel 1 (
    >"%RESULT%" echo STATUS=FAIL
    >>"%RESULT%" echo REASON=archive-failed
    exit /b 1
  )
) else (
  echo [2] no current app, skip archive (first deploy)
)

rem --- 3. deploy new from staging ---
echo [3] robocopy staging -> app
if not exist "%STAGE_APP%" (
  echo [ERROR] staging not found: %STAGE_APP%
  if defined ARCH move "!ARCH!" "%APP_DIR%" >nul
  >"%RESULT%" echo STATUS=FAIL
  >>"%RESULT%" echo REASON=no-staging
  exit /b 1
)
robocopy "%STAGE_APP%" "%APP_DIR%" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul
if errorlevel 8 (
  if defined ARCH move "!ARCH!" "%APP_DIR%" >nul
  >"%RESULT%" echo STATUS=FAIL
  >>"%RESULT%" echo REASON=robocopy-failed
  exit /b 1
)

rem --- 4. generate run.bat (launcher with log redirect) ---
> "%APP_DIR%\run.bat" echo @echo off
>>"%APP_DIR%\run.bat" echo "%%~dp0%EXE_NAME%" ^>^> "%SERVER_LOG%" 2^>^1

rem --- 5. start ---
echo [4] start udapp_login
schtasks /run /tn udapp_login >nul 2>&1
if errorlevel 1 start "" "%APP_DIR%\run.bat"

rem --- 6. health check ---
echo [5] health check (wait up to %HEALTH_WAIT%s)
set /a tries=%HEALTH_WAIT%/2
:healthloop
timeout /t 2 /nobreak >nul
curl -s -I "%HEALTH_URL%" 2>nul | findstr /i "200 OK" >nul
if not errorlevel 1 goto healthy
set /a tries-=1
if !tries! gtr 0 goto healthloop

rem --- 7. rollback ---
echo [FAIL] health timeout, rollback...
taskkill /im %EXE_NAME% /f >nul 2>&1
timeout /t 2 /nobreak >nul
if exist "%APP_DIR%" rmdir /s /q "%APP_DIR%" 2>nul
if defined ARCH (
  move "!ARCH!" "%APP_DIR%" >nul
  schtasks /run /tn udapp_login >nul 2>&1
  timeout /t 5 /nobreak >nul
  >"%RESULT%" echo STATUS=FAIL
  >>"%RESULT%" echo REASON=health-timeout
  >>"%RESULT%" echo ROLLED_BACK=yes
  echo === deploy.remote FAIL (rolled back) ===
) else (
  >"%RESULT%" echo STATUS=FAIL
  >>"%RESULT%" echo REASON=health-timeout
  >>"%RESULT%" echo ROLLED_BACK=no-archive
  echo === deploy.remote FAIL (no archive to rollback) ===
)
exit /b 1

:healthy
>"%RESULT%" echo STATUS=OK
>>"%RESULT%" echo HEALTH=ok
echo === deploy.remote OK ===
endlocal
exit /b 0
