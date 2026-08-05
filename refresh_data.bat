@echo off
REM ============================================================
REM  Refresh the web-app data from "merge data.xlsx".
REM  Double-click this file whenever the Excel file changes.
REM ============================================================
cd /d "%~dp0"
echo Reading "merge data.xlsx" and rebuilding webapp\data.js ...
python build_data.py
if errorlevel 1 (
  echo.
  echo *** Build FAILED. Make sure Python and openpyxl are installed:
  echo ***   pip install openpyxl
  echo.
) else (
  echo.
  echo Done. Open  webapp\index.html  in your browser.
)
echo.
pause
