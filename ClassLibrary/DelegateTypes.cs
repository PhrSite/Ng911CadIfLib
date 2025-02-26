/////////////////////////////////////////////////////////////////////////////////////
//  File:   DelegateTypes.cs                                        27 Jun 23 PHR
/////////////////////////////////////////////////////////////////////////////////////

using Eido;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Ng911CadIfLib;

/// <summary>
/// Callback delegate that the application using the Ng911CadIfServer class can use to perform custom
/// authentication and/or extended authorization.
/// </summary>
/// <param name="certificate">Client certificate provided with the WebSocket connection request. May
/// be null if no client certificate was provided.</param>
/// <param name="chain">Contains the chain of certificate authorities associated with the remote certificate.
/// </param>
/// <param name="errors">One or more errors associated with the remote certificate.</param>
/// <returns>Return true to allow the connection request or false to deny it.</returns>
public delegate bool MutualAuthenticationDelegate(X509Certificate2 certificate, X509Chain chain,
    SslPolicyErrors errors);

/// <summary>
/// Callback delegate that the Ng911CadIfServer class uses to retrieve an EIDO from the PSAP CHFE for
/// the EIDO Retrieval Service.
/// </summary>
/// <param name="EidoReferenceId">Requested EIDO reference ID. This is the last element of the request 
/// path.</param>
/// <param name="ClientCert">X.509 certificate of the client. Will be null if the client did not provide a 
/// certificate. If not null, the application can use the information in this object to determine whether or 
/// not to provide the requested EIDO to the client.</param>
/// <param name="RemIpe">IP endpoint that contains the IP address and port number of the remote client that 
/// is requesting the EIDO for a specific interface via an HTTPS GET request.</param>
/// <param name="ResponseCode">Response code. This is an output parameter that the application provides. If 
/// the application returns an EidoType object, then the response code shall be 200 (OK). Otherwise, the 
/// response code should be set a value that indicates the error condition (for example 404 for not found).
/// </param>
/// <returns>An EidoType object if the requested EIDO was found or null if not found or if the client
/// is not allowed access the EIDO.</returns>
public delegate EidoType EidoRetrievalCallbackDelegate(string EidoReferenceId, X509Certificate2 ClientCert,
    IPEndPoint RemIpe, out int ResponseCode);

/// <summary>
/// Delegate for the NewSubscription event of the Ng911CadIfServer class.
/// </summary>
/// <param name="strSubscriptionId">Subscription ID of the new subscription.</param>
/// <param name="strIdType">Identifies the type of functional element that subscribed to receive EIDO 
/// notification events. This information is from the idType field of the other name field of the 
/// SubjectAltName of the subscriber's X.509 certificate that was issued by the PSAP Credentialing Agency (PCA). 
/// Will be null if the subscriber did not provide a client certificate.</param>
/// <param name="strId">ID of the subscriber from the subscriber's X.509 certificate. Will be null if the subscriber did
/// not provide a client certificate</param>
/// <param name="RemIpe">IP endpoint of the subscriber. The IPEndpoint contains the IP address and port number 
/// of the subscriber.</param>
public delegate void NewSubscriptionDelegate(string strSubscriptionId, string strIdType, string strId, IPEndPoint RemIpe);

/// <summary>
/// Delegate for the SubscriptionEnded event of the Ng911CadIfServer class.
/// </summary>
/// <param name="strSubscriptionId">Subscription ID of the new subscription.</param>
/// <param name="strIdType">Identifies the type of functional element that subscribed to receive EIDO 
/// notification events. This information is from the idType field of the other name field of the 
/// SubjectAltName of the client X.509 certificate that was issued by the PSAP Credentialing Agency (PSAP). 
/// Will be null if the subscriber did not provide a client certificate.</param>
/// <param name="strId">ID of the subscriber from the X.509 certificate. Will be null if the subscriber did
/// not provide a client certificate</param>
/// <param name="RemIpe">IP endpoint of the subscriber. The IPEndpoint contains the IP address and port number 
/// of the subscriber.</param>
/// <param name="strReason">Indicates the reason that the subscription was terminated. For example: 
/// “Unsubscribed”, “Expired” or “Disconnected”.</param>
public delegate void SubscriptionEndedDelegate(string strSubscriptionId, string strIdType, string strId,
    IPEndPoint RemIpe, string strReason);

