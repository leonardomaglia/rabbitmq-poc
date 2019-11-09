using RabbitMq.Poc.Domain.Events;
using RabbitMq.Poc.Infra.CC.EventBus.Interfaces;
using System;

namespace RabbitMq.Poc.Domain.EventsHandlers
{
    public class TestMessageEventHandle
    {
        private readonly IEventBus _eventBus;

        public TestMessageEventHandle(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Handle(TestMessageEvent @event)
        {
            try
            {
                Console.WriteLine(@event.Message);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}