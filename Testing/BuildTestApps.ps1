# PowerShell script for building all of the test programs for testing the Ng911CadIfLib class library.
# This PowerShell script must be run in a Developers PowerShell for VS 2022 command window.

$AppPaths = ".\SimpleLoggingServer", ".\TestNg911CadIfLib", ".\TestNg911CadIfLib"

foreach ($AppPath in $AppPaths)
{
    $BuildOutput = msbuild $AppPath -p:Configuration=Debug
    if ($LASTEXITCODE -eq 0)
    {
        $Host.UI.RawUI.ForegroundColor = "Green"
        Write-Output "$AppPath Build Succeeded"
    }
    else
    {
        $Host.UI.RawUI.ForegroundColor = "Red"
        Write-Output "$AppPath Build Failed"
    }
}

[Console]::ResetColor()

