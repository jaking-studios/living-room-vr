@echo off
REM Starts a tiny local web server for the game and opens it in your browser.
REM Uses the Python that ships with Blender, so nothing extra needs installing.
cd /d "%~dp0"
set PY="C:\Program Files\Blender Foundation\Blender 5.2\5.2\python\bin\python.exe"
if not exist %PY% set PY=python
start "" http://localhost:8080/
%PY% -m http.server 8080
