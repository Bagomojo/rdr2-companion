@echo off
rem Gathers the logs for a bug report into one zip on your desktop, with keys and your Windows
rem user name taken out. Attach the zip to your issue on GitHub.
rem Optional argument: the game folder, if it isn't found on its own.
cd /d "%~dp0"
if exist .venv\Scripts\python.exe (
    .venv\Scripts\python.exe collect_logs.py %*
) else (
    python collect_logs.py %*
)
pause
