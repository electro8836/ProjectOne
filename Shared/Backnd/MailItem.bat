@echo off
REM ============================================================
REM  MailItem chart export - writes MailItem.csv next to this file
REM  Source: ProjectOne\Assets\Project\Data\Tables\*.bytes
REM  If you edited the Excel tables, run ExcelDataTable --build first.
REM  Then upload MailItem.csv to the Backnd console chart "MailItem"
REM  with mail use enabled.
REM ============================================================

echo ============================================
echo  MailItem chart export...
echo ============================================
echo.

dotnet run --project "%~dp0MailItemExporter" -c Release -- "%~dp0."
if errorlevel 1 (
	echo.
	echo [FAILED] MailItem.csv was not written.
	pause
	exit /b 1
)

echo.
echo [DONE] MailItem.csv updated.
pause
