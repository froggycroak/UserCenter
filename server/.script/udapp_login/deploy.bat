@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem ============================================================
rem  deploy.bat  (run on 169)
rem  Default: build first, then push to 206, trigger remote deploy, poll result.
rem  Usage: deploy.bat [--no-build]
rem ============================================================

set "ROOT=%~dp0..\..\.."
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "LOCAL=%~dp0"
set "BUILD_OUT=%ROOT%\server\bin\Release\net8.0\win-x64"

set "NOBUILD=0"
if /i "%~1"=="--no-build" set "NOBUILD=1"

call "%LOCAL%deploy.local.bat"
if errorlevel 1 ( echo [ERROR] deploy.local.bat not found, copy from deploy.local.example.bat & exit /b 1 )
if "%REMOTE_PWD%"=="在这里填 admin 密码" ( echo [ERROR] edit deploy.local.bat and set REMOTE_PWD & exit /b 1 )

echo === user-center deploy ===
echo REMOTE_HOST = %REMOTE_HOST%
echo ROOT       = %ROOT%

rem --- 1. build ---
if "%NOBUILD%"=="0" (
  echo.
  echo [1] build
  pushd "%ROOT%"
  call build.bat
  set "EC=!ERRORLEVEL!"
  popd
  if not "!EC!"=="0" ( echo [ERROR] build failed & exit /b !EC! )
) else (
  echo [1] build skipped (--no-build)
)

if not exist "%BUILD_OUT%\Udapp.UserCenter.exe" (
  echo [ERROR] build output not found: %BUILD_OUT%\Udapp.UserCenter.exe
  exit /b 1
)

rem --- 2. push to 206 staging ---
set "STAGE_APP=%STAGING_DIR%\udapp_login"
echo.
echo [2] robocopy build-out -> staging
rem check if share already connected (Explorer may have opened it, avoid error 1219)
if not exist "%REMOTE_SHARE%\Server" (
  net use "%REMOTE_SHARE%" "%REMOTE_PWD%" /user:%REMOTE_USER% >nul 2>&1
)
if not exist "%STAGING_DIR%" mkdir "%STAGING_DIR%"
if exist "%STAGE_APP%" rmdir /s /q "%STAGE_APP%" 2>nul
mkdir "%STAGE_APP%"
robocopy "%BUILD_OUT%" "%STAGE_APP%" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul
if errorlevel 8 ( echo [ERROR] robocopy artifacts failed & exit /b 1 )

robocopy "%LOCAL%" "%STAGING_DIR%" *.remote.bat /NFL /NDL /NJH /NJS /NC /NS /NP >nul
if exist "%STAGING_DIR%\deploy.result.txt" del "%STAGING_DIR%\deploy.result.txt"

rem --- 3. trigger remote deploy ---
echo.
echo [3] trigger udapp_login_deploy on %REMOTE_HOST%
schtasks /run /s %REMOTE_HOST% /u %REMOTE_USER% /p %REMOTE_PWD% /tn udapp_login_deploy >nul
if errorlevel 1 ( echo [ERROR] schtasks /run failed & exit /b 1 )

rem --- 4. poll result ---
echo [4] wait for result...
set /a wait=0
:poll
timeout /t 3 /nobreak >nul
set /a wait+=3
if exist "%STAGING_DIR%\deploy.result.txt" goto gotresult
if %wait% geq 180 ( echo [ERROR] timeout waiting result & exit /b 1 )
goto poll

:gotresult
echo.
echo --- deploy.result.txt ---
type "%STAGING_DIR%\deploy.result.txt"
echo.
findstr /b "STATUS=OK" "%STAGING_DIR%\deploy.result.txt" >nul
if errorlevel 1 (
  echo [FAIL] remote deploy failed, see result above.
  exit /b 1
)
echo [OK] deploy succeeded.
endlocal
exit /b 0
