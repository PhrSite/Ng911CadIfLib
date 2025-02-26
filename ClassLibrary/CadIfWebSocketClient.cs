/////////////////////////////////////////////////////////////////////////////////////
//  File:   CadIfWebSocketClient.cs                                 13 Feb 25 PHR
/////////////////////////////////////////////////////////////////////////////////////

using System.Net.WebSockets;
using System.Text;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Eido;
using WebSocketSubNot;

namespace Ng911CadIfLib;

/// <summary>
/// This class implements the client side of the NG9-1-1 Emergency Incident Data Object (EIDO) transport protocol specified in
/// the document entitled "NENA Standard for the Conveyance of Emergency Incident Data Objects (EIDOs) between Next 
/// Generation (NG9-1-1) Systems and Applications", NENA-STA-024.1a-2023.
/// <para>
/// The transport protocol between the client and the EIDO server uses WEB sockets. The client subscribes to the server and
/// the server sends notification message containing one or more EIDOs to the client when they become available.
/// </para>
/// <para>
/// When the client receives an EIDO from the server, it fires an event containing the EIDO to the application or component
/// using this class.
/// </para>
/// </summary>
public class CadIfWebSocketClient
{
    private object m_SocketLock = new object();
    private ClientWebSocket m_WebSocket = null;
    private CancellationTokenSource m_ReceiveCancellationTokenSource = new CancellationTokenSource();
    private CancellationTokenSource m_ConnectCancellationTokenSource = new CancellationTokenSource();
    private Task m_ConnectTask = null;
    private Task m_ReceiveTask = null;
    private string m_strSubscriptionId = null;
    private int m_ExpiresSeconds;
    private DateTime m_LastSubscribeSentTime = DateTime.Now;
    private SemaphoreSlim m_FirstSubscribeSemaphore = null;
    private SemaphoreSlim m_UnsubscribeRequestSemaphore = null;

    /// <summary>
    /// Specifies the sub-protocol for the WEB sockets transport protocol. See Section 2.1.3.1.2 of NENA-STA-024.1a-2023.
    /// </summary>
    private const string SubProtocol = "emergency-ent1.0";

    private string m_strUri;
    private Uri m_Uri;
    private DateTime m_LastConnectRequestTime = DateTime.MinValue;
    private const int ConnectRequestIntervalMs = 5000; 
    private bool m_Started = false;
    private X509Certificate m_ClientCertificate;
    private RemoteCertificateValidationCallback m_ValidationCallback;

    /// <summary>
    /// This event is fired when an EIDO is received from the server
    /// </summary>
    /// <value>This delegate receives an EidoType object and the URI of the EIDO server.</value>
    public event EidoReceivedDelegateType EidoReceived = null;

    /// <summary>
    /// This event is fired when the Web Socket connection to the server is established or lost.
    /// </summary>
    /// <value>This delegate receives the current connection state of the Web Socket connection to the server and the URI of
    /// the server.</value>
    public event CadIfConnectionStateDelegate CadIfConnectionState = null;

    /// <summary>
    /// This event is fired when the subscription to the server is established or terminated.
    /// </summary>
    /// <value>This event receives the current subscription state and the URI of the server.</value>
    public event CadIfSubscriptionStateDelegate CadIfSubscriptionState = null;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="clientCertificate">X.509 certificate for this client if the server is using secure WEB sockets
    /// (WSS) and if the server is known to authenticate clients (mutual authentication). This parameter may be null if
    /// the server is known to not authenticate clients. This parameter may be non-null even if not using WSS with
    /// mutual authentication.</param>
    /// <param name="validationCallback">This callback function is called by the .NET network libraries during the TLS handhake.
    /// The parameters include the server's X.509 certificate and other information so that the application can authenticate
    /// the server. If this method returns true then the connection will be allowed. If it returns false then the connection will
    /// be prevented. This parameter is optional. If it is null then this class will use a default validation callback that 
    /// allows connection to any server.</param>
    /// <param name="strUri">WS or WSS URI of the server. For example: wss://192.168.1.84:16000/IncidentData/ent</param>
    /// <param name="expiresSeconds">Specifies the expiration time of the subscription to the server in seconds. Must be greater
    /// than 20 seconds.</param>
    /// <exception cref="UriFormatException">This exception is thrown if the strUri parameter is not a valid URI.</exception>
    public CadIfWebSocketClient(X509Certificate2 clientCertificate, RemoteCertificateValidationCallback validationCallback,
        string strUri, int expiresSeconds)
    {
        m_ClientCertificate = clientCertificate;
        m_ValidationCallback = validationCallback;
        m_strUri = strUri;
        m_Uri = new Uri(strUri);    // Throws a UriFormatException if the URI is not valid
        m_ExpiresSeconds = expiresSeconds;
    }

    /// <summary>
    /// Initiates the connection to the EIDO server. Call this method after hooking the events.
    /// </summary>
    public void Start()
    {
        if (m_Started == true)
            return;

        m_LastConnectRequestTime = DateTime.Now - TimeSpan.FromMilliseconds(ConnectRequestIntervalMs - 1000);
        m_Started = true;
        m_ConnectTask = ClientConnectionTask(m_ConnectCancellationTokenSource.Token);
    }

