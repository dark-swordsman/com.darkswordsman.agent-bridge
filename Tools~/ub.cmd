@echo off
rem Windows launcher for ub: runs the Python script next to this file with the py launcher, else python.
where py >nul 2>nul || goto python
py -3 "%~dp0ub" %*
exit /b %errorlevel%
:python
python "%~dp0ub" %*
