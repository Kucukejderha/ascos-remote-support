@echo off
setlocal
if "%~1"=="" goto usage
set "TARGET_ID=%~1"
set "RESULT_DIR=%USERPROFILE%\Desktop\RotaLink-Test-Sonuclari"
if not exist "%RESULT_DIR%" mkdir "%RESULT_DIR%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Test-RotaLinkCompatibility.ps1" -TargetId "%TARGET_ID%" -OutputPath "%RESULT_DIR%\rotalink-compatibility-%TARGET_ID%.json"
set "EXIT_CODE=%ERRORLEVEL%"
echo.
if "%EXIT_CODE%"=="0" (
  echo On kontrol basarili. JSON raporu: %RESULT_DIR%
) else (
  echo En az bir uyumluluk engeli bulundu. JSON raporu: %RESULT_DIR%
)
echo RotaLink EXE testi ve manuel P0/P1 senaryolari icin WINDOWS-TEST-LABORATUVARI.tr.md dosyasini izleyin.
exit /b %EXIT_CODE%

:usage
echo Kullanim: RotaLink-Uyumluluk-Testi.cmd TARGET_ID
echo Ornek:   RotaLink-Uyumluluk-Testi.cmd server-2019
echo Hedefler: windows-10 windows-11 server-2012 server-2012-r2 server-2016 server-2019 server-2022 server-2025
exit /b 64
