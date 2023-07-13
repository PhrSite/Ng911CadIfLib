/////////////////////////////////////////////////////////////////////////////////////
//  File:   Program.cs                                              8 May 23 PHR
/////////////////////////////////////////////////////////////////////////////////////

using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SimpleLoggingServer
{
    /// <summary>
    /// Class that implements a simple NG9-1-1 Log Event Server. This class listenens for NG-9-1-1
    /// Log event requests from one or more NG9-1-1 log event clients and writes the requests
    /// to the console window.
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            X509Certificate2 ServerCert = new X509Certificate2("SimpleLoggingServer.pfx", "SimpleLoggingServer");
            IPAddress ipAddress = IPAddress.Any;
            int Port = 11000;

            // See: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-7.0
            builder.WebHost.UseKestrel(options =>
            {
                //options.Listen(IPAddress.Parse("192.168.1.79"), 10000, listenOptions =>
                options.Listen(ipAddress, Port, listenOptions =>
                {
                    listenOptions.UseHttps(new HttpsConnectionAdapterOptions
                    {
                        ServerCertificate = ServerCert,
                        ClientCertificateMode = ClientCertificateMode.AllowCertificate,
                        ClientCertificateValidation = DisableChannelValidation
                    });

                });
            })
                .ConfigureLogging((context, logging) =>
                {   // Turn off logging because the ASP .NET CORE framework generates a lot of meaningless
                    // log messages.
                    logging.ClearProviders();
                });

            // Add services to the container.
            builder.Services.AddControllers();

            WebApplication app = builder.Build();
            // Configure the HTTP request pipeline.
            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();

            Console.Title = "SimpleLoggingServer";
            Console.WriteLine($"SimpleLoggerServer listening on: {ipAddress}:{Port}");
            Console.WriteLine("Press Ctrl-C to exit");
            app.Run();
        }

        /// <summary>
        /// Disables connection based client certificate validation so the middleware can handle it instead.
        /// Or, custom validation can be handled here.
        /// </summary>
        /// <param name="certificate">The certificate used to authenticate the 
        /// remote party.</param>
        /// <param name="chain">The chain of certificate authorities associated with
        /// the remote certificate.</param>
        /// <param name="errors">One or more errors associated with the remote 
        /// certificate.</param>
        /// <returns>A Boolean value that determines whether the specified 
        /// certificate is accepted for authentication.</returns>
        public static bool DisableChannelValidation(X509Certificate2 certificate, X509Chain chain,
            SslPolicyErrors errors)
        {
            return true;
        }

    }
}