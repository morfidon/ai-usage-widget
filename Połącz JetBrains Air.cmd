@echo off
cd /d "%~dp0"
"%~dp0tools\central\central.exe" login
if errorlevel 1 (
  echo Logowanie nie zostalo zakonczone. Sprobuj ponownie.
  pause
  exit /b 1
)
echo Konto polaczone. Widget odczyta limity automatycznie w ciagu 2 minut.
pause
