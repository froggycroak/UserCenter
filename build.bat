@echo off
setlocal EnableExtensions

rem Vite writes server\wwwroot, then dotnet build copies it into the Release output.
rem Runs npm run build only. Do not npm install against the Junction.
rem If node_modules is missing, run web\ensure_node_modules.bat first.

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo === user-center build ===
echo ROOT=%ROOT%

where npm >nul 2>&1
if errorlevel 1 (
  echo [ERROR] npm not found
  exit /b 1
)
where dotnet >nul 2>&1
if errorlevel 1 (
  echo [ERROR] dotnet not found
  exit /b 1
)

if not exist "%ROOT%\web\node_modules\" (
  echo [ERROR] web\node_modules is missing. Run web\ensure_node_modules.bat first.
  exit /b 1
)

echo.
echo [1/2] npm run build
pushd "%ROOT%\web"
call npm run build
set "EC=%ERRORLEVEL%"
popd
if not "%EC%"=="0" (
  echo [ERROR] npm build failed
  exit /b %EC%
)

echo.
echo [2/2] dotnet build -c Release -r win-x64
pushd "%ROOT%\server"
call dotnet build -c Release -r win-x64 --nologo
set "EC=%ERRORLEVEL%"
popd
if not "%EC%"=="0" (
  echo [ERROR] dotnet build failed
  exit /b %EC%
)

echo.
echo [OK] output:
echo %ROOT%\server\bin\Release\net8.0\win-x64
endlocal
exit /b 0
