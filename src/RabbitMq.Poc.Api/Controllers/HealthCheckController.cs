using Microsoft.AspNetCore.Mvc;

namespace RabbitMq.Poc.Api.Controllers
{
   [Produces("application/json")]
   [Route("healthcheck")]
    public class HealthCheckController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() =>
            Ok();
    }
}