    /// <summary>
    /// Call this method to end the subscription to the server and close the Web Socket connection to the server when
    /// the application is shutting down.
    /// </summary>
    /// <returns>Returns an awaitable task.</returns>
    public async Task Shutdown()
    {
        if (m_Started == false)
            return;

        if (m_strSubscriptionId != null)
            await SendUnsubscribeRequest();

        m_Started = false;
        if (m_ConnectTask != null)
        {
            m_ConnectCancellationTokenSource.Cancel();
            await m_ConnectTask;
            m_ConnectTask = null;
        }

        if (m_ReceiveTask != null)
        {
            m_ReceiveCancellationTokenSource.Cancel();
            await m_ReceiveTask;
            m_ReceiveTask = null;
        }

        if (m_WebSocket != null)
        {
            try
            {
                if (m_WebSocket.State != WebSocketState.Closed && m_WebSocket.State != WebSocketState.Aborted)
                    m_WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait();
                m_WebSocket.Dispose();
            }
            catch (Exception)
            {
            }

            m_WebSocket = null;
        }
    }

    /// <summary>
    /// This task manages the Web Socket connection and the subscription to the server.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task ClientConnectionTask(CancellationToken cancellationToken)
    {
        while (cancellationToken.IsCancellationRequested == false)
        {
            if (m_WebSocket == null)
            {
                DateTime Now = DateTime.Now;
                if ((Now - m_LastConnectRequestTime).TotalMilliseconds > ConnectRequestIntervalMs)
                {
                    bool Success = await Connect(m_Uri);
                    if (Success == true)
                    {
                        m_ReceiveTask = ReceiveMessageTask(m_ReceiveCancellationTokenSource.Token);
                        CadIfConnectionState?.Invoke(true, m_strUri);
                    }
                    else
                    {
                        if (m_WebSocket != null)
                            m_WebSocket.Dispose();
                        m_WebSocket = null;
                    }
                    m_LastConnectRequestTime = Now;
                }
            }
            else
            {   // Connected to the server
                await Task.Delay(100);

                if (m_WebSocket.State != WebSocketState.Open)
                {
                    await CloseWebSocket();
                    CadIfConnectionState?.Invoke(false, m_strUri);
                    m_strSubscriptionId = null;
                    CadIfSubscriptionState?.Invoke(false, m_strUri);
                    m_ReceiveCancellationTokenSource = new CancellationTokenSource();
                }
                else
                {
                    if (m_strSubscriptionId == null)
                    {   // Not currently subscribed
                        m_FirstSubscribeSemaphore = new SemaphoreSlim(0, 1);
                        await SendSubscribeRequest();
                        bool SubscribeSuccess = false;
                        try
                        {
                            SubscribeSuccess = await m_FirstSubscribeSemaphore.WaitAsync(2000);
                            m_LastSubscribeSentTime = DateTime.Now;
                        }
                        catch (Exception)
                        {
                        }

                        if (string.IsNullOrEmpty(m_strSubscriptionId) == false)
                            CadIfSubscriptionState?.Invoke(true, m_strUri);

                        m_FirstSubscribeSemaphore = null;
                    }
                    else
                    {   // Currently subscribed, see if its time to re-subscribe based on the Expires setting
                        DateTime Now = DateTime.Now;
                        if ((Now - m_LastSubscribeSentTime).TotalSeconds > (m_ExpiresSeconds - 2))
                        {
                            m_LastSubscribeSentTime = Now;
                            await SendSubscribeRequest();
                        }
                    }
                }
            }
        }
    }

    private async Task<bool> Connect(Uri uri)
    {
        m_WebSocket = new ClientWebSocket();
        m_WebSocket.Options.AddSubProtocol(SubProtocol);
        if (m_ClientCertificate != null)
            m_WebSocket.Options.ClientCertificates.Add(m_ClientCertificate);

        if (m_ValidationCallback != null)
            m_WebSocket.Options.RemoteCertificateValidationCallback = m_ValidationCallback;
        else
            m_WebSocket.Options.RemoteCertificateValidationCallback = CertificateValidationCallback!;

        bool Success = false;
        try
        {
            await m_WebSocket.ConnectAsync(uri, CancellationToken.None);
            Success = true;
        }
        catch (WebSocketException)
        {
            Success = false;
        }
        catch (Exception)
        {
            Success = false;
        }

        return Success;
    }

    private async Task CloseWebSocket()
    {
        if (m_WebSocket == null)
            return;

        // First need to top the ReceiveMessageTask
        if (m_ReceiveTask != null)
        {
            m_ReceiveCancellationTokenSource.Cancel();
            await m_ReceiveTask.WaitAsync(CancellationToken.None);
            m_ReceiveTask = null;
        }

        try
        {
            await m_WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            m_WebSocket.Dispose();
            m_WebSocket = null;
        }
    }

