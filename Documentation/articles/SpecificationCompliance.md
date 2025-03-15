# Specification Compliance
The document entitled [Conveyance of Emergency Incident Data Objects (EIDOs) between Next Generation (NG 9-1-1) Systems and Applications (NENA-STA-024.1a-2023)](https://cdn.ymaws.com/www.nena.org/resource/resmgr/standards/nena-sta-024.1a-2023_eidocon.pdf) specifies the requirements for the transport of EIDOs between systems.

The Ng911CadIfLib class library implements a simplified set of requirements appropriate for the interface between a PSAP system and a CAD system. The following table indicates which portions of the NENA standard this class library supports.

| NENA-STA-024 Section No. | Section Title | Supported? | Description |
|--------|--------|---------------|----------------------|
| 2.1.1   | General Description  | Partial | See [General Requirements](#GeneralRequirements) below |
| 2.1.2   | URI Scheme | Yes |  |
| 2.1.3.1.1 | Negotiate Web Socket | Yes |  |
| 2.1.3.1.2 | HTTP Headers | Yes |   |
| 2.1.4  | Client Web Socket API Actions | NA  | Not Applicable |
| 2.1.5 | Objects | Yes |  |
| 2.1.6 | Notification Model | Yes |  |
| 2.2   | Min Rates and Max Rates In Detail | Partial | Support for Min Rates is provided. Max Rates support is not required. |
| 2.3 | qualFilter | No | The intended use of the Ng911CadIfLib class library is for communication between a single agency’s PSAP CHFE and that agency’s CAD system so EIDO filtering will not be required. |
| 2.4 | Transport in Call Signaling | No | Not Applicable |
| 2.5 | EIDO Dereference Factory | No |  |
| 2.6 | EIDO Dereference Service | No |  |
| 2.7 | EIDO Retrieval Service | Yes |  |
| 2.8 | Data Rights Management | No |  |
| 2.9 | Logging | Yes |  |
| 2.10 | Security | Partial | TLS with mutual authentication using PCA issued certificates is supported. Digital Rights Management is not supported. |

## <a name="GeneralRequirements">NENA-STA-024.1a-2023 General Requirements</a>
Section 2.1.1 of NENA-STA-024.1a-2023 specifies various general requirments for the EIDO conveyance protocol. The following table indicates which of these requirments are supported by the Ng911CadIfLib class library.

| Section 2.1.1 Requirement | Supported? | Description |
|--------|--------|--------------|
| The Ng911CadIfServer class shall support Mutual authentication with credentials traceable to the PSAP Credentialing Agency (PCA) | Yes    |  |
| For interoperability, all implementations SHALL accept subscription requests of any size up to and including 65,536 bytes. | Yes |   |
| All communication related to a subscription is handled over the Web Socket. | Yes  |
| Upon successful subscription, notifications SHALL be sent to the subscriber as they match the criteria associated with the subscription until the subscription expires or client unsubscribes. | No | The Ng911CadIfServer class library does not support EIDO filtering. |
| The server shall accept subscriptions for new incidents or for a single incident. | Yes |  |
| For a new incidents subscription, a notification SHALL be sent immediately that contains all active incidents that match the criteria. | Yes |  |
| For a subscription for new incidents, if there are no EIDOs that match the criteria of the new subscription request, the server shall send an empty notification message. | Yes |
| A Single Incident subscription SHALL be accepted for a closed incident if the request is made within five minutes of its closure, in which case a single EIDO representing the last state of that incident SHALL be sent and no further notifications will be made for that incident. The server shall only accept changes to a subscription that changes the expiration of the subscription. | No |  |
| Once established, a Web Socket SHALL handle multiple subscriptions. | Yes | Only a single subscription per Web Socket needs to be supported because EIDO filtering is not required. If the Ng911CadIfServer object receives a new subscription request when a subscription already exists, it shall terminate the existing subscription and accept the new subscription. |
| Upon expiration of the last subscription still in effect the server will close the Web Socket. It is up to the subscriber to detect this closure and act appropriately | Yes |  |
| If the Web Socket is closed (including if the underlying TCP socket is closed) then the server shall flush all subscriptions established using that Web Socket without notifying the subscriber. | Yes |  |
