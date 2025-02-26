# Version History

## v1.0.0 - 28 June 2023
| Issue No. | Change Type | Description |
|--------|--------|-------|
| NA       |  New      | Initial version |

## v1.1.0 - 20 Feb 2025
| Issue No. | Change Type | Description |
|--------|----------|-------|
| NA     | Fix      | Added the ShutdownAsync() method to the Ng911CadIfServer class because the Shutdown() method blocks indefinitly in some situations. |
| NA     | Fix      | Was not setting the subscriptionId field of the SubscriptionResponse message for an existing subscription in the ProcessSubscribeRequest() method of the WebSocketSubscription class. |
| NA     | Add      | Added the CadIfWebSocketClient class |
| NA     | Change   | Changed to .NET 8.0 |

## v1.1.1 - 26 Feb 2025
| Issue No. | Change Type | Description |
|--------|----------|-------|
| NA     | Change   | Updated to use version 1.1.0 of the Ng911Lib NuGet package |
| NA     | Change   | Changed the NuGet package to use a release build |


