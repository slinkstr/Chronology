@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

if not defined INKSCAPE set "INKSCAPE=inkscape"
set SIZES=16 32 48 64 96 128 256

set "tmp=%TEMP%\chronology-ico"
rmdir /s /q "%tmp%" 2>nul
mkdir "%tmp%"

set "inputs="
set "maps="
set n=0
for %%s in (%SIZES%) do (
    start "" /b /wait "%INKSCAPE%" "%~dp0image\icon.svg" -o "%tmp%\%%s.png" -w %%s -h %%s >nul 2>&1
    if errorlevel 1 goto fail
    set "inputs=!inputs! -i %%s.png"
    set "maps=!maps! -map !n!:v"
    set /a n+=1
)

pushd "%tmp%"
ffmpeg -hide_banner -loglevel error -y %inputs% %maps% "%~dp0image\icon.ico"
if errorlevel 1 (popd & goto fail)
popd

rmdir /s /q "%tmp%"
echo icon.ico written
endlocal
exit /b 0

:fail
rmdir /s /q "%tmp%" 2>nul
echo FAILED
pause
exit /b 1
