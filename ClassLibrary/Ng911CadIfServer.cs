/////////////////////////////////////////////////////////////////////////////////////
//  File:   Ng911CadIfServer.cs                                     7 Jun 23 PHR
/////////////////////////////////////////////////////////////////////////////////////

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using System.Collections.Concurrent;

using I3V3.LoggingHelpers;
using Eido;
using I3V3.LogEvents;
using Ng911Lib.Utilities;

namespace Ng911CadIfLib;

/// <summary>
/// Class that provides a EIDO conveyance interface between the PSAP CHFE and one or more CAD systems.
/// See NENA-STA-024.1a-2023.
/// </summary>
public class Ng911CadIfServer
{
    #region Private Variables
    private WebApplication app = null;
    private X509Certificate2 m_ServerCert;
    private int m_Port;
    private IPAddress m_Address;
    private string m_WsPath;
    private string m_HttpsEidoPath;
    private string m_SubProtocol = null;
    private CadIfLoggingSettings m_LoggingSettings;
    private I3LogEventClientMgr m_EventClientMgr;
    private MutualAuthenticationDelegate AuthCallback = null;
    private EidoRetrievalCallbackDelegate EidoRetrievalCallback = null;
    /// <summary>
    /// Dictionary for current Web Socket connections. The key is the string version of the remote IP 
    /// endpoint.
    /// </summary>
    private static ConcurrentDictionary<string, WebSocketSubscription> m_ConnectionDictionary =
        new ConcurrentDictionary<string, WebSocketSubscription>();
    #endregion

    /// <summary>
    /// Fired when a new subscription request is accepted.
    /// </summary>
    /// <value>This delegate receives information about the subscriber including its remote endpoint and information from
    /// the subscriber's X.509 certificate.</value>
    public event NewSubscriptionDelegate NewSubscription;

    /// <summary>
    /// Fired when a subscription expires, the subscriber unsubscribes or if the Web Socket connection was terminated abnormally.
    /// </summary>
    /// <value>This delegate receives information about the subscriber including the reason that the subscription, its remote 
    /// endpoint and information from the subscriber's X.509 certificate.</value>
    public event SubscriptionEndedDelegate SubscriptionEnded;

    /// <summary>
    /// The Ng911CadIfServer class fires this event when it accepts a Web Socket connection.
    /// This event is for testing only. The application is not expected to handle this event.
    /// </summary>
    /// <value>This delegate receives information about the client including its remote endpoint, the Web Socket sub-protocol and
    /// the client's X.509 certificate if available.</value>
    public event WssConnectionAcceptedDelegate WssConnectionAccepted;

    /// <summary>
    /// The Ng911CadIfServer class fires this event when a Web Socket connection has ended.
    /// This event is for testing only. The application is not expected to handle this event.
    /// </summary>
    /// <value>This delegate receives the remote endpoint of the client.</value>
    public event WssConectionEndedDelegate WssConnectionEnded;

    /// <summary>
    /// The Ng911CadIfServer class fires this event when it receives an EIDO conveyance protocol message.
    /// This event is for testing only. The application is not expected to handle this event.
    /// </summary>
    /// <value>This delegate receives the remote endpoint of the client that sent the message to the Ng911CadIfServer object
    /// and a string containing the JSON message.</value>
    public event WssMessageReceivedDelegate WssMessageReceived;

    /// <summary>
    /// The Ng911CadIfServer class fires this event when it sends an EIDO conveyance protocol message.
    /// This event is for testing only. The application is not expected to handle this event.
    /// </summary>
    /// <value>This delegate receives the remote endpoint of the client that the message was sent to and a string containing
    /// the JSON message.</value>
    public event WssMessageSentDelegate WssMessageSent;

    /// <summary>
    /// The Ng911CadIfServer class fires this event when it receives an HTTPS GET request for an EIDO.
    /// This event is for testing only. The application is not expected to handle this event.
    /// </summary>
    /// <value>This delegate receives the remote endpoint of the client, the request path and the client's X.509 certificate 
    /// (if provided).</value>
    public event EidoRequestReceivedDelegate EidoRequestReceived;

