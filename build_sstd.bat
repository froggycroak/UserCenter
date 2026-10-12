@echo off
setlocal EnableExtensions

rem Build Research Portal (web_sstd) static dist for nginx :1088.
rem Does NOT build .NET / User Center. For full User Center use build.bat.
rem If node_modules is missing, run web\ensure_node_modules.bat first.

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo === web_sstd build ===
echo ROOT=%ROOT%

where npm >nul 2>&1
if errorlevel 1 (
  echo [ERROR] npm not found
  exit /b 1
)

if not exist "%ROOT%\web_sstd\node_modules\" (
  echo [ERROR] web_sstd\node_modules is missing. Run web\ensure_node_modules.bat first.
  exit /b 1
)

if not exist "%ROOT%\shared\data\tools.xlsx" (
  echo [ERROR] shared\data\tools.xlsx missing. Run: node shared\scripts\bootstrap-tools-xlsx.mjs
  exit /b 1
)

echo.
echo [1/1] npm run build ^(web_sstd^)
pushd "%ROOT%\web_sstd"
call npm run build
set "EC=%ERRORLEVEL%"
popd
if not "%EC%"=="0" (
  echo [ERROR] web_sstd build failed
  exit /b %EC%
)

echo.
echo [OK] output:
echo %ROOT%\web_sstd\dist
echo Deploy this folder with nginx listen 1088 ^(see server\.script\nginx-web_sstd.example.conf^)
endlocal
exit /b 0
