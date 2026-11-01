@echo off
rem Starts the companion's bridge on 127.0.0.1:7777. Leave this window open while you play,
rem or set Bridge= in DaveCompanion.ini and the mod starts it with the game.
rem Optional argument: mix (default), haiku, or opus.
cd /d "%~dp0"
title Companion bridge
if not exist .venv\Scripts\python.exe (
    echo Run setup.cmd first.
    if not defined RDR2_GAME_PID pause
    exit /b 1
)
.venv\Scripts\python.exe server.py %*
rem Started hidden by the mod: nobody is there to press a key.
if not defined RDR2_GAME_PID pause