    /// <summary>
    /// The Ng911CadIfServer class fires this event when it sends a response to an HTTPS GET request for 
    /// an EIDO.
    /// This event is for testing only. The application is not expected to handle this event.
    /// </summary>
    /// <value>This delegate receives the remote endpoint of the client that the EIDO was sent to, the HTTP response code
    /// that was sent to the client and the EidoType object that was sent to the client. </value>
    public event EidoResponseSentDelegate EidoResponseSent;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="ServerCert">X.509 certificate to use for the HTTPS/WSS web socket listener. The 
    /// certificate must contain a private key. Required</param>
    /// <param name="ServerEndPoint">Specifies the IP address and port number that the Ng911CadIfServer will 
    /// listen on. The IP address may be IPAddress.Any, IPAddress.IPv6Any, an IPv4 address or an IPv6 
    /// address. Required.</param>
    /// <param name="WsPath">Specifies the path that clients will use to connect to the Ng911CadIfServer's
    /// Web Socket server. Required. For example: /IncidentData/Ent</param>
    /// <param name="HttpsEidoPath">Specifies the path that clients will use to access the EIDO Retrieval 
    /// service. Required.For example: /incidents/eidos.</param>
    /// <param name="LoggingSettings">Configuration settings for logging I3V3 NG9-1-1 log events. This
    /// parameter is required if the LoggingInterface parameter is not null.</param>
    /// <param name="LoggingInterface">Specifies the I3LogEventClientMgr object to use for sending NG9-1-1 log 
    /// events. If this parameter is null then the Ng911CadIfServer object will not send NG9-1-1 log events.
    /// If non-null, then the I3LogEventClientMgr object must be configured and running.</param>
    /// <param name="MutualAuthCallback">Callback delegate that allows the caller to perform custom 
    /// authentication of the WebSocket client.</param>
    /// <param name="RetrievalCallback">Specifies a callback for a function that the Ng911CadIfServer object 
    /// will call to retrieve an EIDO from the application (PSAP CHFE) when a functional element (i.e. another 
    /// PSAP system) attempts to retrieve an EIDO using the EIDO Retrieval Service interface of the 
    /// Ng911CadIfServer object.</param>
    public Ng911CadIfServer(X509Certificate2 ServerCert, IPEndPoint ServerEndPoint, string WsPath, 
        string HttpsEidoPath, I3LogEventClientMgr LoggingInterface, CadIfLoggingSettings LoggingSettings, 
        MutualAuthenticationDelegate MutualAuthCallback, EidoRetrievalCallbackDelegate RetrievalCallback)
    {
        if (LoggingInterface != null && LoggingSettings == null)
            throw new ArgumentException("A logging client manager is provided but no Logging" +
                "Settings parameter is provided");

        if (ServerCert == null)
            throw new ArgumentNullException($"The {nameof(ServerCert)} parameter is null. This " +
                "parameter is required.");
        
        if (ServerEndPoint == null)
            throw new ArgumentNullException($"The {nameof(ServerEndPoint)} parameter is null. This " +
                "parameter is required.");

        if (string.IsNullOrEmpty(WsPath) == true)
            throw new ArgumentNullException($"The {nameof(WsPath)} parameter is null. This parameter " +
                "is required.");

        if (string.IsNullOrEmpty(HttpsEidoPath) == true)
            throw new ArgumentNullException($"The {nameof(HttpsEidoPath)} parameter is null. This " +
                "parameter is required.");

        if (RetrievalCallback == null)
            throw new ArgumentNullException($"The {nameof(RetrievalCallback)} parameter is null. This" + 
                "is required.");

        m_ServerCert = ServerCert;
        m_Port = ServerEndPoint.Port;
        m_Address = ServerEndPoint.Address;
        m_WsPath = WsPath;
        m_HttpsEidoPath = HttpsEidoPath;
        m_LoggingSettings = LoggingSettings;
        m_EventClientMgr = LoggingInterface;
        AuthCallback = MutualAuthCallback;
        EidoRetrievalCallback = RetrievalCallback;
    }

