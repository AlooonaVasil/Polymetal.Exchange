@echo OFF

SET PRODUCTIONCHECKSENABLED=FALSE
SET pdms_installed_dir=C:\AVEVA\Plant\PDMS12.1.SP4
SET PDMS_SERVER=K:\40-“’Œ\Aveva\12.1
SET PDMSPROJ=%PDMS_SERVER%\PROJECT
SET RUN_PDMS=%PDMS_SERVER%\RUN_PDMS\SP4
SET LM_LICENSE_FILE=4545@avevalic1.polymetal.ru
call %RUN_PDMS%\evars %pdms_installed_dir%
call %RUN_PDMS%\projects 
SET PDMSUI=%PDMS_SERVER%\PDMSUI %pdmsui% 
SET PDMSDFLTS=%PDMS_SERVER%\PDMSUI\DFLTS %PDMSDFLTS%
SET PMLLIB=%PDMS_SERVER%\PMLLIB  %pmllib% 
SET CAF_UIC_PATH=%PDMS_SERVER%\UIC
set CAF_ADDINS_PATH=%PDMS_SERVER%\ADDIN
SET PDMSINFO=%PDMS_SERVER%\PROJECT\INFO
SET pdmsplots=%PDMS_SERVER%\PDMSUI\plots %pdmsplots%
SET savepath=%path%
if not "%PDMS_ACAD%"=="" goto acaddone
SET acad_version=%PDMS_ACAD%
SET path=%PDMS_ACAD_PATH%;%path%

rem echo %path%
:acaddone
SET found=
SET remains=%PDMSEXE%
:nextSP
SET nextpart=
for /F "tokens=1,* delims= " %%i in ( "%remains%" ) do SET nextpart=%%i
for /F "tokens=1,* delims= " %%i in ( "%remains%" ) do SET remains=%%j
if "%nextpart%" equ "" goto SPdone
call pdms_findexe "%nextpart%" mon.exe
if not "%found%"=="" goto gotMON
goto nextSP
:SPdone
SET remains=%PDMSEXE%
:nextSE
SET nextpart=
for /F "tokens=1,* delims=;" %%i in ( "%remains%" ) do SET nextpart=%%i
for /F "tokens=1,* delims=;" %%i in ( "%remains%" ) do SET remains=%%j
if "%nextpart%" equ "" goto SEdone
call pdms_findexe "%nextpart%" mon.exe
if not "%found%"=="" goto gotMON
goto nextSE
:SEdone
goto noMON

:gotMON
SET monexe=%found%
SET args=%1 %2 %3 %4 %5 %6
if "%1"=="" SET args=graphics
SET PDMS_ACAD=2006
SET ACAD_VERSION=%PDMS_ACAD%
SET PDMS_ACAD_PATH=C:\program files\AutoCAD 2006;C:\Program Files\Common Files\Autodesk Shared
SET path=%PDMSEXE%\autodraftACAD;%PDMS_ACAD_PATH%;%path%
SET PDMS_CONSOLE_WINDOW=ACTIVE
cmd/c %1 %2 %3
exit /b %ERRORLEVEL%
goto end

:noMON
@echo Sorry, cannot find mon.exe within your PDMSEXE - %PDMSEXE%
pause
goto end
SET path=%savepath%
endlocal