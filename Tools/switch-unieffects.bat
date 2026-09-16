@echo off
setlocal EnableExtensions

REM The script lives one level below the project root, e.g. in Tools\.
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "SUB=Packages/media.lightside.unieffects"

if not exist "%ROOT%\.gitmodules" goto :badroot

echo Switching to the UniEffects branches...
echo.

call :sw "%ROOT%" unieffects "LightSideEcosystem"
if errorlevel 1 goto :failed

echo   media.lightside.unieffects -^> restore working tree
git -C "%ROOT%" submodule update --init "%SUB%"
if errorlevel 1 goto :failed

REM submodule update leaves a detached HEAD; commits made there would be
REM unreachable after the next update, so attach the package to its branch.
call :sw "%ROOT%\Packages\media.lightside.unieffects" main "media.lightside.unieffects"
if errorlevel 1 goto :failed

call :sw "%ROOT%\Packages\media.lightside.core" unieffects "media.lightside.core"
if errorlevel 1 goto :failed

call :sw "%ROOT%\Packages\media.lightside.unitext" unieffects "media.lightside.unitext"
if errorlevel 1 goto :failed

call :sw "%ROOT%\Packages\media.lightside.unishapes" unieffects "media.lightside.unishapes"
if errorlevel 1 goto :failed

echo.
echo Current branches:
call :head "%ROOT%" "LightSideEcosystem"
call :head "%ROOT%\Packages\media.lightside.core" "media.lightside.core"
call :head "%ROOT%\Packages\media.lightside.unitext" "media.lightside.unitext"
call :head "%ROOT%\Packages\media.lightside.unishapes" "media.lightside.unishapes"
call :head "%ROOT%\Packages\media.lightside.unieffects" "media.lightside.unieffects"
echo.
echo Done.
exit /b 0

:sw
echo   %~3 -^> %~2
git -C "%~1" checkout "%~2"
exit /b %errorlevel%

:head
for /f "delims=" %%B in ('git -C "%~1" rev-parse --abbrev-ref HEAD 2^>nul') do echo     %~2: %%B
exit /b 0

:badroot
echo.
echo FAILED - no project root at "%ROOT%".
echo Keep this script one level below the root, next to Packages and ProjectSettings.
exit /b 1

:failed
echo.
echo FAILED - see the git error above. The repositories are left as they are.
exit /b 1