    /// <summary>
    /// Causes the Ng911CadIfServer object to start its HTTPS server listening for Web Socket requests and
    /// HTTPS EIDO Retrieval Service requests.
    /// </summary>
    public void Start()
    {
        if (app != null)
            return;     // Already started

        ClientCertificateMode CertMode;
        if (AuthCallback == null)
            CertMode = ClientCertificateMode.AllowCertificate;
        else
            CertMode = ClientCertificateMode.RequireCertificate;

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        // See: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-7.0
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(m_Address, m_Port, listenOptions =>
            {
                listenOptions.UseHttps(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = m_ServerCert,
                    ClientCertificateMode = CertMode,
                    ClientCertificateValidation = DoMutualAuthentication
                });
            });
        })
            .ConfigureLogging((context, logging) =>
            {   // Turn off logging because the ASP .NET CORE framework generates a lot of meaningless
                // log messages.
                logging.ClearProviders();
            });

        app = builder.Build();

        WebSocketOptions webSocketOptions = new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(10),
        };
        app.UseWebSockets(webSocketOptions);

        app.Use(RequestHandler);
        app.StartAsync(CancellationToken.None);
     }

    internal void FireNewSubscription(string strSubscriptionId, string strIdType, string strId, IPEndPoint RemIpe)
    {
        NewSubscription?.Invoke(strSubscriptionId, strIdType, strId, RemIpe);
    }

    internal void FireSubscriptionEnded(string strSubscriptionId, string strIdType, string strId,
        IPEndPoint RemIpe, string strReason)
    {
        SubscriptionEnded?.Invoke(strSubscriptionId, strIdType, strId, RemIpe, strReason);
    }

    /// <summary>
    /// Performs a graceful shutdown by terminating all subscriptions and stopping the Web Socket listener.
    /// <para>
    /// WARNING: This method attempts to shut down the WebApplication object synchronously. Under certain conditions
    /// this can cause this method to block indefinitely. Use the ShutdownAsync() method instead and await its completion
    /// instead of using this method.
    /// </para>
    /// </summary>
    public void Shutdown()
    {
        if (app == null) 
            return;

        ICollection<WebSocketSubscription> Connections = m_ConnectionDictionary.Values;
        foreach (WebSocketSubscription connection in Connections)
        {
            connection.Shutdown();  // Terminates the subscription if there is one.
        }

        m_ConnectionDictionary.Clear();

        app.StopAsync().Wait();
        app = null;
    }

    /// <summary>
    /// Performs a graceful shutdown by terminating all subscriptions and stopping the Web Socket listener. Shuts down
    /// the WebApplication asynchronously.
    /// </summary>
    /// <returns>Returns an awaitable Task object.</returns>
    public async Task ShutdownAsync()
    {
        if (app == null)
            return;

        ICollection<WebSocketSubscription> Connections = m_ConnectionDictionary.Values;
        foreach (WebSocketSubscription connection in Connections)
        {
            connection.Shutdown();  // Terminates the subscription if there is one.
        }

        m_ConnectionDictionary.Clear();

        await app.StopAsync();
        app = null;
    }

    /// <summary>
    /// Sends a single EIDO document to all subscribers. This method queues the EIDO for transmission
    /// and returns immediately. This method should be called when a new EIDO is created or an
    /// existing EIDO is updated.
    /// </summary>
    /// <param name="Eido">EIDO to send</param>
    public void SendEido(EidoType Eido)
    {
        if (app == null)
            throw new InvalidOperationException("The Web Socket server is not running yet. Call Start() " +
                "before calling SendEido()");

        if (Eido == null)
            throw new ArgumentNullException(nameof(Eido));

        ICollection<WebSocketSubscription> Connections = m_ConnectionDictionary.Values;
        foreach (WebSocketSubscription connection in Connections)
        {
            connection.SendEido(Eido);
        }
    }

    /// <summary>
    /// Sends a list of all EIDOs for active incidents to send to a specific subscriber. This method queues
    /// the EIDOs and returns immediately.
    /// </summary>
    /// <param name="strSubscriptionId">Subscription ID of the subscriber.</param>
    /// <param name="Eidos">List of EIDOs to send.</param>
    public void SendEidosToSubscriber(string strSubscriptionId, List<EidoType> Eidos)
    {
        if (app == null)
            throw new InvalidOperationException("The Web Socket server is not running yet. Call Start() " +
                "before calling SendEidosToSubscriber()");

        if (string.IsNullOrEmpty(strSubscriptionId)) 
            throw new ArgumentNullException(nameof(strSubscriptionId));

        if (Eidos == null)
            throw new ArgumentNullException(nameof(Eidos));

        ICollection<WebSocketSubscription> Connections = m_ConnectionDictionary.Values;
        foreach (WebSocketSubscription connection in Connections)
        {
            connection.SendEidosToSubscriber(strSubscriptionId, Eidos);
        }
    }

    private void SendEidoLogEvent(EidoType eido, IPEndPoint RemIpe)
    {
        if (m_EventClientMgr == null)
            return;

        EidoLogEvent Ele = new EidoLogEvent();
        Ele.timestamp = TimeUtils.GetCurrentNenaTimestamp();
        Ele.elementId = m_LoggingSettings.ElementId;
        Ele.agencyId = m_LoggingSettings.AgencyId;
        Ele.agencyAgentId = m_LoggingSettings.AgencyAgentId;
        Ele.agencyPositionId = m_LoggingSettings.AgencyPositionId;
        Ele.ipAddressPort = RemIpe.ToString();
        Ele.callId = eido.callComponent?[0].Id;
        Ele.incidentId = eido.Id;
        Ele.body = EidoHelper.SerializeToString(eido);
        Ele.direction = "outgoing";
        m_EventClientMgr.SendLogEvent(Ele);
    }

    private async Task EidoRequestHandler(HttpContext context)
    {
        HttpResponse Resp = context.Response;

        IPEndPoint RemIpe = new IPEndPoint(context.Connection.RemoteIpAddress, context.Connection.RemotePort);
        string strPath = context.Request.Path;
        int Idx = strPath.LastIndexOf("/");
        EidoRequestReceived?.Invoke(RemIpe, strPath, context.Connection.ClientCertificate);
        if ((Idx + 1) >= strPath.Length)
        {
            context.Response.StatusCode = 400;  // Bad Request
            EidoResponseSent?.Invoke(RemIpe, context.Response.StatusCode, null);
            return;
        }

        string EidoReferenceId = strPath.Substring(Idx + 1);
        int RespCode;
        EidoType eido = EidoRetrievalCallback(EidoReferenceId, context.Connection.ClientCertificate, RemIpe, 
            out RespCode);
        if (eido == null)
        {
            Resp.StatusCode = RespCode;
            EidoResponseSent?.Invoke(RemIpe, context.Response.StatusCode, null);
            return;
        }

        Resp.ContentType = "application/emergency.eido+json";
        string strEido = EidoHelper.SerializeToString(eido);
        if (string.IsNullOrEmpty(strEido) == true)
        {   // Something is wrong with the EIDO received from the application.
            Resp.StatusCode = 500;  // Internal Server Error
            EidoResponseSent?.Invoke(RemIpe, context.Response.StatusCode, eido);
            return;
        }

        Resp.StatusCode = 200;
        await Resp.WriteAsync(strEido);

        SendEidoLogEvent(eido, RemIpe);
        EidoResponseSent?.Invoke(RemIpe, context.Response.StatusCode, eido);
    }

    private async Task WssRequestHandler(HttpContext context)
    {
        if (context.Request.IsHttps == false)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        if (context.WebSockets.IsWebSocketRequest == true)
        {
            if (context.Request.Headers.ContainsKey("Sec-WebSocket-Protocol") == true)
                m_SubProtocol = context.Request.Headers["Sec-WebSocket-Protocol"];

            X509Certificate2 ClientCert = context.Connection.ClientCertificate;
            WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
            IPEndPoint RemIpe = new IPEndPoint(context.Connection.RemoteIpAddress, context.Connection.
                RemotePort);
            string strIpe = RemIpe.ToString();

            WebSocketSubscription Connection = new WebSocketSubscription(webSocket, this, m_LoggingSettings,
                m_EventClientMgr, ClientCert, RemIpe);
            m_ConnectionDictionary.TryAdd(strIpe, Connection);
            WssConnectionAccepted?.Invoke(RemIpe, m_SubProtocol, ClientCert);
            await Connection.ManageConnectionAsync();
            m_ConnectionDictionary.TryRemove(strIpe, out Connection);
            WssConnectionEnded?.Invoke(RemIpe);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
    }

    private async Task RequestHandler(HttpContext context, RequestDelegate next)
    {
        string str = context.Request.Path;
        if (string.IsNullOrEmpty(str) == true)
        {
            context.Response.StatusCode = 400;      // Bad Request
            return;
        }

        str = str.ToLower();
        if (str.IndexOf(m_HttpsEidoPath.ToLower()) >= 0)
        {
            await EidoRequestHandler(context);
            return;
        }

        if (str.IndexOf(m_WsPath.ToLower()) >= 0)
        {
            await WssRequestHandler(context);
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Disables connection based client certificate validation so the middleware can handle it instead.
    /// Or, custom validation can be handled here.
    /// </summary>
    /// <param name="certificate">The certificate used to authenticate the remote party.</param>
    /// <param name="chain">The chain of certificate authorities associated with the remote certificate.
    /// </param>
    /// <param name="errors">One or more errors associated with the remote certificate.</param>
    /// <returns>A Boolean value that determines whether the specified certificate is accepted for 
    /// authentication.</returns>
    private bool DoMutualAuthentication(X509Certificate2 certificate, X509Chain chain, SslPolicyErrors 
        errors)
    {
        if (AuthCallback != null)
            return AuthCallback(certificate, chain, errors);
        else
            return true;
    }

    internal void FireWssMessageReceived(IPEndPoint RemIpe, string JsonString)
    {
        WssMessageReceived?.Invoke(RemIpe, JsonString);
    }

    internal void FireWssMessageSent(IPEndPoint RemIpe, string JsonString)
    {
        WssMessageSent?.Invoke(RemIpe, JsonString);
    }

    internal void FireEidoRequestReceived(IPEndPoint RemIpe, string RequestPath, X509Certificate2
        ClientCertificate)
    {
        EidoRequestReceived?.Invoke(RemIpe, RequestPath, ClientCertificate);
    }

    internal void FireEidoResponseSent(IPEndPoint RemIpe, int ResponseCode, EidoType eido)
    {
        EidoResponseSent?.Invoke(RemIpe, ResponseCode, eido);
    }
}

