@echo off
rem One-time setup for the bridge: a Python virtual environment with its one package,
rem and a .env for your keys. Needs Python 3.10 or newer from python.org.
cd /d "%~dp0"
if not exist .venv (
    py -3 -m venv .venv || python -m venv .venv || goto :nopython
)
.venv\Scripts\python.exe -m pip install --quiet --upgrade pip
.venv\Scripts\python.exe -m pip install --quiet -r requirements.txt || goto :failed
if not exist .env (
    copy .env.example .env >nul
    echo Created bridge\.env. Open it in Notepad and paste in your Anthropic API key.
) else (
    echo bridge\.env already exists; left it alone.
)
echo Setup done. Start the bridge with start-bridge.cmd.
pause
exit /b 0

:nopython
echo Could not find Python. Install Python 3.12 from python.org (tick "Add python.exe to PATH"), then run this again.
pause
exit /b 1

:failed
echo Installing the Python package failed; see the message above.
pause
exit /b 1
