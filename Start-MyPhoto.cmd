@rem 기존 SDK로 읽기 전용 사진함 개발 화면을 실행합니다.
@echo off
setlocal
pushd "%~dp0"
set "PHOTO_DOTNET=E:\tools\dotnet10\dotnet.exe"
if not exist "%PHOTO_DOTNET%" set "PHOTO_DOTNET=dotnet"
"%PHOTO_DOTNET%" run --project "%~dp0src\PhotoLibrary.Desktop\PhotoLibrary.Desktop.csproj" -c Release -- %*
set "PHOTO_EXIT=%ERRORLEVEL%"
popd
exit /b %PHOTO_EXIT%
