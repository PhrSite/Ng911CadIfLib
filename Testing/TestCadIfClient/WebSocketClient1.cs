/////////////////////////////////////////////////////////////////////////////////////
//  File:   WebSocketClient1.cs                                     20 Jun 23 PHR
/////////////////////////////////////////////////////////////////////////////////////

using System.Net.WebSockets;
using System.Text;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace WebSocketClient;

public class WebSocketClient1
{
    private ClientWebSocket m_WebSocket;
    private CancellationTokenSource m_CancellationTokenSource = new CancellationTokenSource();

    public WebSocketClient1(X509Certificate2 clientCertificate, string strSubProtocol,
        RemoteCertificateValidationCallback validationCallback)
    {
        m_WebSocket = new ClientWebSocket();
        if (string.IsNullOrEmpty(strSubProtocol) == false)
            m_WebSocket.Options.AddSubProtocol(strSubProtocol);

        if (clientCertificate != null)
            m_WebSocket.Options.ClientCertificates.Add(clientCertificate);

        if (validationCallback != null)
            m_WebSocket.Options.RemoteCertificateValidationCallback = validationCallback;
        else
            m_WebSocket.Options.RemoteCertificateValidationCallback = CertificateValidationCallback;
    }

    public async Task<bool> Connect(string strUrl)
    {
        Uri uri = new Uri(strUrl);
        bool Success = false;
        try
        {
            await m_WebSocket.ConnectAsync(uri, CancellationToken.None);
            Success = true;
        }
        catch (Exception)
        {
            Success = false;
        }

        if (Success == true)
        {
            Task Tsk = ReceiveMessage(m_CancellationTokenSource.Token);
        }

        return Success;
    }

    public void Close()
    {
        try
        {
            if (m_WebSocket.State != WebSocketState.Closed && m_WebSocket.State != WebSocketState.Aborted)
                m_WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).
                    Wait();
            m_WebSocket.Dispose();
        }
        catch (Exception)
        {
            //Console.WriteLine(Ex.ToString());
        }
    }

    private async Task ReceiveMessage(CancellationToken cancellationToken)
    {
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
                    ms.Write(Buffer.Array, Buffer.Offset, result.Count);
                }
                while (result.EndOfMessage == false);

                if (result.MessageType != WebSocketMessageType.Close)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    string strMsg = Encoding.UTF8.GetString(ms.ToArray());
                    MessageReceived?.Invoke(strMsg, this);
                }
                else
                {
                    await m_WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, 
                        CancellationToken.None);
                }
            }
        }
        catch (TaskCanceledException)
        { 
        }
        catch (Exception)
        { 
        }
    }

    private object m_SocketLock = new object();

    public async Task<bool> SendMessage(string strMessage)
    {
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

    public event WebSocketMessageReceived MessageReceived;

    private bool CertificateValidationCallback(object sender, X509Certificate certificate,
        X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

}

public delegate void WebSocketMessageReceived(string strMessage, WebSocketClient1 webSocketClient);