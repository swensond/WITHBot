using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Net;
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

        static void AddEmote(dynamic emote, IDatabase redis)
        {
            string emoteText = emote.emote;
            string emoteNumber = emote.amount;

            if (emoteText.Equals(""))
                return;

            HashEntry[] commandDef = new HashEntry[4];
            commandDef[0] = new HashEntry("command", $"{emoteText.ToUpper()}");
            commandDef[1] = new HashEntry("message", $"{emoteText} has been typed {{0}} times");
            commandDef[2] = new HashEntry("variable", $"count:{emoteText.ToUpper()}");
            string emoteTextRegex = "";

            foreach (char c in emoteText)
            {
                if (!Char.IsLetterOrDigit(c))
                    emoteTextRegex += $"\\{c}";
                else
                    emoteTextRegex += c;
            }

            commandDef[3] = new HashEntry("regex", $"(?>\\b|\\B)({emoteTextRegex})(?>\\b|\\B)");
            redis.HashSet(emoteText, commandDef);
            redis.StringSet($"count:{emoteText.ToUpper()}", emoteNumber);
            redis.SetAdd("commands", emoteText);
            redis.SetAdd("counters", emoteText);
        }

        static void Main(string[] args)
        {
            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs eArgs) =>
            {
                _quitMain.Set();
                eArgs.Cancel = true;
            };

            using (WebClient wc = new WebClient())
            {
                dynamic json = JsonConvert.DeserializeObject<dynamic>(wc.DownloadString("https://api.streamelements.com/kappa/v2/chatstats/giantwaffle/stats"));
                ConnectionMultiplexer _redisConnection = ConnectionMultiplexer.Connect(configuration: "localhost");
                IDatabase redis = _redisConnection.GetDatabase();
                foreach (dynamic emote in json.bttvEmotes)
                {
                    AddEmote(emote, redis);
                }

                foreach (dynamic emote in json.ffzEmotes)
                {
                    AddEmote(emote, redis);
                }
            }

            ThreadManager manager = new ThreadManager();


            Bot bot = new Bot();

            ManualResetEvent robotBlocker = new ManualResetEvent(false);
            ThreadDefinition robotThreadDefinition = new ThreadDefinition
            {
                name = "RobotThread",
                callback = () =>
                {
                    Console.WriteLine("Robot thread started");
                    bot.Connect();
                    robotBlocker.WaitOne();
                    bot.Disconnect();
                    Console.WriteLine("Robot thread terminated");
                }
            };

            manager.Spawn(robotThreadDefinition);

            FileSystemWatcher fileSystemWatcher = new FileSystemWatcher();

            fileSystemWatcher.Changed += (object sender, FileSystemEventArgs e) =>
            {
                if (e.Name.Equals(".#config.json"))
                {
                    Console.WriteLine("Configuration has changed reloading commands and counters");
                    bot.PauseAutoScaler();
                    bot.LoadCommands();
                    bot.LoadCounters();
                    bot.RestartAutoScaler();
                }
            };

            fileSystemWatcher.Path = Directory.GetCurrentDirectory();
            fileSystemWatcher.EnableRaisingEvents = true;

            Console.WriteLine("File watching enabled");

            _quitMain.WaitOne();
            robotBlocker.Set();
            Console.WriteLine("Terminating");
        }

        //     static void OldMain(string[] args)
        //     {
        //         Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs eArgs) =>
        //         {
        //             _quitMain.Set();
        //             eArgs.Cancel = false;
        //         };

        //         _manager = new ThreadManager();


        //         FileSystemWatcher fileSystemWatcher = new FileSystemWatcher();

        //         fileSystemWatcher.Changed += (object sender, FileSystemEventArgs e) =>
        //         {
        //             if (e.Name.Equals(".#config.json"))
        //             {
        //                 timer.Change(Timeout.Infinite, Timeout.Infinite);
        //                 _manager.ReSpawn();
        //                 timer.Change(5000, 500);
        //             }
        //         };

        //         fileSystemWatcher.Path = Directory.GetCurrentDirectory();
        //         fileSystemWatcher.EnableRaisingEvents = true;


        //         for (int i = 0; i < 1; i++)
        //         {
        //             int temp = i;
        //             _manager.Spawn(new ThreadDefinition
        //             {
        //                 name = $"Consumer:{temp}",
        //                 method = delegate (object blocker)
        //                 {
        //                     Consumer consumer = new Consumer(queue: QUEUE, name: $"Consumer:{temp}");
        //                     (blocker as ManualResetEvent).WaitOne();
        //                     consumer.Shutdown();
        //                 },
        //                 arguments = null,
        //             });
        //         }

        //         IConnectionFactory _factory = new ConnectionFactory() { HostName = "localhost" };
        //         IConnection _connection = _factory.CreateConnection();
        //         IModel _channel = _connection.CreateModel();
        //         _channel.QueueDeclare(queue: QUEUE, durable: true, exclusive: false, autoDelete: false, arguments: null);
        //         IBasicProperties properties = _channel.CreateBasicProperties();

        //         string username = Environment.GetEnvironmentVariable("TWITCH_USERNAME");
        //         string token = Environment.GetEnvironmentVariable("TWITCH_TOKEN");
        //         string channel = Environment.GetEnvironmentVariable("TWITCH_CHANNEL");
        //         TwitchClient client = new TwitchClient();
        //         ConnectionCredentials credentials = new ConnectionCredentials(username, token);
        //         client.Initialize(credentials, channel);

        //         client.OnMessageReceived += delegate (object sender, OnMessageReceivedArgs e)
        //         {
        //             _channel.BasicPublish(exchange: "", routingKey: QUEUE, mandatory: false, basicProperties: properties, body: Encoding.UTF8.GetBytes(e.ChatMessage.Message));
        //         };

        //         Timer spawner = new Timer(
        //             callback: delegate
        //             {
        //                 Console.WriteLine($"Manager count {_manager.Count}");
        //                 double messageCount = _channel.MessageCount(QUEUE) / (double)_manager.Count;
        //                 Console.WriteLine($"Checking message count {messageCount}");
        //                 if (messageCount > 5)
        //                 {
        //                     Console.WriteLine("Spawning new consumer");
        //                     int newNumber = _manager.Count;
        //                     _manager.Spawn(new ThreadDefinition
        //                     {
        //                         name = $"Consumer:{newNumber}",
        //                         method = delegate (object blocker)
        //                         {
        //                             Consumer consumer = new Consumer(queue: QUEUE, name: $"Consumer:{newNumber}");
        //                             (blocker as ManualResetEvent).WaitOne();
        //                             consumer.Shutdown();
        //                         },
        //                         arguments = null
        //                     });
        //                 }
        //                 else if (_manager.Count > 1 && messageCount <= 1)
        //                 {
        //                     Console.WriteLine("Removing newest consumer");
        //                     _manager.DeSpawn();
        //                 }
        //             },
        //             state: null,
        //             dueTime: 2500,
        //             period: 2500
        //         );

        //         client.Connect();
        //         _quitMain.WaitOne();
        //         client.Disconnect();
        //         timer.Dispose();
        //         _quitThread.Set();
        //     }
        // }
    }
}
