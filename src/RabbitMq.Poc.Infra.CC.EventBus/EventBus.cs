using Autofac;
using Newtonsoft.Json;
using RabbitMq.Poc.Infra.CC.EventBus.Interfaces;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IModel = RabbitMQ.Client.IModel;

namespace RabbitMq.Poc.Infra.CC.EventBus
{
    public class EventBus : IEventBus, IDisposable
    {
        private const string AutofacScopeName = "rabbitmqpoc_event_bus";
        private const string Env = "DEV";

        private readonly IEventBusPersistentConnection _persistentConnection;
        private readonly ILifetimeScope _autofac;
        private IModel _consumerChannel;

        private readonly string _serviceName;

        private string _poisonMessageEventName;

        private readonly List<Type> _eventTypes;
        private readonly Dictionary<string, Type> _handlers;

        public EventBus(IEventBusPersistentConnection persistentConnection, ILifetimeScope autofac, string serviceName)
        {
            _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
            _autofac = autofac ?? throw new ArgumentNullException(nameof(autofac));

            _serviceName = !string.IsNullOrWhiteSpace(serviceName) ? serviceName : throw new ArgumentNullException(nameof(serviceName));

            _eventTypes = new List<Type>();
            _handlers = new Dictionary<string, Type>();

            _consumerChannel = CreateConsumerChannel();
        }

        public void Publish(object @event)
        {
            if (!_persistentConnection.IsConnected)
                _persistentConnection.TryConnect();

            using (var channel = _persistentConnection.CreateModel())
            {
                var eventName = @event.GetType().Name;

                var message = JsonConvert.SerializeObject(@event);
                var body = Encoding.UTF8.GetBytes(message);

                var properties = channel.CreateBasicProperties();
                properties.DeliveryMode = 2;

                channel.BasicPublish(
                    exchange: $"{_serviceName}.Output.E.Fanout.{Env}",
                    routingKey: eventName,
                    mandatory: true,
                    basicProperties: properties,
                    body: body);
            }
        }

        public void Subscribe<T, TH>(bool isPoisonMessageEventHandler = false)
        {
            var eventName = typeof(T).Name;

            if (GetEventTypeByName(eventName) != null)
                return;

            _eventTypes.Add(typeof(T));
            _handlers.Add(eventName, typeof(TH));

            if (!isPoisonMessageEventHandler)
            {
                DoQueueBind(eventName);
            }
            else
            {
                _poisonMessageEventName = eventName;
            }
        }

        private void InitializeRabbitMqPocMessageHub(IModel channel)
        {
            channel.ExchangeDeclare(
                exchange: $"RabbitMqPocMessageHub.E.Fanout.{Env}",
                type: "fanout");
        }

        private void InitializeInputExchange(IModel channel)
        {
            channel.ExchangeDeclare(
                exchange: $"{_serviceName}.Input.E.Direct.{Env}",
                type: "direct");

            channel.ExchangeBind(
                destination: $"{_serviceName}.Input.E.Direct.{Env}",
                source: $"RabbitMqPocMessageHub.E.Fanout.{Env}",
                routingKey: "");
        }

        private void InitializeOutputExchange(IModel channel)
        {
            channel.ExchangeDeclare(
                exchange: $"{_serviceName}.Output.E.Fanout.{Env}",
                type: "fanout");

            channel.ExchangeBind(
                destination: $"RabbitMqPocMessageHub.E.Fanout.{Env}",
                source: $"{_serviceName}.Output.E.Fanout.{Env}",
                routingKey: "");
        }

        private void InitializeDeadLetterExchange(IModel channel)
        {
            channel.ExchangeDeclare(
                exchange: $"{_serviceName}.Dlx.E.Fanout.{Env}",
                type: "fanout");
        }

        private void InitializeWorkerQueue(IModel channel)
        {
            var mainQueueArgs = new Dictionary<string, object> {
                { "x-dead-letter-exchange", $"{_serviceName}.Dlx.E.Fanout.{Env}" }
            };

            channel.QueueDeclare(
                queue: $"{_serviceName}.Queue.{Env}",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: mainQueueArgs);
        }

