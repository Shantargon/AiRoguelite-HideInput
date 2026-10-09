@echo off
rem Русские буквы в этом файле записаны в кодировке DOS-866,
rem поэтому сразу переключаем на неё окно консоли - иначе будут кракозябры.
chcp 866 >nul
title Udalenie moda "Skryt vvod igrokov"
setlocal enableextensions
cd /d "%~dp0"

echo.
echo   Удаление мода "Скрыть ввод игроков" из AI Roguelite
echo   ---------------------------------------------------
echo.

rem --- мы точно в папке игры? ---
if not exist "AI Roguelite.exe" (
  echo   Рядом нет файла "AI Roguelite.exe", значит это не папка игры.
  echo   Перенеси этот файл туда, где лежит игра, и запусти снова.
  echo.
  pause
  exit /b 1
)

rem --- игра должна быть закрыта, иначе Windows не отдаст файлы ---
tasklist /FI "IMAGENAME eq AI Roguelite.exe" 2>nul | find /I "AI Roguelite.exe" >nul
if not errorlevel 1 (
  echo   Игра сейчас запущена, а занятые файлы Windows удалить не даст.
  echo   Закрой игру и запусти этот файл ещё раз.
  echo.
  pause
  exit /b 1
)

rem --- есть ли в BepInEx другие моды, кроме нашего ---
set OTHER=0
for %%F in ("BepInEx\plugins\*.dll") do (
  if /I not "%%~nxF"=="HidePlayerInput.dll" set OTHER=1
)
for /d %%D in ("BepInEx\plugins\*") do set OTHER=1
for %%F in ("BepInEx\patchers\*.dll") do set OTHER=1

echo   Будет удалено:
if "%OTHER%"=="0" (
  echo     - папка BepInEx: и мод, и его настройки
  echo     - winhttp.dll, doorstop_config.ini, .doorstop_version
) else (
  echo     - BepInEx\plugins\HidePlayerInput.dll и настройки мода
  echo     - в BepInEx лежат другие моды, поэтому саму папку не трогаю
)
echo     - ReadMe-rus.txt и этот файл
echo.
echo   Сохранения, сценарии и настройки самой игры не затрагиваются.
echo.

set ANSW=
set /p ANSW=  Удалять? (д - да, любая другая клавиша - нет): 
if /I "%ANSW%"=="д" goto :udalyaem
if /I "%ANSW%"=="да" goto :udalyaem
if /I "%ANSW%"=="y" goto :udalyaem
if /I "%ANSW%"=="yes" goto :udalyaem
echo.
echo   Ничего не тронул.
echo.
pause
exit /b 0

:udalyaem
echo.
if "%OTHER%"=="0" (
  if exist "BepInEx" rd /s /q "BepInEx"
  if exist "winhttp.dll" del /f /q "winhttp.dll"
  if exist "doorstop_config.ini" del /f /q "doorstop_config.ini"
  if exist ".doorstop_version" del /f /q ".doorstop_version"
) else (
  if exist "BepInEx\plugins\HidePlayerInput.dll" del /f /q "BepInEx\plugins\HidePlayerInput.dll"
  if exist "BepInEx\config\shantargon.airoguelite.hideplayerinput.cfg" del /f /q "BepInEx\config\shantargon.airoguelite.hideplayerinput.cfg"
)
if exist "ReadMe-rus.txt" del /f /q "ReadMe-rus.txt"

rem --- проверяем, что всё ушло ---
set FAIL=0
if exist "BepInEx\plugins\HidePlayerInput.dll" set FAIL=1
if "%OTHER%"=="0" if exist "winhttp.dll" set FAIL=1
if "%OTHER%"=="0" if exist "BepInEx" set FAIL=1

if "%FAIL%"=="1" (
  echo   Удалить удалось не всё - файлы чем-то заняты.
  echo   Проверь, что игра закрыта, и запусти этот файл ещё раз.
  echo.
  pause
  exit /b 1
)

if "%OTHER%"=="0" (
  echo   Готово. Мода больше нет, игра вернулась в исходный вид.
) else (
  echo   Готово. Мод удалён, остальные моды остались на месте.
)
echo.
pause

rem --- стираем сам себя и выходим ---
(goto) 2>nul & del /f /q "%~f0"
