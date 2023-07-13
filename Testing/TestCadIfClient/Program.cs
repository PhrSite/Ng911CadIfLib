/////////////////////////////////////////////////////////////////////////////////////
//  File:   Program.cs                                              20 Jun 23 PHR
/////////////////////////////////////////////////////////////////////////////////////

using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using Eido;
using WebSocketSubNot;
using HttpUtils;
using Ng911Lib.Utilities;

namespace WebSocketClient;

public class Program
{
    private const string strWssUrl = "wss://[::1]:10000/IncidentData/ent";
    private const string strEidoUrl = "https://[::1]:10000/incidents/eidos/123";

    private static WebSocketClient1 Cws;
    private const int ExpiresSeconds = 60;
    private static X509Certificate2 ClientCert;

    static async Task Main(string[] args)
    {
        ClientCert = new X509Certificate2("WebSocketClient.pfx", "Wss12345");

        Console.Title = "TestCadIfClient";
        //Cws = new WebSocketClient1(ClientCert, "emergency-ent1.0", null);
        Cws = new WebSocketClient1(null, "emergency-ent1.0", null);
        Cws.MessageReceived += OnMessageReceived;
        bool Success = await Cws.Connect(strWssUrl);
        if (Success == false)
        {
            Console.WriteLine("Could not connect to the server");
            return;
        }

        Console.WriteLine("Connected to the server.");
        Console.WriteLine("Press Enter to end the program or type one of the following commands followed by Enter.");
        Console.WriteLine("\tget -- To send an HTTPS GET request to the server.");
        Console.WriteLine("\tstatus -- To display the subscription status");
        Console.WriteLine("\tunsubscribe -- To unsubscribe");

        // Send a SubscribeRequest to the notifier
        SubscribeRequest Sr = new SubscribeRequest();
        Sr.subscribe.requestId = Guid.NewGuid().ToString();
        Sr.subscribe.requestType = "eido";
        Sr.subscribe.requestSubType = "new";
        Sr.subscribe.expires = ExpiresSeconds;
        Sr.subscribe.minRate = 20;
        string strSr = EidoHelper.SerializeToString(Sr);
        await Cws.SendMessage(strSr);

        bool Done = false;
        string strMsg;

        while (Done == false)
        {
            strMsg = Console.ReadLine();
            if (string.IsNullOrEmpty(strMsg) == true)
                Done = true;
            else
            {
                if (strMsg == "get")
                    await GetEido();
                else if (strMsg == "unsubscribe")
                {
                    if (string.IsNullOrEmpty(m_strSubscriptionId) == true)
                        Console.WriteLine("Not subscribed");
                    else
                    {
                        SendUnsubscribe();
                        Console.WriteLine("Unsubscribed");
                    }
                }
                else if (strMsg == "status")
                {
                    if (m_strSubscriptionId == null)
                        Console.WriteLine("Not subscribed");
                    else
                        Console.WriteLine("Subscribed");
                }
                else
                    Console.WriteLine("Unknown command");
            }
        }

        if (RenewTask != null)
            m_CancellationSource.Cancel();

        Cws.Close();
    }

    private static void SendUnsubscribe()
    {
        UnsubscribeRequest Ur = new UnsubscribeRequest();
        Ur.unsubscribe.requestId = Guid.NewGuid().ToString();
        Ur.unsubscribe.subscriptionId = m_strSubscriptionId;
        Cws.SendMessage(EidoHelper.SerializeToString(Ur)).Wait();
    }

    private static async Task GetEido()
    {
        AsyncHttpRequestor Ahr = new AsyncHttpRequestor(ClientCert, 2000);

        HttpResults Hr = await Ahr.DoRequestAsync(HttpMethodEnum.GET, strEidoUrl, null, null, true);
        if (Hr.StatusCode == HttpStatusCode.OK)
        {
            Console.WriteLine($"EIDO received, Content-Type = {Hr.ContentType}");
            Console.WriteLine(Hr.Body);
        }
        else
            Console.WriteLine($"HTTPS GET request failed. Status Code = {Hr.StatusCode.ToString()}");
    }

    private static Task RenewTask = null;
    private static string m_strSubscriptionId = null;
    private static CancellationTokenSource m_CancellationSource = new CancellationTokenSource();

    private static void OnMessageReceived(string strMessage, WebSocketClient1 client)
    {
        if (strMessage.IndexOf("terminate") > -1)
        {
            TerminateRequest Tr = EidoHelper.DeserializeFromString<TerminateRequest>(strMessage);
            TerminateResponse resp = new TerminateResponse();
            TerminateResponseObject Tro = resp.terminateResponse;
            Tro.requestId = Tr.terminate.requestId;
            Tro.subscriptionId = Tr.terminate.subscriptionId;
            string strResp = EidoHelper.SerializeToString(resp);
            Cws.SendMessage(strResp).Wait();
            StopSubscription();
        }
        else if (strMessage.IndexOf("unsubscribeResponse") > -1)
        {
            StopSubscription();
        }
        else if (strMessage.IndexOf("subscribeResponse") > -1)
        {
            SubscribeResponse Resp = EidoHelper.DeserializeFromString<SubscribeResponse>(strMessage);
            if (Resp != null)
            {
                if (m_strSubscriptionId == null)
                {
                    m_strSubscriptionId = Resp.subscribeResponse.subscriptionId;
                    RenewTask = SubscriptionRenewTask(m_CancellationSource.Token);
                }
                
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
                Cws.SendMessage(strNer).Wait();

                if (Ne.Event?.notification != null)
                {
                    foreach (EidoType eido in Ne.Event.notification)
                    {
                        Console.WriteLine($"EIDO received at: {TimeUtils.GetCurrentNenaTimestamp()}");
                        Console.WriteLine(EidoHelper.SerializeToString(eido));
                    }
                }
            }
        }
    }

    private static void StopSubscription()
    {
        if (RenewTask != null)
        {
            m_CancellationSource.Cancel();
            RenewTask = null;
        }

        m_strSubscriptionId = null;
    }

    private static async Task SubscriptionRenewTask(CancellationToken token)
    {
        int SendSubscribeIntervalMs = (ExpiresSeconds - 2) * 1000;
        try
        {
            while (true)
            {
                await Task.Delay(SendSubscribeIntervalMs, token);
                SubscribeRequest Sr = new SubscribeRequest();
                Sr.subscribe.subscriptionId = m_strSubscriptionId;
                Sr.subscribe.requestId = Guid.NewGuid().ToString();
                Sr.subscribe.requestType = "eido";
                Sr.subscribe.expires = ExpiresSeconds;
                Sr.subscribe.minRate = 20;
                string strSr = EidoHelper.SerializeToString(Sr);
                await Cws.SendMessage(strSr);
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    public static bool RemoteCertificateValidationCallback(object sender, X509Certificate certificate, 
        X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }
}