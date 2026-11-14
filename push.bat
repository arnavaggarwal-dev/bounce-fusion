@echo off
set /p msg=Input commit msg:
git add .
git commit -m "%msg%"
git pull --rebase
git push   