using RabbitMQ.Client;

namespace RabbitMq.Poc.Infra.CC.EventBus.Interfaces
{
    public interface IEventBusPersistentConnection
    {
        bool IsConnected { get; }
        bool TryConnect();
        IModel CreateModel();
    }
}