/// <summary>
/// Delegate for the WssConnectionAccepted event of the Ng911CadIfServer class.
/// </summary>
/// <param name="RemIpe">Endpoint of the client that requested the connection.</param>
/// <param name="SubProtocol">Web Socket sub-protocol. May be null if not provided by the client.</param>
/// <param name="ClientCertificate">The client’s X.509 certificate. May be null if the client did not provide a 
/// certificate.</param>
public delegate void WssConnectionAcceptedDelegate(IPEndPoint RemIpe, string SubProtocol, X509Certificate2 
    ClientCertificate);

/// <summary>
/// Delegate for the WssConnectionEnded event of the Ng911CadIfServer class.
/// </summary>
/// <param name="RemIpe">Endpoint of the client.</param>
public delegate void WssConectionEndedDelegate(IPEndPoint RemIpe);

/// <summary>
/// Delegate for the WssMessageReceived event of the Ng911CadIfServer class.
/// </summary>
/// <param name="RemIpe">Endpoint of the client</param>
/// <param name="JsonString">JSON message that was received.</param>
public delegate void WssMessageReceivedDelegate(IPEndPoint RemIpe, string JsonString);

/// <summary>
/// Delegate for the WssMessageSent event of the Ng911CadIfServer class.
/// </summary>
/// <param name="RemIpe">Endpoint of the client that the message was sent to</param>
/// <param name="JsonString">JSON message that was received.</param>
public delegate void WssMessageSentDelegate(IPEndPoint RemIpe, string JsonString);

/// <summary>
/// Delegate for the EidoRequestReceived event of the Ng911CadIfServer class.
/// </summary>
/// <param name="RemIpe">Endpoint of the client</param>
/// <param name="RequestPath">Path from the HTTPS GET request</param>
/// <param name="ClientCertificate">The client’s X.509 certificate. May be null if the client did not provide 
/// a certificate.</param>
public delegate void EidoRequestReceivedDelegate(IPEndPoint RemIpe, string RequestPath, X509Certificate2 ClientCertificate);

/// <summary>
/// Delegate for the EidoResponseSent event of the Ng911CadIfServer class.
/// </summary>
/// <param name="RemIpe">Endpoint of the client that the EIDO was sent to.</param>
/// <param name="ResponseCode">HTTP response code that was sent.</param>
/// <param name="eido">EIDO document that was sent. Will be null if the ResponseCode is not 200.</param>
public delegate void EidoResponseSentDelegate(IPEndPoint RemIpe, int ResponseCode, EidoType eido);

/// <summary>
/// Delegate type for the EidoReceived event of the CadIfWebSocketClient class.
/// </summary>
/// <param name="eido">EIDO that was received</param>
/// <param name="strServerUri">URI of the server that the CadIfWebSocketClient is connected to.</param>
public delegate void EidoReceivedDelegateType(EidoType eido, string strServerUri);

/// <summary>
/// Delegate type for the CadIfConnectionState event of the CadIfWebSocketClient class.
/// </summary>
/// <param name="IsConnected">If true, the a Web Socket connection has been established to the CAD I/F server (EIDO notifier).
/// If false if the Web Socket connection has been interrupted or the attempt to connect failed.</param>
/// <param name="strServerUri">URI that identifies the CAD I/F server.</param>
public delegate void CadIfConnectionStateDelegate(bool IsConnected, string strServerUri);

/// <summary>
/// Delegate type for the CadIfSubsriptionState event of the CadIfWebSocketClient class.
/// </summary>
/// <param name="IsSubscribed">If true, then a subscription to the CAD I/F server has been established. If false
/// then a subscription could not be established or the server terminated the subscription.</param>
/// <param name="strServerUri">URI that identifies the CAD I/F server.</param>
public delegate void CadIfSubscriptionStateDelegate(bool IsSubscribed, string strServerUri);
