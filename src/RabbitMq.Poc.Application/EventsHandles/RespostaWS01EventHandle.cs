using System;
using System.Threading.Tasks;
using RabbitMq.Poc.Application.Events;
using RabbitMq.Poc.Infra.CC.EventBus.Interfaces;

namespace RabbitMq.Poc.Application.EventsHandlers
{
    public class RespostaWS01EventHandle
    {
        private readonly IEventBus _eventBus;

        public RespostaWS01EventHandle(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public async Task Handle(RespostaWS01Event @event)
        {
            try
            {
                Console.WriteLine(@event.Mensagem);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}