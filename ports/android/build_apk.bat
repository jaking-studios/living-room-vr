@echo off
REM Rebuilds LivingRoomVR.apk from the web game in this repo.
REM Needs Android Studio installed (its bundled Java + Android SDK).
cd /d "%~dp0"
echo Copying the game into the app...
if exist app\src\main\assets rmdir /s /q app\src\main\assets
robocopy ..\.. app\src\main\assets /E /XD .git ports docs /XF *.bat README.md .nojekyll >nul
set "JAVA_HOME=C:\Program Files\Android\Android Studio\jbr"
if not exist local.properties echo sdk.dir=%LOCALAPPDATA:\=\\%\\Android\\Sdk> local.properties
set GRADLE=
for /d %%G in ("%USERPROFILE%\.gradle\wrapper\dists\gradle-9.3.1-bin\*") do set "GRADLE=%%G\gradle-9.3.1\bin\gradle.bat"
if "%GRADLE%"=="" (echo Gradle 9.3.1 not found - open this folder in Android Studio once so it downloads Gradle, then run again. & pause & exit /b 1)
call "%GRADLE%" assembleRelease --console=plain
copy /y app\build\outputs\apk\release\app-release.apk LivingRoomVR.apk
echo Done: %cd%\LivingRoomVR.apk
pause
