@echo off
rem Starts the companion's bridge on 127.0.0.1:7777. Leave this window open while you play.
rem Optional argument: mix (default), haiku, or opus.
cd /d "%~dp0"
title Companion bridge
if not exist .venv\Scripts\python.exe (
    echo Run setup.cmd first.
    pause
    exit /b 1
)
.venv\Scripts\python.exe server.py %*
pause
