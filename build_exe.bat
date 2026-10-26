@echo off
cd /d "%~dp0"
echo ==============================================
echo Building SortiFile Standalone Single-File EXE
echo ==============================================

:: Close running SortiFile if currently open
taskkill /F /IM SortiFile.exe 2>nul

:: Publish directly to root
dotnet publish ./src-wpf/SortiFileWpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ==============================================
    echo [SUCCESS] SortiFile.exe created successfully!
    echo Location: %~dp0SortiFile.exe
    echo ==============================================
    del /f /q SortiFile.pdb 2>nul
) else (
    echo.
    echo [ERROR] Build failed. Review the error messages above.
)
pause