        private void InitializeDeadLetterQueue(IModel channel)
        {
            var dlqQueueArgs = new Dictionary<string, object> {
                { "x-dead-letter-exchange", $"{_serviceName}.Input.E.Direct.{Env}" },
                { "x-message-ttl", (int)TimeSpan.FromMinutes(1).TotalMilliseconds }
            };

            channel.QueueDeclare(
                queue: $"{_serviceName}.Dlq.Queue.{Env}",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: dlqQueueArgs);

            channel.QueueBind(queue: $"{_serviceName}.Dlq.Queue.{Env}",
                              exchange: $"{_serviceName}.Dlx.E.Fanout.{Env}",
                              routingKey: "");
        }

        private long GetMessageRetryCount(IDictionary<string, object> headers)
        {
            if (!(headers?.ContainsKey("x-death") ?? false))
                return 0;

            var xDeathData = (List<object>)headers["x-death"];

            if (xDeathData.Count < 1)
                return 0;

            var xDeathMostRecentMetaData = (Dictionary<string, object>)xDeathData[0];

            if (!xDeathMostRecentMetaData.ContainsKey("count"))
                return 0;

            return (long)xDeathMostRecentMetaData["count"];
        }

        private IModel CreateConsumerChannel()
        {
            if (!_persistentConnection.IsConnected)
                _persistentConnection.TryConnect();

            var channel = _persistentConnection.CreateModel();

            InitializeRabbitMqPocMessageHub(channel);
            InitializeInputExchange(channel);
            InitializeOutputExchange(channel);
            InitializeDeadLetterExchange(channel);

            InitializeWorkerQueue(channel);
            InitializeDeadLetterQueue(channel);

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += async (model, ea) =>
            {
                var eventName = ea.RoutingKey;
                var message = Encoding.UTF8.GetString(ea.Body);

                try
                {
                    await ProcessEvent(eventName, message, GetMessageRetryCount(ea.BasicProperties.Headers));

                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception e)
                {
                    channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            channel.BasicQos(0, 25, false);
            channel.BasicConsume(queue: $"{_serviceName}.Queue.{Env}",
                                 autoAck: false,
                                 consumer: consumer);

            channel.CallbackException += (sender, ea) =>
            {
                _consumerChannel.Dispose();
                _consumerChannel = CreateConsumerChannel();
            };

            return channel;
        }

        private Type GetEventTypeByName(string eventName) =>
            _eventTypes.SingleOrDefault(t => t.Name == eventName);

        private Type GetHandlerTypeByEventName(string eventName) =>
            _handlers[eventName];

        private void DoQueueBind(string eventName)
        {
            if (!_persistentConnection.IsConnected)
                _persistentConnection.TryConnect();

            using (var channel = _persistentConnection.CreateModel())
            {
                channel.QueueBind(queue: $"{_serviceName}.Queue.{Env}",
                                  exchange: $"{_serviceName}.Input.E.Direct.{Env}",
                                  routingKey: eventName);
            }
        }

        private async Task ProcessEvent(string eventName, string message, long retries)
        {
            using (var scope = _autofac.BeginLifetimeScope(AutofacScopeName))
            {
                var isPoisonMessageEvent = false;

                var eventType = GetEventTypeByName(!isPoisonMessageEvent ? eventName : _poisonMessageEventName);
                var integrationEvent = !isPoisonMessageEvent
                    ? JsonConvert.DeserializeObject(message, eventType)
                    : Activator.CreateInstance(eventType, eventName, message, retries);

                var handlerType =
                    GetHandlerTypeByEventName(!isPoisonMessageEvent ? eventName : _poisonMessageEventName);
                var handler = scope.ResolveOptional(handlerType);

                await (Task)handlerType.GetMethod("Handle").Invoke(handler, new[] { integrationEvent });
            }
        }

        public void Dispose()
        {
            _consumerChannel?.Dispose();
        }
    }
}