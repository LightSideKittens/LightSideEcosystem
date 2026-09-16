@echo off
node "%~dp0assetstore.js" prepare
exit /b %errorlevel%
