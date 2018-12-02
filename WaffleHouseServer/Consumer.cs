using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

namespace WITHBot
{
    public class Consumer
    {
        private readonly string name;
        private readonly IModel channel;
        private readonly IConnection connection;
        private readonly Dictionary<string, Counter> counters = new Dictionary<string, Counter>();
        private readonly IDatabase db;

        public Consumer(string queue, string name)
        {
            //Store name
            this.name = name;
            // Setup Channel
            IConnectionFactory factory = new ConnectionFactory() { HostName = "localhost" };
            connection = factory.CreateConnection();
            channel = connection.CreateModel();
            channel.QueueDeclare(queue: queue, durable: true, exclusive: false, autoDelete: false, arguments: null);
            channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
            // Load Configuration
            string path = Path.Join(Directory.GetCurrentDirectory(), "config.json");
            string jsonText = File.ReadAllText(path);
            Config config = JsonConvert.DeserializeObject<Config>(jsonText);
            // Load counters into dictionary
            config.Counters.ForEach((Counter obj) => counters.Add(obj.TextRegex, obj));
            // Setup Database
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost");
            db = redis.GetDatabase();
            if (db.IsConnected(""))
            {
                foreach (RedisValue value in db.SetMembers("counters"))
                {
                    RedisValue regex = db.HashGet((string)value, "regex");
                    RedisValue variable = db.HashGet((string)value, "variable");

                    counters.Add((string)regex, new Counter
                    {
                        TextRegex = (string)regex,
                        StoredVariable = (string)variable
                    });
                }
            }
            // Setup Consumer
            EventingBasicConsumer consumer = new EventingBasicConsumer(channel);
            consumer.Received += Received;
            channel.BasicConsume(queue: queue, autoAck: false, consumer: consumer);
            Console.WriteLine($"{name} is online with {counters.Count} counters");
        }

        private void Received(object model, BasicDeliverEventArgs ea)
        {
            // Decode Body
            string message = Encoding.UTF8.GetString(ea.Body);
            // Setup Redis batch operation
            IBatch batch = db.CreateBatch();
            // Cycle through counters; testing and adding into the DB
            foreach (KeyValuePair<string, Counter> counter in counters)
            {
                Match match = Regex.Match(message, @counter.Key, RegexOptions.Multiline);
                if (match.Success)
                    batch.StringIncrementAsync(counter.Value.StoredVariable, Regex.Matches(message, @counter.Key).Count);
            }
            // Execute batch job
            batch.Execute();
            // Tell the queue we have finished processing this document
            channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        }

        public void Shutdown()
        {
            // Close the channel
            channel.Close();
            // Close the connection
            connection.Close();
            Console.WriteLine($"{name} is offline");
        }
    }
}
