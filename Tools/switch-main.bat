@echo off
setlocal EnableExtensions

REM The script lives one level below the project root, e.g. in Tools\.
REM A wrong root would make the deinit step below match nothing and skip
REM silently, leaving the package in the project, so verify it up front.
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "SUB=Packages/media.lightside.unieffects"
set "PKG=%ROOT%\Packages\media.lightside.unieffects"

if not exist "%ROOT%\.gitmodules" goto :badroot

echo Returning to the main branches...
echo.

REM deinit is only possible while the current branch still tracks the submodule,
REM so it has to run before the checkout. checkout removes an emptied directory
REM but never one that still holds a working tree.
git -C "%ROOT%" ls-files --error-unmatch -- "%SUB%" >nul 2>&1
if errorlevel 1 goto :nosub

REM deinit discards the package's working tree. Commits survive in
REM .git\modules, uncommitted work does not - so refuse to run on a dirty tree.
set "DIRT="
for /f "delims=" %%L in ('git -C "%PKG%" status --porcelain 2^>nul') do set "DIRT=1"
if defined DIRT goto :dirty

echo   media.lightside.unieffects -^> remove working tree
git -C "%ROOT%" submodule deinit "%SUB%"
if errorlevel 1 goto :failed
:nosub

call :sw "%ROOT%" v3.x.x "LightSideEcosystem"
if errorlevel 1 goto :failed

call :sw "%ROOT%\Packages\media.lightside.core" main "media.lightside.core"
if errorlevel 1 goto :failed

call :sw "%ROOT%\Packages\media.lightside.unitext" 3.x.x "media.lightside.unitext"
if errorlevel 1 goto :failed

call :sw "%ROOT%\Packages\media.lightside.unishapes" main "media.lightside.unishapes"
if errorlevel 1 goto :failed

REM rd without /s refuses a non-empty directory, so a surviving working tree is never destroyed.
if exist "%ROOT%\Packages\media.lightside.unieffects" rd "%ROOT%\Packages\media.lightside.unieffects" 2>nul

echo.
echo Current branches:
call :head "%ROOT%" "LightSideEcosystem"
call :head "%ROOT%\Packages\media.lightside.core" "media.lightside.core"
call :head "%ROOT%\Packages\media.lightside.unitext" "media.lightside.unitext"
call :head "%ROOT%\Packages\media.lightside.unishapes" "media.lightside.unishapes"
if exist "%ROOT%\Packages\media.lightside.unieffects" echo     WARNING: Packages\media.lightside.unieffects still exists on disk
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

:dirty
echo.
echo STOPPED: Packages\media.lightside.unieffects has uncommitted changes.
echo Commit or discard them first - removing the working tree would lose them.
echo.
git -C "%PKG%" status --short
exit /b 1

:failed
echo.
echo FAILED - see the git error above. The repositories are left as they are.
exit /b 1
