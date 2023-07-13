/////////////////////////////////////////////////////////////////////////////////////
//  File:   LogEventController.cs                                   8 May 23 PHR
/////////////////////////////////////////////////////////////////////////////////////

using Microsoft.AspNetCore.Mvc;
using System.Net;

using I3V3.LogEvents;
using Ng911Common;
using Ng911Lib.Utilities;
using I3V3.LoggingHelpers;

namespace SimpleLoggingServer.Controllers;

/// <summary>
/// 
/// </summary>
[Route("/[controller]")]
[Produces("application/json")]
[Consumes("application/json")]
[ApiController]
public class LogEventsController : ControllerBase
{
    [HttpPost]
    public void PostLogEvent([FromBody] I3LogEventContent Content)
    {
        if (Content == null || Content.content == null || Content.content.payload == null)
        {
            Response.StatusCode = 400;
            return;
        }

        string strLe = I3Jws.Base64UrlStringToJsonString(Content.content.payload);
        LogEvent Le = JsonHelper.DeserializeFromString<LogEvent>(strLe);
        if (Le == null)
        {
            Response.StatusCode = 400;
            return;
        }

        string strLet = I3LoggingUtils.GetLogEventType(strLe);

        IPAddress ClientIp = Request.HttpContext.Connection.RemoteIpAddress;
        int Port = Request.HttpContext.Connection.RemotePort;
        Console.WriteLine($"Received: {strLet} from {ClientIp}:{Port} at {TimeUtils.GetCurrentNenaTimestamp()}");
        Console.WriteLine(strLe);
        Console.WriteLine();

        Response.StatusCode = 201;
    }
}
