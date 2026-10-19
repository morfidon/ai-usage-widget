@echo off
cd /d "%~dp0"
if not exist "%~dp0tools\central\central.exe" (
  echo Central is missing. Rerun setup with JetBrains Air selected.
  pause
  exit /b 1
)
"%~dp0tools\central\central.exe" login
if errorlevel 1 (
  echo Login did not finish. Try again.
  pause
  exit /b 1
)
echo Connected. The widget will read quotas within two minutes.
pause
