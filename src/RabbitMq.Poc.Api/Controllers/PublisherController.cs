using Microsoft.AspNetCore.Mvc;
using RabbitMq.Poc.Application;
using System.Threading.Tasks;

namespace RabbitMq.Poc.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PublisherController : ControllerBase
    {
        private readonly EventPublisher _publisherEvent;
            
        public PublisherController(EventPublisher publisherEvent)
        {
            _publisherEvent = publisherEvent;
        }

        [HttpGet]
        [Route("PublishTest")]
        public async Task<IActionResult> Get()
        {
            _publisherEvent.Publish();

            return Ok("Mensagem publicada");
        }
    }
}
