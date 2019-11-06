namespace RabbitMq.Poc.Infra.CC.EventBus.Interfaces
{
    public interface IEventBus
    {
        void Subscribe<T, TH>(bool isPoisonMessageEventHandler = false);
        void Publish(object @event);
    }
}