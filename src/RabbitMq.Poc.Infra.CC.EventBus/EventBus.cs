using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using RabbitMq.Poc.Infra.CC.EventBus.Interfaces;
using Newtonsoft.Json;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using IModel = RabbitMQ.Client.IModel;

namespace RabbitMq.Poc.Infra.CC.EventBus
{
    public class EventBus : IEventBus, IDisposable
    {
        private const string AutofacScopeName = "rabbitmqpoc_event_bus";

        private readonly IEventBusPersistentConnection _persistentConnection;
        private readonly ILifetimeScope _autofac;
        private IModel _consumerChannel;
        private readonly int _retryCount;

        private readonly string _serviceName;
        private readonly string _env;

        private string _poisonMessageEventName;

        private readonly List<Type> _eventTypes;
        private readonly Dictionary<string, Type> _handlers;

        public EventBus(IEventBusPersistentConnection persistentConnection, ILifetimeScope autofac, string serviceName, string env, int retryCount = 5)
        {
            _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
            _autofac = autofac ?? throw new ArgumentNullException(nameof(autofac));
            //_logService = logService;

            _serviceName = !string.IsNullOrWhiteSpace(serviceName) ? serviceName : throw new ArgumentNullException(nameof(serviceName));
            _env = !string.IsNullOrWhiteSpace(env) ? env : throw new ArgumentNullException(nameof(env));

            _eventTypes = new List<Type>();
            _handlers = new Dictionary<string, Type>();

            _retryCount = retryCount;

            _consumerChannel = CreateConsumerChannel();
        }

        public void Publish(object @event)
        {
            if (!_persistentConnection.IsConnected)
                _persistentConnection.TryConnect();

            var connectionPolicy = Policy
                .Handle<SocketException>()
                .Or<BrokerUnreachableException>()
                .WaitAndRetry(_retryCount,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2,
                        retryAttempt)));

            using (var channel = _persistentConnection.CreateModel())
            {
                var eventName = @event.GetType().Name;

                var message = JsonConvert.SerializeObject(@event);
                var body = Encoding.UTF8.GetBytes(message);

                connectionPolicy.Execute(() =>
                {
                    var properties = channel.CreateBasicProperties();
                    properties.DeliveryMode = 2;

                    channel.BasicPublish(
                        exchange: $"{_serviceName}.Output.E.Fanout.{_env}",
                        routingKey: eventName,
                        mandatory: true,
                        basicProperties: properties,
                        body: body);
                });
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
                exchange: $"RabbitMqPocMessageHub.E.Fanout.{_env}",
                type: "fanout");
        }

        private void InitializeInputExchange(IModel channel)
        {
            channel.ExchangeDeclare(
                exchange: $"{_serviceName}.Input.E.Direct.{_env}",
                type: "direct");

            channel.ExchangeBind(
                destination: $"{_serviceName}.Input.E.Direct.{_env}",
                source: $"RabbitMqPocMessageHub.E.Fanout.{_env}",
                routingKey: "");
        }

        private void InitializeOutputExchange(IModel channel)
        {
            channel.ExchangeDeclare(
                exchange: $"{_serviceName}.Output.E.Fanout.{_env}",
                type: "fanout");

            channel.ExchangeBind(
                destination: $"RabbitMqPocMessageHub.E.Fanout.{_env}",
                source: $"{_serviceName}.Output.E.Fanout.{_env}",
                routingKey: "");
        }

        private void InitializeDeadLetterExchange(IModel channel)
        {
            channel.ExchangeDeclare(
                exchange: $"{_serviceName}.Dlx.E.Fanout.{_env}",
                type: "fanout");
        }

        private void InitializeWorkerQueue(IModel channel)
        {
            var mainQueueArgs = new Dictionary<string, object> {
                { "x-dead-letter-exchange", $"{_serviceName}.Dlx.E.Fanout.{_env}" }
            };

            channel.QueueDeclare(
                queue: $"{_serviceName}.Queue.{_env}",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: mainQueueArgs);
        }

        private void InitializeDeadLetterQueue(IModel channel)
        {
            var dlqQueueArgs = new Dictionary<string, object> {
                { "x-dead-letter-exchange", $"{_serviceName}.Input.E.Direct.{_env}" },
                { "x-message-ttl", (int)TimeSpan.FromMinutes(1).TotalMilliseconds }
            };

            channel.QueueDeclare(
                queue: $"{_serviceName}.Dlq.Queue.{_env}",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: dlqQueueArgs);

            channel.QueueBind(queue: $"{_serviceName}.Dlq.Queue.{_env}",
                              exchange: $"{_serviceName}.Dlx.E.Fanout.{_env}",
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
                    //_logService.Log($"{e.Message} - {e.StackTrace}");
                    channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            channel.BasicQos(0, 25, false);
            channel.BasicConsume(queue: $"{_serviceName}.Queue.{_env}",
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
                channel.QueueBind(queue: $"{_serviceName}.Queue.{_env}",
                                  exchange: $"{_serviceName}.Input.E.Direct.{_env}",
                                  routingKey: eventName);
            }
        }

        private async Task ProcessEvent(string eventName, string message, long retries)
        {
            using (var scope = _autofac.BeginLifetimeScope(AutofacScopeName))
            {
                //var isPoisonMessageEvent = retries > _retryCount; 
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