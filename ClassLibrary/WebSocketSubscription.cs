/////////////////////////////////////////////////////////////////////////////////////
//  File:   WebSocketSubscription.cs                                12 Jun 23 PHR
//
//  Revised: 17 Feb 25 PHR
//              -- Fixed: Was not setting the subscriptionId field of the Subscription
//                 Response message for an existing subscription in
//                 ProcessSubscribeRequest().
/////////////////////////////////////////////////////////////////////////////////////

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Eido;
using I3V3.LoggingHelpers;
using I3V3.LogEvents;
using WebSocketSubNot;
using Ng911CertUtils;
using Ng911Lib.Utilities;

using System.Net;

namespace Ng911CadIfLib;

/// <summary>
/// Class for managing a single subscription for a Web Socket connection 
/// </summary>
internal class WebSocketSubscription
{
    private Ng911CadIfServer m_Ncif;
    private WebSocket m_WebSocket;
    private CadIfLoggingSettings m_LoggingSettings;
    private I3LogEventClientMgr LoggingClientMgr;
    private X509Certificate2 m_ClientCert;
    private IPEndPoint m_RemoteEndPoint;
    private string m_strClientIdType = null;
    private string m_strClientId = null;

    private SemaphoreSlim m_Semaphore = new SemaphoreSlim(0, int.MaxValue);
    private ConcurrentQueue<string> m_RxMessageQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<EidoType> m_EidoQueue = new ConcurrentQueue<EidoType>();

    private TerminateResponse m_TerminateResponse = null;
    private AutoResetEvent m_TerminateResponseEvent = new AutoResetEvent(false);
    private AutoResetEvent m_NotifyResponseEvent = new AutoResetEvent(false);

    /// <summary>
    /// Contains the subscription ID of the current subscription. Is null if there is no subscription yet.
    /// </summary>
    private string m_strSubscriptionId = null;
    /// <summary>
    /// Either "single" or "new"
    /// </summary>
    private string m_strRequestSubType = null;
    /// <summary>
    /// Incident ID if m_strRequestSubType is "single"
    /// </summary>
    private string m_strIncidentId = null;
    /// <summary>
    /// Last time that a subscribe request was received
    /// </summary>
    private DateTime m_SubscriptionStart = DateTime.MinValue;
    /// <summary>
    /// Minimum notify rate in seconds
    /// </summary>
    private int m_MinRateSeconds = 0;
    /// <summary>
    /// Subscription expires time in seconds.
    /// </summary>
    private int m_ExpiresSeconds = 0;
    /// <summary>
    /// Last time that an event notification was sent.
    /// </summary>
    private DateTime m_LastNotifySent = DateTime.MinValue;

    /// <summary>
    /// Used for NG9-1-1 event logging to uniquely identify the Web Socket
    /// </summary>
    private string m_strWebSocketId;

    private const int MinExpiresSeconds = 15;
    private const int MinMinRateSeconds = 5;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="Ws">Web Socket object to send and receive messages with.</param>
    /// <param name="Ncif">Ng911CadIfServer object to use to notify the application when certain events occur.
    /// </param>
    /// <param name="loggingSettings">Settings for Ng9-1-1 event logging. Will be null if loggingClientMgr
    /// is null.</param>
    /// <param name="loggingClientMgr">NG9-1-1 logging client manager to use to send NG9-1-1 log events
    /// to one or more log event servers. May be null is NG9-1-1 logging is not being used.</param>
    /// <param name="clientCert">The client's certificate to use to use to identify the client. May
    /// be null is the client did not provide a certificate.</param>
    /// <param name="RemEndPoint">Remote endpoint of the client.</param>
    public WebSocketSubscription(WebSocket Ws, Ng911CadIfServer Ncif, CadIfLoggingSettings loggingSettings, 
        I3LogEventClientMgr loggingClientMgr, X509Certificate2 clientCert, IPEndPoint RemEndPoint)
    {
        m_Ncif = Ncif;
        m_WebSocket = Ws;
        m_LoggingSettings = loggingSettings;
        LoggingClientMgr = loggingClientMgr;
        m_ClientCert = clientCert;
        m_RemoteEndPoint = RemEndPoint;

        // Get the subscriber identity from the client certificate if available
        if (m_ClientCert != null)
        {
            Ng911SanParams Nsp = CertUtils.GetOtherNameParams(m_ClientCert);
            m_strClientIdType = Nsp?.idType;
            m_strClientId = Nsp?.iD;
        }

        string strShortId = Guid.NewGuid().ToString().Substring(0, 10);
        m_strWebSocketId = $"urn:nena:uid:logEvent:{strShortId}:{GetPeerId()}";
        SendWebSocketEstablisedLogEvent();
    }

