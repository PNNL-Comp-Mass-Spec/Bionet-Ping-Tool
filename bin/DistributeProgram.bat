@echo off
echo Copying to Gigasax
echo.

xcopy Debug\BionetPingTool.exe \\gigasax\DMS_Programs\BionetPingTool\ /D /Y /F
xcopy Debug\*.dll              \\gigasax\DMS_Programs\BionetPingTool\ /D /Y /F

pause