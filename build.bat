@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title NOVA - build for Windows x64

echo ========================================
echo  NOVA - Release build for Windows x64
echo ========================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK 10 was not found in PATH.
    echo Install the .NET 10 SDK x64, then reopen this window.
    echo https://dotnet.microsoft.com/download/dotnet/10.0
    goto :fail
)

if not exist "Nova.App\Nova.App.csproj" (
    echo ERROR: Project file not found.
    echo Place build.bat in the root of the extracted NOVA source project.
    goto :fail
)

echo Checking installed .NET SDKs...
dotnet --list-sdks | findstr /b /c:"10." >nul
if errorlevel 1 (
    echo ERROR: .NET SDK 10 is not installed or not available to this user.
    echo Install .NET 10 SDK x64: https://dotnet.microsoft.com/download/dotnet/10.0
    goto :fail
)

echo Publishing. The first build may download packages from NuGet.
echo Output: %CD%\publish
echo.
dotnet publish "Nova.App\Nova.App.csproj" --configuration Release --runtime win-x64 --self-contained false --output "%CD%\publish"
if errorlevel 1 goto :fail

echo.
echo ========================================
echo BUILD SUCCEEDED
echo Run: %CD%\publish\Nova.App.exe
echo Requires .NET 10 Desktop Runtime x64 and WebView2 Runtime.
echo Keep all files in the publish folder together.
echo ========================================
echo.
set /p "run=Start NOVA now? [y/N]: "
if /i "%run%"=="y" start "" "%CD%\publish\Nova.App.exe"
echo.
pause
exit /b 0

:fail
echo.
echo BUILD FAILED. See the error messages above.
echo Check your internet connection and installed .NET 10 SDK.
echo.
pause
exit /b 1
