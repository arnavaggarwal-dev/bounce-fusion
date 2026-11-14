@echo off
rem Publishes the local WebGL build (docs\) to GitHub Pages.
rem Make the build first in Unity: Bounce Fusion > Build WebGL.
cd /d "%~dp0"
set REPO=arnavaggarwal-dev/bounce-fusion

if not exist docs\index.html (
  echo No build found in docs\. In Unity click Bounce Fusion ^> Build WebGL first.
  pause
  exit /b 1
)

git checkout master || exit /b 1
git add docs
git diff --cached --quiet || git commit -q -m "Update WebGL build"
git pull --rebase --autostash || exit /b 1
for /f %%i in ('git rev-parse origin/master') do set BEFORE=%%i
git push || exit /b 1
for /f %%i in ('git rev-parse origin/master') do set AFTER=%%i
rem nothing new pushed = no automatic publish, so start one by hand
if "%BEFORE%"=="%AFTER%" gh workflow run webgl.yml --repo %REPO% --ref master

rem turn on GitHub Pages (deploy from Actions) the first time
gh api repos/%REPO%/pages >nul 2>&1 || gh api -X POST repos/%REPO%/pages -f build_type=workflow >nul

echo Publishing...
timeout /t 15 /nobreak >nul
for /f %%i in ('gh run list --repo %REPO% --workflow webgl.yml -L 1 --json databaseId -q ".[0].databaseId"') do set RUN=%%i
gh run watch %RUN% --repo %REPO% --exit-status && echo. && echo Play it: https://arnavaggarwal-dev.github.io/bounce-fusion/
pause
