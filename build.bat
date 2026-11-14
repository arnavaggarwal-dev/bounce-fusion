@echo off
rem Pushes master to GitHub, which starts the WebGL cloud build, then watches it.
cd /d "%~dp0"
set REPO=arnavaggarwal-dev/bounce-fusion

git checkout master || exit /b 1
rem bring in the game branch if it's still separate (no-op once merged)
git rev-parse --verify -q bounce-merge-game >nul && git merge --ff-only bounce-merge-game
git pull --rebase --autostash || exit /b 1
for /f %%i in ('git rev-parse origin/master') do set BEFORE=%%i
git push || exit /b 1
for /f %%i in ('git rev-parse origin/master') do set AFTER=%%i
rem nothing new pushed = no automatic build, so start one by hand
if "%BEFORE%"=="%AFTER%" gh workflow run webgl.yml --repo %REPO% --ref master

rem turn on GitHub Pages (deploy from Actions) the first time
gh api repos/%REPO%/pages >nul 2>&1 || gh api -X POST repos/%REPO%/pages -f build_type=workflow >nul

echo Waiting for the build to start...
timeout /t 15 /nobreak >nul
for /f %%i in ('gh run list --repo %REPO% --workflow webgl.yml -L 1 --json databaseId -q ".[0].databaseId"') do set RUN=%%i
gh run watch %RUN% --repo %REPO% --exit-status && echo. && echo Play it: https://arnavaggarwal-dev.github.io/bounce-fusion/
pause
