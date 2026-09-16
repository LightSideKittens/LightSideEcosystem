@echo off
node "%~dp0assetstore.js" restore
exit /b %errorlevel%
