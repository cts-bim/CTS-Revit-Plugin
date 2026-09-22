@echo off
REM Builds CTS Revit Plugin for Revit 2023, 2024, 2025 and 2026 (Release)
REM and packages everything in dist\CTSRevitPlugin.bundle
REM Extra arguments are forwarded, e.g.:  Build.bat -Configuration Debug
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Bundle %*
pause
