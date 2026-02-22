@echo off
echo ============================================
echo   WallArt - Build and Publish
echo ============================================
echo.

echo [1/2] Publishing application...
dotnet publish -c Release -r win-x64 --self-contained true -o bin\publish

echo.
echo [2/2] Build complete!
echo.
echo Published files are in: bin\publish\
echo.
echo To create the installer:
echo   1. Download Inno Setup 6 from https://jrsoftware.org/isdl.php
echo   2. Open installer.iss in Inno Setup Compiler
echo   3. Click Build ^> Compile
echo   4. The installer EXE will be in: installer_output\WallArt_Setup.exe
echo.
echo Or distribute the files in bin\publish\ directly.
echo ============================================
pause
