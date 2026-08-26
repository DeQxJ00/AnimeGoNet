@echo off
setlocal

rem AnimeGoNet local TestSpace launcher.
rem The TestSpace directory is kept outside the source tree and is not committed.
set "PROJECT_ROOT=%~dp0"
set "TESTSPACE_ROOT=%PROJECT_ROOT%..\TestSpace"

set "ANIMEGO_DATA_PATH=%TESTSPACE_ROOT%\animegonet_data"
set "ASPNETCORE_URLS=http://127.0.0.1:6180"

echo AnimeGoNet TestSpace
echo   data_path  = %ANIMEGO_DATA_PATH%
echo   webui      = %ASPNETCORE_URLS%
echo.

dotnet run --project "%PROJECT_ROOT%src\AnimeGoNet.App\AnimeGoNet.App.csproj" -- --urls "%ASPNETCORE_URLS%"
set "EXIT_CODE=%ERRORLEVEL%"
echo.
echo AnimeGoNet exited with code %EXIT_CODE%.
pause
exit /b %EXIT_CODE%
