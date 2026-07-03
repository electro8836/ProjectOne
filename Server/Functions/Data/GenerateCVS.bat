@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

:: -------------------------------------------------------------
:: 변수 설정: 여기에 엑셀 파일 이름을 적어주세요
set "EXCEL_FILE=ServerChartData.xlsx"
:: -------------------------------------------------------------

if not exist "%EXCEL_FILE%" (
    echo [오류] "%EXCEL_FILE%" 파일을 찾을 수 없습니다.
    pause
    exit /b
)

echo 엑셀 파일을 읽어서 총 12개 안팎의 워크시트를 CSV로 변환 중입니다...
echo -------------------------------------------------------------

powershell -NoProfile -Command ^
    "$excel = New-Object -ComObject Excel.Application;" ^
    "$excel.Visible = $false;" ^
    "$excel.DisplayAlerts = $false;" ^
    "$currDir = Get-Location;" ^
    "$xlPath = Join-Path $currDir '%EXCEL_FILE%';" ^
    "$wb = $excel.Workbooks.Open($xlPath);" ^
    "foreach ($ws in $wb.Worksheets) {" ^
    "    try {" ^
    "        if ($ws.Visible -ne -1) { continue };" ^
    "        $wsName = $ws.Name;" ^
    "        $safeName = $wsName -replace '[\\/:*?\"<>|]', '_';" ^
    "        $csvPath = Join-Path $currDir ($safeName + '.csv');" ^
    "        if (Test-Path -LiteralPath $csvPath) { Remove-Item -LiteralPath $csvPath -Force };" ^
    "        $ws.SaveAs($csvPath, 6);" ^
    "        Write-Host ('[성공] ' + $wsName + ' -> ' + $safeName + '.csv');" ^
    "    } catch {" ^
    "        Write-Host ('[실패] ' + $ws.Name + ' 시트 처리 중 오류 발생: ' + $_.Exception.Message) -ForegroundColor Red;" ^
    "    }" ^
    "};" ^
    "$wb.Close($false);" ^
    "$excel.Quit();" ^
    "[System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null;"

echo -------------------------------------------------------------
echo 작업이 끝났습니다.
pause