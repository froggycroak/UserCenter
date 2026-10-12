@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem rollback.remote.bat  (run on 206)
rem stop -> pick latest/specified archive -> move back -> start -> health check
rem Usage: rollback.remote.bat [version]   (omit to rollback to latest archive by mtime)

set "SERVER_ROOT=C:\ProgramCustom\Server"
set "APP_DIR=%SERVER_ROOT%\udapp_login"
set "EXE_NAME=Udapp.UserCenter.exe"
set "OUTVER=%SERVER_ROOT%\.outversion\udapp"
set "RESULT=%SERVER_ROOT%\.staging\deploy.result.txt"

echo === rollback.remote start %DATE% %TIME% ===

if not exist "%OUTVER%" (
  >"%RESULT%" echo STATUS=FAIL
  >>"%RESULT%" echo REASON=no-archive
  echo [ERROR] no archive dir.
  exit /b 1
)

rem pick target: explicit > latest (by mtime desc)
set "TARGET=%~1"
if "%TARGET%"=="" (
  set "TARGET="
  for /f "delims=" %%i in ('dir /b /ad /o-d "%OUTVER%\udapp_login_*" 2^>nul') do if "!TARGET!"=="" set "TARGET=%%i"
)
if "%TARGET%"=="" (
  >"%RESULT%" echo STATUS=FAIL
  >>"%RESULT%" echo REASON=no-archive
  echo [ERROR] no archive to rollback.
  exit /b 1
)
set "ARCH=%OUTVER%\%TARGET%"
if not exist "%ARCH%" (
  >"%RESULT%" echo STATUS=FAIL
  >>"%RESULT%" echo REASON=archive-not-found
  echo [ERROR] archive not found: %TARGET%
  exit /b 1
)

echo [1] stop %EXE_NAME%
taskkill /im %EXE_NAME% /f >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2] swap: archive -> current
if exist "%APP_DIR%" rmdir /s /q "%APP_DIR%" 2>nul
move "%ARCH%" "%APP_DIR%" >nul
if errorlevel 1 (
  >"%RESULT%" echo STATUS=FAIL
  >>"%RESULT%" echo REASON=move-failed
  exit /b 1
)

echo [3] start udapp_login
schtasks /run /tn udapp_login >nul 2>&1
if errorlevel 1 start "" "%APP_DIR%\run.bat"

echo [4] health check
set /a tries=30
:hloop
timeout /t 2 /nobreak >nul
curl -s -I "http://127.0.0.1:1010/api/health" 2>nul | findstr /i "200 OK" >nul
if not errorlevel 1 goto hok
set /a tries-=1
if !tries! gtr 0 goto hloop

>"%RESULT%" echo STATUS=FAIL
>>"%RESULT%" echo REASON=health-timeout
echo [FAIL] health timeout after rollback.
exit /b 1

:hok
>"%RESULT%" echo STATUS=OK
>>"%RESULT%" echo ROLLBACK=%TARGET%
echo [OK] rolled back to %TARGET%.
endlocal
exit /b 0
