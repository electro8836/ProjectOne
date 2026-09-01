@echo off
setlocal

REM ExcelDataTable headless pipeline runner
REM   Runs Refresh -> Save -> Build without the GUI, for CI/batch automation.
REM   Prerequisite: ..\Build\ExcelDataTable.exe must exist (run build.bat first),
REM                 and DataPath etc. must already be saved once from the GUI
REM                 (edt_local.json / edt_settings.json).

set "EXE=%~dp0ExcelDataTable.exe"

if not exist "%EXE%" (
  echo [Pipeline] ExcelDataTable.exe not found: %EXE%
  echo [Pipeline] Run build.bat first to publish it.
  endlocal
  exit /b 1
)

echo [Pipeline] Running Refresh -^> Save -^> Build...
echo [Pipeline] EXE: %EXE%
echo.

"%EXE%" --build
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%EXIT_CODE%"=="0" (
  echo [Pipeline] FAILED ^(exit code %EXIT_CODE%^)
  endlocal
  exit /b %EXIT_CODE%
)

echo [Pipeline] DONE.
endlocal
