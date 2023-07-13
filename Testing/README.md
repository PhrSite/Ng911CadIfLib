# Introduction
The Testing directory contains three applications that can be used to test the Ng911CadIfLib class library. These test programs can be used together to test the functionality of the Ng911CadIfLib class library.

This directory provides a PowerShell script that can be used to build all of the test programs (see <a href="#Building">Building the Test Programs</a>) and a PowerShell script that will launch all three applications in separate PowerShell command line windows (see <a href="#Running">Runing the Test Programs</a>)

## TestNg911CadIfLib
The TestNg911CadIfLib test program simulates the operation of PSAP CHFE system by creating an instance of the [Ng911CadIfServer](https://phrsite.github.io/Ng911CadIfLibDocumentation/api/Ng911CadIfLib.Ng911CadIfServer.html) class and configuring it to accept Web Socket EIDO conveyance subscription requests.

This program writes the contents of all EIDO conveyance protocol messages to the console window as well as the status of its connection to a NG9-1-1 log event server.

Type send and this application will send a fixed EIDO document to all subscribers.

Press the Enter key to terminate the program. The program will perform an orderly shutdown by terminating all existing subscriptions and closing all Web Socket connections.

Press Ctrl-C to abruptly terminate the program to simulate a failure.

The TestNg911CadIfLib application listens on port 10000 of all IPv6 addresses. The URLs for its interfaces are:

| URL | Interface |
|--------|--------|
| wss://[::1]:10000/IncidentData/ent | WebSocket EIDO conveyance protocol subscription URL |
| https://[::1]:10000//incidents/eidos/{RefID} | EIDO Retrival Service URL. The last path in the request "{RefID}" is a reference to an EIDO document. This program accepts any reference ID value and always returns the same EIDO object. |

Clients can use the IPv6 local loopback address (::1) if connecting from the same machine or a specific IPv6 address if connecting from another machine.

This test program configures the instance of the Ng911CadIfServer object to send NG9-1-1 log events to an instance of an NG9-1-1 Log Event Server that listens on port 11000 of the IPv4 local loopback address (127.0.0.1) so that the log events supported by the Ng911CadIfServer can be tested. See <a href="Sls">SimpleLoggingServer</a> below.

## TestCadIfClient
This test program simulates a CAD system by subscribing to receive EIDOs using the EIDO conveyance Web Socket protocol. It is also capable of retrieving EIDOs using the EIDO Retrieval Service of the Ng911CadIfServer class.

Multiple instances of the program can be run in separate console windows in order to test the ability of the Ng911CadIfServer class's ability to handle multiple client connections.

When this program is run, it connects to the URLs of the TestNg911CadIfLib test program shown in the previous section. It automatically subscribes to the Ng911CadIfServer using the following subscription parameters.

| Subscribe Parameter | Value |
|--------|--------|
| requestType | "eido" |
| requestSubType | "new" |
| expires | 60 seconds |
| minRate | 20 seconds |

If the initial subscription is successful then the TestCadIfClient application will periodically resubscribe two seconds prior to the expiration of the subscription to keep the subscription alive.

If the initial subscrption request was not successful then you must restart the application in order to try again.

If the subscription is terminated by the server, by an unsubscripte command (see below) or if the Web Socket connection is closed, then you must must restart the application in order to subscribe again.

The TestCadIfClient application will write EIDOs it receives to the console window when it receives them via the Web Socket connection or via the EIDO Retrieval Service.

The TestCadIfClient application accepts the following commands via its command-line interface.

| Command | Description |
|--------|--------|
| get  | Gets an EIDO document via the EIDO Retrieval Service of the Ng911CadIfServier class instance being hosted by the TestNg911CadIfLib test application. This command can be run even if the client is not subscribed to the EIDO conveyance Web Socket server.|
| status | Writes the subscription status of the client (subscribed or unsubscribed) |
| unsubscribe | Unsubscribes from the EIDO conveyance Web Socket server. Once unsubscribed, the application will not attempt to subscribe again. Restart the application to subscribe. |

Press the Enter key to terminate the program. This causes the TestCadIfClient application to close its end of the Web Socket without unsubscribing.

## <a name="Sls">SimpleLoggingServer</a>
The SimpleLoggingServer application is a very simple NG9-1-1 Log Event server. It listens for NG9-1-1 log event messages and writes the contents of each log event message to the console window.

This application listens on port 11000 of all IPv4 addresses. It supports the following URLs.

| URL | Description |
|--------|--------|
| https://127.0.0.1:11000/LogEvents | For posting NG9-1-1 log events |
| https://127.0.0.1:11000/Versions | For getting the versions that this log event server supports |

Use the IPv4 local loopback address (127.0.0.1) if connecting from the same machine that this application is running on or a specific IPv4 address if connecting from another machine.

Press Ctrl-C to stop the application.

# <a name="Building">Building the Test Programs</a>
Perform the following actions to build all of the Ng911CadIfLib test programs.
1. Open a Developer PowerShell for VS 2022 window.
2. Change directory to the Testing sub-diretory under the Ng911CadIfLib project
3. Run the BuildTestApps.ps1 PowerShell script by typing .\BuildTestApps.ps1.

The BuildTestApps.ps1 script builds the Debug configuration of all three test programs.
# <a name="Running">Running the Test Programs</a>
Perform the following actions to run all three test applications.
1. Open a Developer PowerShell for VS 2022 window.
2. Change directory to the Testing sub-diretory under the Ng911CadIfLib project
3. Run the RunTests.ps1 PowwerShell script by typing .\RunTests.ps1.

The RunTests.ps1 script will launch the Debug build of each test program in a separate PowerShell command prompt window.

