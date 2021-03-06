﻿using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using RabbitMQ.Client;
using StackExchange.Redis;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace WITHBot
{
    public class Bot
    {
        #region Redis

        private IDatabase redis;
        private ConnectionMultiplexer _redisConnection;
        private readonly string _redisQueue = "counters";

        private void ConnectRedis()
        {
            Console.WriteLine("Connecting to Redis");
            _redisConnection = ConnectionMultiplexer.Connect(configuration: "localhost");
            redis = _redisConnection.GetDatabase();
        }

        private void DisconnectRedis()
        {
            Console.WriteLine("Disconnecting from Redis");
            _redisConnection.Close();
        }

        #endregion

        #region RabbitMQ

        private IModel rabbit;
        private IConnection _rabbitConnection;
        private IBasicProperties _rabbitProperties;

        private void ConnectRabbit()
        {
            Console.WriteLine("Connecting to RabbitMQ");
            IConnectionFactory rabbitFactory = new ConnectionFactory() { HostName = "localhost" };
            _rabbitConnection = rabbitFactory.CreateConnection();
            rabbit = _rabbitConnection.CreateModel();
            rabbit.QueueDeclare(queue: _redisQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);
            _rabbitProperties = rabbit.CreateBasicProperties();
            _rabbitProperties.Persistent = true;
        }

        private void DisconnectRabbit()
        {
            Console.WriteLine("Disconnecting from RabbitMQ");
            rabbit.Close();
            _rabbitConnection.Close();
        }

        #endregion

        #region Twitch

        private TwitchClient twitch;
        private readonly string _twitchUsername = Environment.GetEnvironmentVariable("TWITCH_USERNAME");
        private readonly string _twitchToken = Environment.GetEnvironmentVariable("TWITCH_TOKEN");
        private readonly string _twitchChannel = Environment.GetEnvironmentVariable("TWITCH_CHANNEL");
        private Hashtable _twitchCommands = new Hashtable();
        private ThreadManager _twitchManager = new ThreadManager();
        private Timer _twitchAutoscaling;

        public void LoadCommands()
        {
            _twitchCommands.Clear();

            if (redis.IsConnected(""))
            {
                foreach (RedisValue value in redis.SetMembers("commands"))
                {
                    RedisValue command = redis.HashGet(value.ToString(), "command");
                    RedisValue message = redis.HashGet(value.ToString(), "message");
                    RedisValue variable = redis.HashGet(value.ToString(), "variable");

                    _twitchCommands.Add(command.ToString().ToLower(), new Command
                    {
                        TextCommand = command.ToString(),
                        TextSay = message.ToString(),
                        StoredVariable = variable.ToString()
                    });
                }
            }

            string path = Path.Join(Directory.GetCurrentDirectory(), "config.json");
            string jsonText = File.ReadAllText(path);
            Config config = JsonConvert.DeserializeObject<Config>(jsonText);
            config.Commands.ForEach((Command obj) => _twitchCommands.Add(obj.TextCommand.ToLower(), obj));

            Console.WriteLine($"Loaded {_twitchCommands.Count} commands");
        }

        public void LoadCounters()
        {
            _twitchManager.ReSpawn();
        }

        private void SetupAutoScaler()
        {
            Console.WriteLine("Spinning up first counter");
            _twitchManager.ManagedSpawn(new ParameterizedThreadDefinition
            {
                name = $"counter:0",
                callback = (object blocker) =>
                {
                    Consumer consumer = new Consumer(queue: _redisQueue, name: $"counter:0");
                    (blocker as ManualResetEvent).WaitOne();
                    consumer.Shutdown();
                }
            });

            _twitchAutoscaling = new Timer(
                callback: delegate
                {
                    double messageCount = rabbit.MessageCount(_redisQueue) / (double)_twitchManager.Count;
                    Console.WriteLine($"There is {rabbit.MessageCount(_redisQueue)} messages in queue");

                    if (messageCount > 5)
                    {
                        int newNumber = _twitchManager.Count;
                        _twitchManager.ManagedSpawn(new ParameterizedThreadDefinition
                        {
                            name = $"counter:{newNumber}",
                            callback = (object blocker) =>
                            {
                                Consumer consumer = new Consumer(queue: _redisQueue, name: $"counter:{newNumber}");
                                (blocker as ManualResetEvent).WaitOne();
                                consumer.Shutdown();
                            }
                        });
                    }
                    else if (_twitchManager.Count > 1 && messageCount <= 1)
                    {
                        _twitchManager.DeSpawn();
                    }
                },
                state: null,
                dueTime: 2500,
                period: 2500
            );
        }

        public void PauseAutoScaler()
        {
            _twitchAutoscaling.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void RestartAutoScaler()
        {
            _twitchAutoscaling.Change(2500, 2500);
        }

        private void ConnectTwitch()
        {
            Console.WriteLine("Connecting to Twitch");
            LoadCommands();
            SetupAutoScaler();
            twitch = new TwitchClient();
            ConnectionCredentials credentials = new ConnectionCredentials(twitchUsername: _twitchUsername, twitchOAuth: _twitchToken);
            twitch.Initialize(credentials: credentials, channel: _twitchChannel);
            twitch.OnMessageReceived += OnMessageReceived;
            twitch.OnChatCommandReceived += OnChatCommandReceived;
            twitch.Connect();
        }

        private void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.Message.StartsWith('!'))
            {
                var chatCommand = new OnChatCommandReceivedArgs() { Command = new ChatCommand(e.ChatMessage) };
                OnChatCommandReceived(sender, chatCommand);
            }


            rabbit.BasicPublish(
                exchange: "",
                routingKey: _redisQueue,
                mandatory: false,
                basicProperties: _rabbitProperties,
                body: Encoding.UTF8.GetBytes(e.ChatMessage.Message)
            );
        }

        private void OnChatCommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            if (redis.SetContains("commandMessages", e.Command.ChatMessage.Id))
                return;

            redis.SetAdd("commandMessages", e.Command.ChatMessage.Id);

            if (!_twitchCommands.ContainsKey(e.Command.CommandText.ToLower()))
                return;

            Command command = _twitchCommands[e.Command.CommandText.ToLower()] as Command;

            if (redis.KeyExists($"command:call:{command.TextCommand}"))
                return;

            string message = command.TextSay;

            if (command.StoredVariable.Length != 0)
            {
                RedisValue data = redis.StringGet(command.StoredVariable);
                message = String.Format(message, String.Format("{0:N0}", Int32.Parse(data)));
            }

            twitch.SendMessage(channel: e.Command.ChatMessage.Channel, message: $"{e.Command.ChatMessage.DisplayName} {message}");
            //rabbit.BasicPublish(
            //    exchange: "",
            //    routingKey: _redisQueue,
            //    mandatory: false,
            //    basicProperties: _rabbitProperties,
            //    body: Encoding.UTF8.GetBytes($"{e.Command.ChatMessage.Message.Substring(1)}")
            //);
            rabbit.BasicPublish(
                exchange: "",
                routingKey: _redisQueue,
                mandatory: false,
                basicProperties: _rabbitProperties,
                body: Encoding.UTF8.GetBytes($"{e.Command.ChatMessage.DisplayName} {message}")
            );
            redis.StringSet($"command:call:{command.TextCommand}", "");
            redis.KeyExpire($"command:call:{command.TextCommand}", DateTime.Now.AddSeconds(5));
        }

        private void DisconnectTwitch()
        {
            Console.WriteLine("Stopping AutoScalar");
            _twitchAutoscaling.Change(Timeout.Infinite, Timeout.Infinite);
            for (int i = 0; i <= _twitchManager.Count - 1; i++)
            {
                _twitchManager.DeSpawn();
            }
            Console.WriteLine("Disconnecting from Twitch");
            twitch.Disconnect();
        }

        #endregion

        public void Connect()
        {
            ConnectRedis();
            ConnectRabbit();
            ConnectTwitch();

            Console.WriteLine("Connections established");
        }

        public void Disconnect()
        {
            DisconnectTwitch();
            DisconnectRabbit();
            DisconnectRedis();

            Console.WriteLine("Connections terminated");
        }

    }
}
