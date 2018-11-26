using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using StackExchange.Redis;
using RabbitMQ.Client;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace WITHBot
{
    class Program
    {
        static readonly ManualResetEvent _quitMain = new ManualResetEvent(false);
        static readonly ManualResetEvent _quitThread = new ManualResetEvent(false);
        static readonly ManualResetEvent _quitCommandThread = new ManualResetEvent(false);
        static readonly string QUEUE = "counters";

        static ThreadManager manager;
        static Timer timer;

        static void Main(string[] args)
        {
            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs eArgs) =>
            {
                _quitMain.Set();
                eArgs.Cancel = false;
            };

            manager = new ThreadManager();

            timer = new Timer(
                callback: new TimerCallback(delegate
                {
                    manager.SanityCheck();
                }),
                state: new { },
                dueTime: 5000,
                period: 5000
            );


            FileSystemWatcher fileSystemWatcher = new FileSystemWatcher();

            fileSystemWatcher.Changed += (object sender, FileSystemEventArgs e) =>
            {
                if (e.Name.Equals(".#config.json"))
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                    manager.ReSpawn();
                    timer.Change(5000, 500);
                }
            };

            fileSystemWatcher.Path = Directory.GetCurrentDirectory();
            fileSystemWatcher.EnableRaisingEvents = true;


            for (int i = 0; i < 1; i++)
            {
                int temp = i;
                manager.Spawn(new ThreadDefinition
                {
                    name = $"Consumer:{temp}",
                    method = delegate (object blocker)
                    {
                        Consumer consumer = new Consumer(queue: QUEUE, name: $"Consumer:{temp}");
                        (blocker as ManualResetEvent).WaitOne();
                        consumer.Shutdown();
                    },
                    arguments = null,
                });
            }

            IConnectionFactory _factory = new ConnectionFactory() { HostName = "localhost" };
            IConnection _connection = _factory.CreateConnection();
            IModel _channel = _connection.CreateModel();
            _channel.QueueDeclare(queue: QUEUE, durable: true, exclusive: false, autoDelete: false, arguments: null);
            IBasicProperties properties = _channel.CreateBasicProperties();

            string username = Environment.GetEnvironmentVariable("TWITCH_USERNAME");
            string token = Environment.GetEnvironmentVariable("TWITCH_TOKEN");
            string channel = Environment.GetEnvironmentVariable("TWITCH_CHANNEL");
            TwitchClient client = new TwitchClient();
            ConnectionCredentials credentials = new ConnectionCredentials(username, token);
            client.Initialize(credentials, channel);

            client.OnMessageReceived += delegate (object sender, OnMessageReceivedArgs e)
            {
                _channel.BasicPublish(exchange: "", routingKey: QUEUE, mandatory: false, basicProperties: properties, body: Encoding.UTF8.GetBytes(e.ChatMessage.Message));
            };

            Timer spawner = new Timer(
                callback: delegate
                {
                    Console.WriteLine($"Manager count {manager.Count}");
                    double messageCount = _channel.MessageCount(QUEUE) / (double)manager.Count;
                    Console.WriteLine($"Checking message count {messageCount}");
                    if (messageCount > 5)
                    {
                        Console.WriteLine("Spawning new consumer");
                        int newNumber = manager.Count;
                        manager.Spawn(new ThreadDefinition
                        {
                            name = $"Consumer:{newNumber}",
                            method = delegate (object blocker)
                            {
                                Consumer consumer = new Consumer(queue: QUEUE, name: $"Consumer:{newNumber}");
                                (blocker as ManualResetEvent).WaitOne();
                                consumer.Shutdown();
                            },
                            arguments = null
                        });
                    }
                    else if (manager.Count > 1 && messageCount <= 1)
                    {
                        Console.WriteLine("Removing newest consumer");
                        manager.DeSpawn();
                    }
                },
                state: null,
                dueTime: 2500,
                period: 2500
            );

            client.Connect();
            _quitMain.WaitOne();
            client.Disconnect();
            timer.Dispose();
            _quitThread.Set();
        }
    }
}
