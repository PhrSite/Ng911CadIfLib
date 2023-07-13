/////////////////////////////////////////////////////////////////////////////////////
//  File:   Program.cs                                              13 Jun 23 PHR
/////////////////////////////////////////////////////////////////////////////////////

namespace TestNg911CadIfLib;

using Ng911CadIfLib;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using I3V3.LoggingHelpers;
using HttpUtils;
using Eido;
using Ng911Lib.Utilities;

internal class Program
{
    private const string EidoTestFileName = "SampleCallTransferEido.json";
    private static EidoType m_TestEido;
    private static Ng911CadIfServer m_ng911CadIf;

    // For syncronizing access to the Console from multiple threads.
    private static object m_LockObj = new object();

    static void Main(string[] args)
    {
        X509Certificate2 ServerCert = new X509Certificate2("WebSocketServer.pfx", "Wss12345");
        string strEido = File.ReadAllText(EidoTestFileName);
        m_TestEido = EidoHelper.DeserializeFromString<EidoType>(strEido);
        m_TestEido.lastUpdateTimestamp = TimeUtils.GetCurrentNenaTimestamp();

        SetUpLoggingClient(ServerCert);

        IPAddress IpAddr = IPAddress.IPv6Any;
        IPEndPoint Ipe = new IPEndPoint(IpAddr, 10000);

        m_ng911CadIf = new Ng911CadIfServer(ServerCert, Ipe, "/IncidentData/ent", "/incidents/eidos",
            m_ClientMgr, m_LoggerSettings, null, GetEido);
        // Hook events that an actual application will use.
        m_ng911CadIf.NewSubscription += OnNewSubscription;
        m_ng911CadIf.SubscriptionEnded += OnSubscriptionEnded;

        // Hook events for testing
        m_ng911CadIf.WssConnectionAccepted += OnWssConnectionAccepted;
        m_ng911CadIf.WssConnectionEnded += OnWssConnectionEnded;
        m_ng911CadIf.WssMessageReceived += OnWssMessageReceived;
        m_ng911CadIf.WssMessageSent += OnWssMessageSent;
        m_ng911CadIf.EidoRequestReceived += OnEidoRequestReceived;
        m_ng911CadIf.EidoResponseSent += OnEidoResponseSent;

        m_ng911CadIf.Start();
        Console.Title = "TestNg911CadIfLib";
        Console.WriteLine($"Ng911CadIfServer started, listening on:{Ipe}, press Enter to shut down");
        Console.WriteLine("Type send, then Enter to send an EIDO to all subscribers.");
        bool Done = false;
        while (Done == false)
        {
            string strLine = Console.ReadLine();
            if (string.IsNullOrEmpty(strLine) == true)
                Done = true;
            else
            {
                if (strLine == "send")
                {
                    m_TestEido.lastUpdateTimestamp = TimeUtils.GetCurrentNenaTimestamp();
                    m_ng911CadIf.SendEido(m_TestEido);
                }
            }
        }

        m_ng911CadIf.Shutdown();
        m_ClientMgr.Shutdown();
        Console.WriteLine("Ng911CadIfServer shut down.");
    }

    private static void OnEidoResponseSent(IPEndPoint RemIpe, int ResponseCode, EidoType eido)
    {
        lock (m_LockObj)
        {
            Console.WriteLine($"EIDO Response Sent: {ResponseCode} to {RemIpe} at {TimeUtils.GetCurrentNenaTimestamp()}");
        }
    }

    private static void OnEidoRequestReceived(IPEndPoint RemIpe, string RequestPath, X509Certificate2 
        ClientCertificate)
    {
        lock (m_LockObj)
        {
            Console.WriteLine($"EIDO Request Path: {RequestPath} from {RemIpe} at {TimeUtils.GetCurrentNenaTimestamp()}");
        }
    }

