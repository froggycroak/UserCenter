@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem ============================================================
rem  rollback.bat  (run on 169)
rem  Usage: rollback.bat [version]   (omit to rollback to latest archive)
rem  Reuses udapp_login_deploy task: temporarily change /tr to rollback.remote.bat,
rem  then restore to deploy.remote.bat.
rem ============================================================

set "LOCAL=%~dp0"
call "%LOCAL%deploy.local.bat"
if errorlevel 1 ( echo [ERROR] deploy.local.bat not found & exit /b 1 )
if "%REMOTE_PWD%"=="在这里填 admin 密码" ( echo [ERROR] edit deploy.local.bat and set REMOTE_PWD & exit /b 1 )

set "STAGING_DIR=%REMOTE_SHARE%\Server\.staging"
rem check if share already connected (Explorer may have opened it, avoid error 1219)
if not exist "%REMOTE_SHARE%\Server" (
  net use "%REMOTE_SHARE%" "%REMOTE_PWD%" /user:%REMOTE_USER% >nul 2>&1
)
if not exist "%STAGING_DIR%" mkdir "%STAGING_DIR%"
if exist "%STAGING_DIR%\deploy.result.txt" del "%STAGING_DIR%\deploy.result.txt"

robocopy "%LOCAL%" "%STAGING_DIR%" rollback.remote.bat /NFL /NDL /NJH /NJS /NC /NS /NP >nul

rem Temporarily change udapp_login_deploy task /tr to rollback.remote.bat, then restore
schtasks /change /s %REMOTE_HOST% /u %REMOTE_USER% /p %REMOTE_PWD% /tn udapp_login_deploy /tr "%STAGING_DIR%\rollback.remote.bat %~1" >nul
if errorlevel 1 ( echo [ERROR] schtasks /change failed & exit /b 1 )
schtasks /run /s %REMOTE_HOST% /u %REMOTE_USER% /p %REMOTE_PWD% /tn udapp_login_deploy >nul
schtasks /change /s %REMOTE_HOST% /u %REMOTE_USER% /p %REMOTE_PWD% /tn udapp_login_deploy /tr "%STAGING_DIR%\deploy.remote.bat" >nul

set /a wait=0
:poll
timeout /t 3 /nobreak >nul
set /a wait+=3
if exist "%STAGING_DIR%\deploy.result.txt" goto got
if %wait% geq 180 ( echo [ERROR] timeout waiting result & exit /b 1 )
goto poll

:got
echo.
echo --- deploy.result.txt ---
type "%STAGING_DIR%\deploy.result.txt"
echo.
findstr /b "STATUS=OK" "%STAGING_DIR%\deploy.result.txt" >nul
if errorlevel 1 ( echo [FAIL] rollback failed. & exit /b 1 )
echo [OK] rollback succeeded.
endlocal
exit /b 0
