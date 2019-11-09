using RabbitMq.Poc.Domain.Events;
using RabbitMq.Poc.Infra.CC.EventBus.Interfaces;

namespace RabbitMq.Poc.Application
{
    public class EventPublisher
    {
        private readonly IEventBus _eventBus;

        public EventPublisher(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Publish()
        {
            var testMessage = new TestMessageEvent
            {
                Message = "Publish test"
            };

            _eventBus.Publish(testMessage);
        } 
    }
}