    private async Task ReceiveMessageTask(CancellationToken cancellationToken)
    {
        if (m_WebSocket == null)
            return;

        try
        {
            ArraySegment<byte> Buffer = new ArraySegment<byte>(new byte[10000]);
            WebSocketReceiveResult result;
            while (m_WebSocket.State == WebSocketState.Open)
            {
                MemoryStream ms = new MemoryStream();
                do
                {
                    result = await m_WebSocket.ReceiveAsync(Buffer, cancellationToken);
                    ms.Write(Buffer.Array!, Buffer.Offset, result.Count);
                }
                while (result.EndOfMessage == false);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    string strMsg = Encoding.UTF8.GetString(ms.ToArray());
                    await ProcessReceivedMessage(strMsg);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {   // This case will be handled in the ClientConnectionTask
                }
            }
        }
        catch (TaskCanceledException)
        { 
        }
        catch (WebSocketException)
        {   // This exception may occur if the other server's socket is not closed normally
            
        }
        catch (Exception)
        { 
        }
    }

    private async Task SendSubscribeRequest()
    {
        if (m_WebSocket == null)
            return;

        SubscribeRequest Sr = new SubscribeRequest();
        Sr.subscribe.requestId = Guid.NewGuid().ToString();
        Sr.subscribe.requestType = "eido";
        Sr.subscribe.requestSubType = "new";
        Sr.subscribe.subscriptionId = m_strSubscriptionId;
        Sr.subscribe.expires = m_ExpiresSeconds;
        Sr.subscribe.minRate = 20;
        string strSr = EidoHelper.SerializeToString(Sr);
        await SendMessage(strSr);
    }

    private async Task SendUnsubscribeRequest()
    {
        if (m_WebSocket == null || string.IsNullOrEmpty(m_strSubscriptionId) == true) 
            return;

        m_UnsubscribeRequestSemaphore = new SemaphoreSlim(0, 1);
        UnsubscribeRequest Ur = new UnsubscribeRequest();
        Ur.unsubscribe.requestId = Guid.NewGuid().ToString();
        Ur.unsubscribe.subscriptionId = m_strSubscriptionId;
        await SendMessage(EidoHelper.SerializeToString(Ur));
        await m_UnsubscribeRequestSemaphore.WaitAsync(1000);
    }

    private async Task ProcessReceivedMessage(string strMessage)
    {
        if (strMessage.IndexOf("terminate") > -1)
        {
            TerminateRequest Tr = EidoHelper.DeserializeFromString<TerminateRequest>(strMessage);
            TerminateResponse resp = new TerminateResponse();
            TerminateResponseObject Tro = resp.terminateResponse;
            Tro.requestId = Tr.terminate.requestId;
            Tro.subscriptionId = Tr.terminate.subscriptionId;
            string strResp = EidoHelper.SerializeToString(resp);
            await SendMessage(strResp);
            m_strSubscriptionId = null;
            CadIfSubscriptionState?.Invoke(false, m_strUri);
        }
        else if (strMessage.IndexOf("unsubscribeResponse") > -1)
        {
            if (m_UnsubscribeRequestSemaphore != null)
                m_UnsubscribeRequestSemaphore.Release();
        }
        else if (strMessage.IndexOf("subscribeResponse") > -1)
        {
            SubscribeResponse Resp = EidoHelper.DeserializeFromString<SubscribeResponse>(strMessage);
            if (Resp != null)
            {
                m_strSubscriptionId = Resp.subscribeResponse.subscriptionId;
                if (m_FirstSubscribeSemaphore != null)
                    m_FirstSubscribeSemaphore.Release();
            }
        }
        else if (strMessage.IndexOf("event") > -1)
        {
            NotifyEvent Ne = EidoHelper.DeserializeFromString<NotifyEvent>(strMessage);
            if (Ne != null)
            {
                NotifyEventResponse Ner = new NotifyEventResponse();
                Ner.eventResponse.transactionId = Ne.Event.transactionId;
                Ner.eventResponse.statusCode = 200;
                Ner.eventResponse.statusText = "OK";
                string strNer = EidoHelper.SerializeToString(Ner);
                SendMessage(strNer).Wait();

                if (Ne.Event?.notification != null)
                {   // There can be multiple EIDOs in a notification message
                    foreach (EidoType eido in Ne.Event.notification)
                    {
                        EidoReceived?.Invoke(eido, m_strUri);
                    }
                }
            }
        }

    }

    private async Task<bool> SendMessage(string strMessage)
    {
        if (m_WebSocket == null)
            return false;

        bool Success = false;
        if (m_WebSocket.State != WebSocketState.Open)
            return false;

        try
        {
            ReadOnlyMemory<byte> MsgBytes = Encoding.UTF8.GetBytes(strMessage);
            Monitor.Enter(m_SocketLock);
            await m_WebSocket.SendAsync(MsgBytes, WebSocketMessageType.Text, true, CancellationToken.None);
            Success = true;
        }
        catch (Exception)
        {
            Success = false;
        }
        finally
        {
            Monitor.Exit(m_SocketLock);
        }

        return Success;
    }

    // Allows all server certificates.
    private bool CertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, 
        SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

}
