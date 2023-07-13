using Microsoft.AspNetCore.Mvc;
using Ng911Common;

namespace SimpleLoggingServer.Controllers
{
    [Route("/[controller]")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ApiController]
    public class VersionsController : ControllerBase
    {
        [HttpGet]
        public VersionsArrayType GetVersions()
        {
            //Console.WriteLine($"In GetVersions() at {DateTime.Now.ToString()}");

            VersionsArrayType Vat = new VersionsArrayType();
            Vat.versions = new List<VersionItemType>();
            VersionItemType Vit = new VersionItemType();
            Vit.major = 1;
            Vit.minor = 0;
            Vat.versions.Add(Vit);

            return Vat;
        }
    }
}
