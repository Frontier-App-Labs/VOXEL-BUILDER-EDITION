@echo off
REM ==========================================================================
REM  Voxel Siege -- Texture Generation Setup
REM  Usage: setup_textures.bat YOUR_GEMINI_API_KEY
REM         setup_textures.bat                       (uses GEMINI_API_KEY env)
REM         setup_textures.bat YOUR_KEY --force       (regenerate all)
REM ==========================================================================

setlocal

REM -- Resolve API key ---
set "API_KEY=%~1"
if "%API_KEY%"=="" (
    if "%GEMINI_API_KEY%"=="" (
        echo ERROR: No API key provided.
        echo Usage: setup_textures.bat YOUR_GEMINI_API_KEY [--force]
        echo    or: set GEMINI_API_KEY=YOUR_KEY then run setup_textures.bat
        exit /b 1
    )
    set "API_KEY=%GEMINI_API_KEY%"
    REM Shift args so %2 becomes the first extra arg
) else (
    shift
)

REM -- Collect remaining args ---
set "EXTRA_ARGS=%1 %2 %3 %4 %5"

echo.
echo ============================================
echo  Voxel Siege -- AI Texture Generator Setup
echo ============================================
echo.

REM -- Install Python dependencies ---
echo [1/2] Installing Python dependencies...
pip install google-generativeai Pillow --quiet
if errorlevel 1 (
    echo.
    echo ERROR: pip install failed. Make sure Python and pip are on your PATH.
    exit /b 1
)

echo.
echo [2/2] Running texture generator...
echo.

REM -- Run the generator ---
python "%~dp0generate_textures.py" --api-key "%API_KEY%" %EXTRA_ARGS%
if errorlevel 1 (
    echo.
    echo ERROR: Texture generation failed. Check the output above for details.
    exit /b 1
)

echo.
echo Setup complete! Textures are in assets\textures\voxels\
echo.

endlocal
