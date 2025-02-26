# Powershell script for running all of the test probrams for testing the Ng911CadIfLib class library.

# Change this if the .NET version changes in the projects
$Frmwk = "net8.0"

$LoggingServerWd = ".\SimpleLoggingServer\bin\Debug\$FrmWk\"
$LoggingServerApp = "SimpleLoggingServer.exe"
$LoggingServerPath = $LoggingServerWd + $LoggingServerApp

$TestCadIfLibWd = ".\TestNg911CadIfLib\bin\debug\$Frmwk\"
$TestCadIfLibApp = "TestNg911CadIfLib.exe"
$TestCadIfLibPath = $TestCadIfLibWd + $TestCadIfLibApp

$TestCadIfClientWd = ".\TestCadIfClient\bin\Debug\$Frmwk\"
$TestCadIfClientApp = "TestCadIfClient.exe"
$TestCadIfClientPath = $TestCadIfClientWd + $TestCadIfClientApp

$AppPaths = $LoggingServerPath, $TestCadIfLibPath, $TestCadIfClientPath

foreach ($AppPath in $AppPaths)
{
    if (!(Test-Path $AppPath))
    {
        Write-Output "$AppPath does not exist"
        Write-Output "Run BuildTestApps.ps1 from a  Developers PowerShell for VS 2022 command window and try again" 
        exit
    }
}

Start PowerShell -WorkingDirectory $LoggingServerWd -ArgumentList "-NoExit -Command .\$LoggingServerApp"
Start PowerShell -WorkingDirectory $TestCadIfLibWd -ArgumentList "-NoExit -Command .\$TestCadIfLibApp"
Start PowerShell -WorkingDirectory $TestCadIfClientWd -ArgumentList "-NoExit -Command .\$TestCadIfClientApp"
