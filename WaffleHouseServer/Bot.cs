// using System;
// using System.Collections;
// using System.Collections.Generic;
// using System.Text.RegularExpressions;
// using StackExchange.Redis;
// using TwitchLib.Client;
// using TwitchLib.Client.Events;
// using TwitchLib.Client.Models;

// namespace WaffleHouseServer
// {

//     public class Bot
//     {
//         private readonly TwitchClient client = new TwitchClient();
//         private ConnectionMultiplexer redis;

//         public IDatabase db;

//         public void SetupDB()
//         {
//             redis = ConnectionMultiplexer.Connect("localhost");
//             db = redis.GetDatabase();
//         }

//         public void SetupTwitch()
//         {
//             string username = Environment.GetEnvironmentVariable("TWITCH_USERNAME");
//             string token = Environment.GetEnvironmentVariable("TWITCH_TOKEN");
//             string channel = Environment.GetEnvironmentVariable("TWITCH_CHANNEL");
//             ConnectionCredentials credentials = new ConnectionCredentials(username, token);
//             client.Initialize(credentials, channel);
//             client.AddChatCommandIdentifier('!');
//         }

//         public void SetupCounter(Counter data)
//         {
//             client.OnMessageReceived += delegate (object sender, OnMessageReceivedArgs e) {
//                 if (e.ChatMessage.Username.Equals(Environment.GetEnvironmentVariable("TWITCH_USERNAME")))
//                     return;

//                 Match match = Regex.Match(e.ChatMessage.Message, @data.TextRegex, RegexOptions.Multiline);
//                 if (match.Success)
//                     this.db.StringIncrement(data.StoredVariable, Regex.Matches(e.ChatMessage.Message, @data.TextRegex).Count);
//             };
//         }

//         public void SetupCommands(Hashtable commands)
//         {
//             RedisValue[] DBCommands = this.db.ListRange("commands", 0, -1);
//             foreach (string dbcommand in DBCommands.ToStringArray())
//             {
//                 if (commands.ContainsKey(dbcommand.ToLower()))
//                     continue;

//                 commands.Add(dbcommand.ToLower(), new Command
//                 {
//                     TextCommand = dbcommand,
//                     TextSay = this.db.HashGet(dbcommand, "message"),
//                     StoredVariable = this.db.HashGet(dbcommand, "variable")
//                 });
//             }

//             client.OnChatCommandReceived += delegate (object sender, OnChatCommandReceivedArgs e)
//             {
//                 if (commands.ContainsKey(e.Command.CommandText.ToLower()))
//                 {
//                     Command data = commands[e.Command.CommandText.ToLower()] as Command;
//                     string key = $"command:timer:{data.TextCommand}";
//                     if (!this.db.KeyExists(key))
//                     {
//                         string message = !data.StoredVariable.Equals("") ? $"@{e.Command.ChatMessage.Username} {String.Format(data.TextSay, db.StringGet(data.StoredVariable))}" : $"{data.TextSay}";
//                         client.SendMessage(e.Command.ChatMessage.Channel, message);

//                         this.db.StringSet(key, "");
//                         this.db.KeyExpire(key, DateTime.Now.AddSeconds(15));
//                     }
//                 }

//                 if (!e.Command.ChatMessage.IsModerator || !e.Command.ChatMessage.IsBroadcaster)
//                     return;

//                 if(e.Command.CommandText.Equals("AddCommand"))
//                 {
//                     Match match = Regex.Match(e.Command.ChatMessage.Message, @"command:(?<command>[^\s]+)|message:""(?<message>.*?)""|variable:(?<variable>[^\s]+)");
//                     string command = match.Groups["command"].Value;
//                     string message = match.Groups["message"].Value;
//                     string variable = match.Groups["variable"].Value;
//                     this.db.ListRightPush("commands", command);
//                     this.db.HashSet(command, "message", message);
//                     this.db.HashSet(command, "variable", variable);
//                     client.SendMessage(e.Command.ChatMessage.Channel, $"{command} has been created.");
//                     Watcher.RestartCommandThread();
//                     return;
//                 }

//                 if(e.Command.CommandText.Equals("AddCounter"))
//                 {
//                     Match counterMatch = Regex.Match(e.Command.ChatMessage.Message, @"text:""(?<text>.*?)""|variable:(?<variable>[^\s]+)");
//                     string text = counterMatch.Groups["text"].Value;
//                     this.db.HashSet("counters", text, $"count:{text}");
//                     Watcher.Spawn(new ThreadSetup
//                     {
//                         name = $"counter:count:{text}",
//                         arguments = null,
//                         method = () =>
//                         {
//                             Bot bot = new Bot();
//                             bot.SetupDB();
//                             bot.SetupTwitch();
//                             bot.SetupCounter(new Counter
//                             {
//                                 TextRegex = $"\\b({text})\\b",
//                                 StoredVariable = $"count:{text}"
//                             });
//                             bot.Connect();
//                             Watcher.ThreadExit.WaitOne();
//                         }
//                     });
//                     client.SendMessage(e.Command.ChatMessage.Channel, $"Counter has been created.");
//                     return;
//                 }
//             };
//         }

//         public void Connect()
//         {
//             client.Connect();
//         }
//     }
// }
