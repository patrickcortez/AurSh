@echo off

if "%1" == "Commit" (
    call :Commit "%2"
    goto :eof
)

if "%1" == "Push" (
    call :Push "%2"
    goto :eof
)

if "%1" == "" (
    call :Help
    goto :eof
)

goto :eof


:Help
echo Help:
echo Commit : add and commit
echo Push   : push to remote repository
goto :eof


:Commit

git --version >nul 2>&1

if %ERRORLEVEL% EQU 0 (

    echo Staging repository...

    git add -A

    echo Committing changes...

    git commit -m %~1

) else (

    echo Git not found!

)

goto :eof


:Push

git --version >nul 2>&1

if %ERRORLEVEL% EQU 0 (

    if "%~1" == "" (
        echo Branch cannot be empty!
    ) else (
        echo Pushing to branch %~1
        git push origin %~1
    )

) else (

    echo Git is not installed

)

goto :eof