    /// <summary>
    /// Sends a single EIDO document to the subscriber. This method queues the EIDO for transmission
    /// and returns immediately.
    /// </summary>
    /// <param name="Eido">EIDO to send</param>
    public void SendEido(EidoType Eido)
    {
        if (m_strSubscriptionId == null)
            return;     // Not subscribed

        if (m_strRequestSubType != null && m_strRequestSubType == "single" && m_strIncidentId != null)
        {   // The subscriber is subscribed to receive EIDOs for a single incident
            string Id = Eido.incidentComponent?.Id;
            if (Id == null)
                return;

            if (m_strIncidentId == Id)
            {
                m_EidoQueue.Enqueue(Eido);
                m_Semaphore.Release();
            }
        }
        else
        {   // The subscriber wants all EIDOs
            m_EidoQueue.Enqueue(Eido);
            m_Semaphore.Release();
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
        if (m_strSubscriptionId == null)
            return;     // Error: no subscriber yet

        if (strSubscriptionId != m_strSubscriptionId)
            return;     // Not for this subscriber

        foreach(EidoType Eido in Eidos)
        {
            SendEido(Eido);
        }
    }

    /// <summary>
    /// Call this method to start managing the subscription.
    /// </summary>
    /// <returns>Returns an Task to await.</returns>
    public async Task ManageConnectionAsync()
    {
        await Task.WhenAll(ReceiveMessagesAsync(), ProcessMessagesAsync());
    }

    private async Task ProcessMessagesAsync()
    {
        string strRxMessage;
        try
        {
            while (m_WebSocket.State == WebSocketState.Open)
            {
                await m_Semaphore.WaitAsync(100);

                while (m_RxMessageQueue.TryDequeue(out strRxMessage) == true)
                {   // Process each received message
                    ProcessRxMessage(strRxMessage);
                }

                ProcessEidos();
                DoTimedEvents();
            } // end while
        }
        catch (Exception)
        {
        }
    }

    private const int EventNotificationTimeoutMs = 5000;

    private void ProcessEidos()
    {
        if (m_EidoQueue.Count == 0)
            return;  // Nothing to send

        List<EidoType> Eidos  = new List<EidoType>();
        EidoType eido;
        while (m_EidoQueue.TryDequeue(out eido) == true)
        {
            Eidos.Add(eido);
        }

        NotifyEvent Ne = new NotifyEvent();
        Ne.Event.subscriptionId = m_strSubscriptionId;
        Ne.Event.transactionId = Guid.NewGuid().ToString();
        Ne.Event.notification = Eidos;
        SendMessage(Ne);
        bool Signaled = m_NotifyResponseEvent.WaitOne(EventNotificationTimeoutMs);
        if (Signaled == true)
        {
            foreach (EidoType SentEido in Eidos)
                SendEidoLogEvent(SentEido);
        }
        else
            SendEidoTransmissionErrorLogEvent(Ne.Event.transactionId);
    }

    private void DoTimedEvents()
    {
        if (string.IsNullOrEmpty(m_strSubscriptionId) == true)
            return;     // No subscription

        DateTime Now = DateTime.Now;
        if (m_ExpiresSeconds != 0)
        {   // Check to see if the subscription has expired
            if ((Now - m_SubscriptionStart).TotalSeconds > m_ExpiresSeconds)
            {   // The subscription expired
                SendTerminateSubscription(m_strSubscriptionId, "Expired");
                // Notify the application that the subscription expired.
                m_Ncif.FireSubscriptionEnded(m_strSubscriptionId, m_strClientIdType, m_strClientId,
                    m_RemoteEndPoint, "Expired");

                m_strSubscriptionId = null;
                Shutdown();
                return;
            }
        }

        if (m_MinRateSeconds != 0)
        {   // Check to see if its time to send an empty notify message
            if ((Now - m_LastNotifySent).TotalSeconds > m_MinRateSeconds)
            {
                SendEmptyNotify();
                m_LastNotifySent = Now;
            }
        }
    }

    private const int MaxMissedMessages = 3;
    private int m_NumMissedMessages = 0;
    private const int EmptyNotifyTimeoutMs = 1000;

    /// <summary>
    /// Sends an empty event notification message to the subscriber and waits for a response. An empty
    /// event notification message contains no EIDO objects.
    /// </summary>
    private void SendEmptyNotify()
    {
        NotifyEvent notifyEvent = new NotifyEvent();
        notifyEvent.Event.subscriptionId = m_strSubscriptionId;
        notifyEvent.Event.transactionId = Guid.NewGuid().ToString();
        SendMessage(notifyEvent);
        bool Signaled = m_NotifyResponseEvent.WaitOne(EmptyNotifyTimeoutMs);
        if (Signaled == false)
        {   // A timeout occurred
            m_NumMissedMessages += 1;
            if (m_NumMissedMessages >= MaxMissedMessages)
            {   // The connection is broken or the subscriber remote endpoint is down
                m_Ncif.FireSubscriptionEnded(m_strSubscriptionId, m_strClientIdType, m_strClientId,
                    m_RemoteEndPoint, "Empty Event Notify Response Timeout");
                m_strSubscriptionId = null;
                Shutdown();
            }
        }
        else
            m_NumMissedMessages = 0;
    }

    private void ProcessRxMessage(string strMsg)
    {
        if (strMsg == null || strMsg.Length == 0)
            return;  

        if (strMsg.IndexOf(SubNotMsgTypes.UnsubscribeRequest) != -1)
            ProcessUnsubscribeRequest(strMsg);
        else if (strMsg.IndexOf(SubNotMsgTypes.SubscribeRequest) != -1)
            ProcessSubscribeRequest(strMsg);
    }

    private void ProcessUnsubscribeRequest(string strMsg)
    {
        UnsubscribeRequest Ur = EidoHelper.DeserializeFromString<UnsubscribeRequest>(strMsg);
        UnsubscribeResponse Resp = new UnsubscribeResponse();
        if (Ur == null)
        {   // Invalid object
            Resp.unsubscribeResponse.statusCode = (int)UnsubscribeResponseCodeEnum.BadRequest;
            Resp.unsubscribeResponse.statusText = "Bad Request";
            // Cannot fill in the other required properties because the original request message
            // could not be de-serialized.
            SendMessage(Resp);
            return;
        }

        Resp.unsubscribeResponse.requestId = Ur.unsubscribe.requestId;
        Resp.unsubscribeResponse.subscriptionId = Ur.unsubscribe.subscriptionId;
        if (Ur.unsubscribe.subscriptionId != m_strSubscriptionId)
        {   // Error: Unknown subscription
            Resp.unsubscribeResponse.statusCode = (int)UnsubscribeResponseCodeEnum.SubscriptionDoesNotExist;
            Resp.unsubscribeResponse.statusText = "Subscription Does Not Exist";
        }
        else
        {
            Resp.unsubscribeResponse.statusCode = (int)UnsubscribeResponseCodeEnum.OK;
            Resp.unsubscribeResponse.statusText= "OK";
        }

        SendMessage(Resp);

        // Notify the application that the subscription has been terminated.
        m_Ncif.FireSubscriptionEnded(m_strSubscriptionId, m_strClientIdType, m_strClientId, m_RemoteEndPoint,
            "Unsubscribed");

        m_strSubscriptionId = null;
        Shutdown();
    }

    private void ProcessSubscribeRequest(string strMsg)
    {
        string strQueryId = Guid.NewGuid().ToString();

        SubscribeRequest Sr = EidoHelper.DeserializeFromString<SubscribeRequest>(strMsg);
        if (Sr == null)
        {   // Invalid object
            SubscribeResponse BadSubResp = new SubscribeResponse();
            BadSubResp.subscribeResponse.statusCode = (int)SubscribeResponseCodeEnum.BadRequest;
            BadSubResp.subscribeResponse.statusText = "Bad Request";
            // Cannot fill in the other required properties because the original request message
            // could not be de-serialized.
            SendMessage(BadSubResp);
            SendSubscriptionRequestedLogEvent(null, strQueryId, 0);
            SendSubscriptionRequestedResponseLogEvent(null, strQueryId, 0, (int)SubscribeResponseCodeEnum.
                BadRequest, "Bad Request");

            return;
        }

        SubscribeResponse Resp = new SubscribeResponse();
        Resp.subscribeResponse.requestId = Sr.subscribe.requestId;
        if (string.IsNullOrEmpty(Sr.subscribe.subscriptionId) == true)
        {   // Its a new subscription
            // Is there an existing subscription?
            if (m_strSubscriptionId != null)
            {   // Already have a subscription so terminate it and use the new one. This is actually
                // an error by the subscriber.
                SendTerminateSubscription(m_strSubscriptionId);
            }

            m_strSubscriptionId = Guid.NewGuid().ToString();
            m_strRequestSubType = Sr.subscribe.requestSubType;
            m_strIncidentId = Sr.subscribe.incidentId;
            m_SubscriptionStart = DateTime.Now;
            m_MinRateSeconds = Sr.subscribe.minRate == null ? 0 : Sr.subscribe.minRate.Value;
            if (m_MinRateSeconds != 0 && m_MinRateSeconds < MinMinRateSeconds)
                m_MinRateSeconds = MinMinRateSeconds;
            m_ExpiresSeconds = Sr.subscribe.expires == null ? 0 : Sr.subscribe.expires.Value;
            if (m_ExpiresSeconds != 0 && m_ExpiresSeconds < MinExpiresSeconds)
                m_ExpiresSeconds = MinExpiresSeconds;

            Resp.subscribeResponse.subscriptionId = m_strSubscriptionId;

            if (m_MinRateSeconds > 0)
                Resp.subscribeResponse.minRate = m_MinRateSeconds;
            if (m_ExpiresSeconds > 0)
                Resp.subscribeResponse.expires = m_ExpiresSeconds;

            Resp.subscribeResponse.statusCode = (int)SubscribeResponseCodeEnum.OK;
            Resp.subscribeResponse.statusText = "OK";
            m_SubscriptionStart = DateTime.Now;
            m_LastNotifySent = DateTime.Now;

            SendSubscriptionRequestedLogEvent(m_strSubscriptionId, strQueryId, m_ExpiresSeconds);
            SendSubscriptionRequestedResponseLogEvent(m_strSubscriptionId, strQueryId, m_ExpiresSeconds, 200, "OK");
            // Notify the application of the new subscription
            m_Ncif.FireNewSubscription(m_strSubscriptionId, m_strClientIdType, m_strClientId, m_RemoteEndPoint);
        }
        else
        {   // Is it an existing subscription?
            if (Sr.subscribe.subscriptionId == m_strSubscriptionId)
            {
                if (m_ExpiresSeconds != 0)
                    Resp.subscribeResponse.expires = m_ExpiresSeconds;
                if (m_MinRateSeconds != 0)
                    Resp.subscribeResponse.minRate = m_MinRateSeconds;
                Resp.subscribeResponse.statusCode = (int)SubscribeResponseCodeEnum.OK;
                Resp.subscribeResponse.subscriptionId = m_strSubscriptionId;    // 17 Feb 25 PHR
                Resp.subscribeResponse.statusText = "OK";
                m_SubscriptionStart = DateTime.Now;
                SendSubscriptionRequestedLogEvent(m_strSubscriptionId, strQueryId, m_ExpiresSeconds);
                SendSubscriptionRequestedResponseLogEvent(m_strSubscriptionId, strQueryId, m_ExpiresSeconds, 200, "OK");
            }
            else
            {   // Error: Unknown subscription ID
                Resp.subscribeResponse.subscriptionId = Sr.subscribe.subscriptionId;
                Resp.subscribeResponse.statusCode = (int)SubscribeResponseCodeEnum.NotFound;
                Resp.subscribeResponse.statusText = "Not Found";
                SendSubscriptionRequestedLogEvent(Sr.subscribe.subscriptionId, strQueryId, m_ExpiresSeconds);
                SendSubscriptionRequestedResponseLogEvent(Sr.subscribe.subscriptionId, strQueryId, 
                    m_ExpiresSeconds, (int)SubscribeResponseCodeEnum.NotFound, "Not Found");
            }
        }

        SendMessage(Resp);
    }

    private const int TerminateRequestTimeoutMs = 1000;

    private void SendTerminateSubscription(string subscriptionId, string strReason = null)
    {
        TerminateRequest Tr = new TerminateRequest();
        Tr.terminate.requestId = Guid.NewGuid().ToString();
        Tr.terminate.subscriptionId = subscriptionId;
        SendMessage(Tr);
        string strQueryId = Guid.NewGuid().ToString();
        SendSubscriptionTerminatedLogEvent(subscriptionId, strReason, strQueryId);
        m_TerminateResponseEvent.WaitOne(TerminateRequestTimeoutMs);
        SendSubscriptionTerminatedResponseLogEvent(subscriptionId, strQueryId, m_TerminateResponse);
    }

    private const int SendMessageTimeoutMs = 5000;

    private void SendMessage(object msgObj)
    {
        string strMsg = EidoHelper.SerializeToString(msgObj);
        byte[] TxMessage = Encoding.UTF8.GetBytes(strMsg);
        m_WebSocket.SendAsync(new ArraySegment<byte>(TxMessage), WebSocketMessageType.Text,
            true, CancellationToken.None).Wait(SendMessageTimeoutMs);
        m_Ncif.FireWssMessageSent(m_RemoteEndPoint, strMsg);
    }

    /// <summary>
    /// Terminates the subscription by sending a terminate request to the client, waits for a response
    /// and then closes the Web Socket connection.
    /// </summary>
    public void Shutdown()
    {
        if (m_WebSocket.State == WebSocketState.Open)
        {
            if (m_strSubscriptionId != null)
            {
                SendTerminateSubscription(m_strSubscriptionId, "Shutting Down");
                m_strSubscriptionId = null;
            }

            m_WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            SendWebSocketTerminatedLogEvent((int)WebSocketCloseStatus.NormalClosure, "NormalClosure",
                "outgoing");
        }
    }

    private const int ReceiveBufferSize = 10000;

    private async Task ReceiveMessagesAsync()
    {
        try
        {
            ArraySegment<byte> Buffer = new ArraySegment<byte>(new byte[ReceiveBufferSize]);
            WebSocketReceiveResult result;
            while (m_WebSocket.State == WebSocketState.Open)
            {
                // Use a MemoryStream so that very large messages can be received.
                MemoryStream ms = new MemoryStream();
                do
                {
                    result = await m_WebSocket.ReceiveAsync(Buffer, CancellationToken.None);
                    ms.Write(Buffer.Array, Buffer.Offset, result.Count);
                }
                while (result.EndOfMessage == false);

                if (result.MessageType != WebSocketMessageType.Close)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    string strMsg = Encoding.UTF8.GetString(ms.ToArray());
                    m_Ncif.FireWssMessageReceived(m_RemoteEndPoint, strMsg);

                    // Signal an event for all responses and queue all requests
                    if (strMsg.IndexOf(SubNotMsgTypes.TerminateResponse) > -1)
                    {
                        m_TerminateResponse = EidoHelper.DeserializeFromString<TerminateResponse>(strMsg);
                        m_TerminateResponseEvent.Set();
                    }
                    else if (strMsg.IndexOf(SubNotMsgTypes.EventResponse) > -1)
                        m_NotifyResponseEvent.Set();
                    else
                        m_RxMessageQueue.Enqueue(strMsg);
                }
                else
                {   // The client closed its end of the Web Socket connection
                    SendWebSocketTerminatedLogEvent((int) result.MessageType, result.MessageType.ToString(),
                        "incoming");
                    await m_WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty,
                        CancellationToken.None);
                    if (m_strSubscriptionId != null)
                        m_Ncif.FireSubscriptionEnded(m_strSubscriptionId, m_strClientIdType, m_strClientId,
                            m_RemoteEndPoint, "Client Connection Closed");
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void SetLogEventType(LogEvent logEvent)
    {
        logEvent.timestamp = TimeUtils.GetCurrentNenaTimestamp();
        logEvent.elementId = m_LoggingSettings.ElementId;
        logEvent.agencyId = m_LoggingSettings.AgencyId;
        logEvent.agencyAgentId = m_LoggingSettings.AgencyAgentId;
        logEvent.agencyPositionId = m_LoggingSettings.AgencyPositionId;
        logEvent.ipAddressPort = m_RemoteEndPoint.ToString();
    }

    private string GetPeerId()
    {
        if (string.IsNullOrEmpty(m_strClientId) == false)
            return m_strClientId;
        else
            return m_RemoteEndPoint.ToString();
    }

    private void SendEidoLogEvent(EidoType eido)
    {
        if (LoggingClientMgr == null)
            return;

        EidoLogEvent Ele = new EidoLogEvent();
        SetLogEventType(Ele);
        Ele.callId = eido.callComponent?[0].Id;
        Ele.incidentId = eido.Id;
        Ele.body = EidoHelper.SerializeToString(eido);
        Ele.direction = "outgoing";

        Ele.peerId = GetPeerId();
        Ele.subscriptionId = m_strSubscriptionId;
        LoggingClientMgr.SendLogEvent(Ele);
    }

    private void SendEidoTransmissionErrorLogEvent(string strTransactionId)
    {
        if (LoggingClientMgr == null)
            return;

        EidoTransmissionErrorLogEvent Ete = new EidoTransmissionErrorLogEvent();
        SetLogEventType(Ete);
        Ete.peerId = GetPeerId();
        Ete.direction = "outgoing";
        Ete.retries = 1;
        Ete.reasonCode = "408";
        Ete.reasonText = "Timeout";
        Ete.transactionId = strTransactionId;
        LoggingClientMgr.SendLogEvent(Ete);
    }

    private void SendSubscriptionRequestedLogEvent(string strSubscriptionId, string strQueryId,
        int Expires)
    {
        if (LoggingClientMgr == null)
            return;

        SubscriptionRequestedLogEvent Sre = new SubscriptionRequestedLogEvent();
        SetLogEventType(Sre);
        Sre.peerId = GetPeerId();
        Sre.direction = "incoming";
        Sre.queryId = strQueryId;
        Sre.expires = Expires;
        Sre.subscriptionId = strSubscriptionId;
        LoggingClientMgr.SendLogEvent(Sre);
    }

    private void SendSubscriptionRequestedResponseLogEvent(string strSubscriptionId, string strQueryId,
        int Expires, int statusCode, string statusText)
    {
        if (LoggingClientMgr == null)
            return;

        SubscriptionRequestedResponseLogEvent Sre = new SubscriptionRequestedResponseLogEvent();
        SetLogEventType(Sre);
        Sre.queryId = strQueryId;
        Sre.direction = "outgoing";
        Sre.subscriptionId= strSubscriptionId;
        Sre.expires= Expires;
        Sre.errorCode = statusCode.ToString();
        Sre.errorText = statusText;
        LoggingClientMgr.SendLogEvent(Sre);
    }

    private void SendSubscriptionTerminatedLogEvent(string strSubscriptionId, string strReason,
        string strQueryId)
    {
        if (LoggingClientMgr == null)
            return;

        SubscriptionTerminatedLogEvent Ste = new SubscriptionTerminatedLogEvent();
        SetLogEventType(Ste);
        Ste.peerId = GetPeerId();
        Ste.direction = "outgoing";
        Ste.queryId = strQueryId;
        Ste.reason = strReason;
        Ste.subscriptionId = strSubscriptionId;
        LoggingClientMgr.SendLogEvent(Ste);
    }

    private void SendSubscriptionTerminatedResponseLogEvent(string strSubscriptionId, string strQueryId,
        TerminateResponse Resp)
    {
        if (LoggingClientMgr == null)
            return;

        SubscriptionTerminatedResponseLogEvent Ter = new SubscriptionTerminatedResponseLogEvent();
        SetLogEventType(Ter);
        Ter.peerId = GetPeerId();
        Ter.direction = "incoming";
        Ter.queryId = strQueryId;
        Ter.statusCode = Resp?.terminateResponse?.statusCode.ToString();
        Ter.statusText = Resp?.terminateResponse?.statusText;
        LoggingClientMgr.SendLogEvent(Ter);
    }

    private void SendWebSocketEstablisedLogEvent()
    {
        if (LoggingClientMgr == null) 
            return;

        WebSocketEstablishedLogEvent Wse = new WebSocketEstablishedLogEvent();
        SetLogEventType(Wse);

        Wse.peerId = GetPeerId();
        Wse.direction = "incoming";
        Wse.status = "200";
        Wse.statusDescription = "OK";
        Wse.webSocketId = m_strWebSocketId;
        LoggingClientMgr.SendLogEvent(Wse);
    }

    private void SendWebSocketTerminatedLogEvent(int closeCode, string strCloseText, string strDirection)
    {
        if (LoggingClientMgr == null)
            return;

        WebSocketTerminatedLogEvent Wst = new WebSocketTerminatedLogEvent();
        SetLogEventType(Wst);
        Wst.peerId = GetPeerId();
        Wst.direction = strDirection;
        Wst.closeCode = closeCode;
        Wst.closeText = strCloseText;
        Wst.webSocketId = m_strWebSocketId;
        LoggingClientMgr.SendLogEvent(Wst);
    }
}
