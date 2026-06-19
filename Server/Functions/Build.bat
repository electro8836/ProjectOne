@echo off
REM ============================================================
REM  Backend Build and Deploy - build then deploy to server
REM  1) backend build  : create publish.zip
REM  2) backend deploy : upload to backnd server
REM  Deploy result (StatusCode etc.) is shown by deploy output.
REM ============================================================

echo ============================================
echo  [1/2] Backend Build - building...
echo ============================================
echo.

call backend build
if errorlevel 1 (
	echo.
	echo [FAILED] build failed. deploy aborted.
	pause
	exit /b 1
)

echo.
echo ============================================
echo  [2/2] Backend Deploy - uploading to server...
echo ============================================
echo.

call backend deploy
if errorlevel 1 (
	echo.
	echo [FAILED] deploy failed.
	pause
	exit /b 1
)

echo.
echo ============================================
echo  [SUCCESS] build and deploy completed.
echo ============================================
pause