    private static void OnWssMessageSent(IPEndPoint RemIpe, string JsonString)
    {
        lock (m_LockObj)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"WSS Message sent to: {RemIpe} at {TimeUtils.GetCurrentNenaTimestamp()}");
            Console.WriteLine(JsonString);
            Console.ResetColor();
        }
    }

    private static void OnWssMessageReceived(IPEndPoint RemIpe, string JsonString)
    {
        lock (m_LockObj)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"WSS Message received from: {RemIpe} at {TimeUtils.GetCurrentNenaTimestamp()}");
            Console.WriteLine(JsonString);
            Console.ResetColor();
        }
    }

    private static void OnWssConnectionEnded(IPEndPoint RemIpe)
    {
        lock (m_LockObj)
        {
            Console.WriteLine($"WSS connection to: {RemIpe} ended at: {TimeUtils.GetCurrentNenaTimestamp()}");
        }
    }

    private static void OnWssConnectionAccepted(IPEndPoint RemIpe, string SubProtocol, X509Certificate2 
        ClientCertificate)
    {
        lock (m_LockObj)
        {
            Console.WriteLine($"WSS connection accepted from: {RemIpe}, SubProtocol = {SubProtocol} " +
                $"at {TimeUtils.GetCurrentNenaTimestamp()}");
        }
    }

    // Called by the Ng911CadIfServer object when a client requests and EIDO via the EIDO Retrieval
    // Service using an HTTPS GET request. Always returns the same EIDO regardless of the EidoReferenceId
    // requested.
    private static EidoType GetEido(string EidoReferenceId, X509Certificate2 ClientCert, IPEndPoint RemIpe, 
        out int ResponseCode)
    {
        ResponseCode = 200;
        return m_TestEido;
    }

    // For testing the NG9-1-1 event logging functions of the Ng911CadIfServer class.
    private static I3LogEventClientMgr m_ClientMgr;
    private static CadIfLoggingSettings m_LoggerSettings;

    private static void SetUpLoggingClient(X509Certificate2 ClientCert)
    {
        m_LoggerSettings = new CadIfLoggingSettings()
        {
            ElementId = "psap1.simivalley.ca.us",
            AgencyId = "psap1.simivalley.ca.us",
            AgencyAgentId = "Agent1@psap1.simivalley.ca.us",
            AgencyPositionId = "Position1.Agent1@psap1.simivalley.ca.us"
        };

        string strUrl = "https://127.0.0.1:11000";
        m_ClientMgr = new I3LogEventClientMgr();
        I3LogEventClient LoggingClient = new I3LogEventClient(strUrl, "Logger 1", ClientCert);
        m_ClientMgr.AddLoggingClient(LoggingClient);
        m_ClientMgr.LoggingServerStatusChanged += OnLoggingServerStatusChanged;
        m_ClientMgr.LoggingServerError += OnLoggingServerError;
        m_ClientMgr.Start();
    }

    private static void OnLoggingServerStatusChanged(string strLoggerName, bool Responding)
    {
        lock (m_LockObj)
        {
            if (Responding == true)
                Console.ForegroundColor = ConsoleColor.Green;
            else
                Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine($"{strLoggerName} Responding: {Responding}");
            Console.ResetColor();
        }
    }

    private static void OnLoggingServerError(HttpResults Hr, string strLoggerName, string strLogEvent)
    {
        // Do something with the error information such as log it.
    }

    private static void OnNewSubscription(string strSubscriptionId, string strIdType, string strId,
        IPEndPoint RemIpe)
    {
        lock (m_LockObj)
        {
            Console.WriteLine($"New Subsciption: Subscription ID = {strSubscriptionId}, IdType: {strIdType}, " +
                $"ID = {strId}, End Point: {RemIpe}");
        }

        m_ng911CadIf.SendEido(m_TestEido);
    }

    private static void OnSubscriptionEnded(string strSubscriptionId, string strIdType, string strId,
        IPEndPoint RemIpe, string strReason)
    {
        lock (m_LockObj)
        {
            Console.WriteLine($"Subsciption Ended: Subscription ID = {strSubscriptionId}, IdType: {strIdType}, " +
                $"ID = {strId}, End Point: {RemIpe}, Reason = {strReason}");
        }
    }
}