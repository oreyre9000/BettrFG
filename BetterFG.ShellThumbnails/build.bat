@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul 2>nul
cd /d "%~dp0"
if not exist obj mkdir obj
cl /nologo /O2 /MT /EHsc /W3 /LD /Foobj\ BettrFGThumbs.cpp /link /DEF:BettrFGThumbs.def /OUT:obj\BettrFGThumbs.dll /IMPLIB:obj\BettrFGThumbs.lib || exit /b 1
copy /y obj\BettrFGThumbs.dll ..\BetterFG\assets\shell\BettrFGThumbs.dll >nul
echo BettrFGThumbs.dll rebuilt into BetterFG\assets\shell
