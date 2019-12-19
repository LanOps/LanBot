using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using System.Net.Http;
using System.IO;
using Newtonsoft.Json;
using NadekoBot.Modules.LanOps.DTO;
using Discord;
using NadekoBot.Common.Attributes;
using Discord.WebSocket;
using NadekoBot.Core.Services;
using System.Threading;

namespace NadekoBot.Modules.LanOps
{
    [Group]
    public class Lanops : NadekoTopLevelModule, INService
    {
        static List<WatchedLan> watchedLans = new List<WatchedLan>();
        const string configPath = "data/lanops.json";
        readonly DiscordSocketClient _client;
        readonly Semaphore semaphore = new Semaphore(1,1);

        public Lanops(DiscordSocketClient c)
        {
            _client = c;
            LoadConfig();
            if (semaphore.WaitOne(5000))
            {
                System.Threading.Tasks.Task.Run(WatchForNewAttendees);
            }
        }

        private static void LoadConfig()
        {
            if(File.Exists(configPath))
            {
                var savedConfig = JsonConvert.DeserializeObject<SavedConfig>(File.ReadAllText(configPath));
                watchedLans = savedConfig.WatchedLans;
            }
        }

        private void SaveConfig()
        {
            var configObject = new SavedConfig() { WatchedLans = watchedLans.ToList() };
            File.WriteAllText(configPath, JsonConvert.SerializeObject(configObject));
        }


        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task Readme()
        {
            var channel = (ITextChannel)Context.Channel;
            await channel.SendMessageAsync("See <http://nadekobot.readthedocs.io/en/latest/Commands%20List/>");
        }


        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task Attendees(string lanNumber)
        {
            var channel = (ITextChannel)Context.Channel;

            try
            {
                var users = await GetAttendeeList(lanNumber);

                string msg = $"Attendee list for LanOps {lanNumber}\r\n";
                for (int i = 0; i < users.Count(); i++)
                {
                    if (string.IsNullOrWhiteSpace(users.ElementAt(i).Seat) || string.IsNullOrWhiteSpace(users.ElementAt(i).Seat))
                        msg += $"\r\n{(i + 1)}. {users.ElementAt(i).User.SteamName }";
                    else
                        msg += $"\r\n{(i + 1)}. {users.ElementAt(i).User.SteamName } Seat: {users.ElementAt(i).Seat}";
                }

                await channel.SendMessageAsync($"`{msg}`".Replace("@", "\\@"));
            }
            catch
            {
                await channel.SendMessageAsync("`Failed to get the attendee list :<`");
            }
        }


        [NadekoCommand, Usage, Description, Aliases]
        [RequireBotPermission(GuildPermission.Administrator)]
        [RequireContext(ContextType.Guild)]
        public async Task Watch(string lanNumber)
        {
            var channel = (ITextChannel)Context.Channel;
          

            var isWatching = watchedLans.Where(w => w.ChannelId == channel.Id && Context.Guild.Id == w.ServerId && lanNumber == w.LanId).Count() > 0;

            if (isWatching)
            {
                await channel.SendMessageAsync("`That lan is already been watched!`");
                return;
            }
            else
            {
                watchedLans.Add(new WatchedLan() { ChannelId = channel.Id, ServerId = Context.Guild.Id, LanId = lanNumber });
                await channel.SendMessageAsync($"`I am now watching LanOps {lanNumber} for new attendees.`");
                SaveConfig();
                return;
            }
        }


        [NadekoCommand, Usage, Description, Aliases]
        [RequireBotPermission(GuildPermission.Administrator)]
        [RequireContext(ContextType.Guild)]
        public async Task Stop(string lanNumber)
        {
            var channel = (ITextChannel)Context.Channel;

            var isWatching = watchedLans.Where(w => w.ChannelId == channel.Id && Context.Guild.Id == w.ServerId && lanNumber == w.LanId);

            if (isWatching.Count() > 0)
            {
                watchedLans.Remove(isWatching.First());
                await channel.SendMessageAsync($"`I am no longer watching LanOps {lanNumber} for new attendees in this channel.`");
                SaveConfig();
                return;
            }
            else
            {
                await channel.SendMessageAsync($"`I am already not watching LanOps {lanNumber} for new attendees in this channel.`");
                return;
            }
        }

        private static async Task<List<Participant>> GetAttendeeList(string lanNumber)
        {
            var client = new HttpClient();
            var result = await client.GetAsync($"http://www.lanops.co.uk/api/events/{lanNumber}/participants");

            var content = await result.Content.ReadAsStringAsync();
            File.WriteAllText("c:\\temp\\lan" + Environment.TickCount + ".json", content);

            return JsonConvert.DeserializeObject<List<Participant>>(content).OrderBy(x=>x, new ParticiantComparer()).ToList();
        }


        public class ParticiantComparer : IComparer<Participant>
        {
            public int Compare(Participant x, Participant y)
            {
                var a = 0;
                var b = 0;

                if(!string.IsNullOrEmpty(x.Seat) && x.Seat.Length == 2)
                {
                    a = (int)x.Seat[0] * 1000 + (int)x.Seat[1];
                }
                else
                {
                    a = 1000000000;
                }

                if (!string.IsNullOrEmpty(y.Seat) && y.Seat.Length == 2)
                {
                    b = (int)y.Seat[0] * 1000 + (int)y.Seat[1];
                }
                else
                {
                    b = 1000000000;
                }

                var val =  a.CompareTo(b);
                if (val == 0 && !string.IsNullOrWhiteSpace(x.User.SteamName) && !string.IsNullOrWhiteSpace(y.User.SteamName))
                    return x.User.SteamName.CompareTo(y.User.SteamName);
                return val;
            }
        }

        private async System.Threading.Tasks.Task WatchForNewAttendees()
        {
            Console.WriteLine("Started Checking");
            Dictionary<string, List<Participant>> lastCheck = new Dictionary<string, List<Participant>>();
            while(true)
            {
                try
                {
                    foreach (var watch in watchedLans.ToList())
                    {
                        Console.WriteLine(DateTime.Now.ToString() + " Checked for lan attendees for " + watch.LanId + " on thread " + Thread.CurrentThread.ManagedThreadId);
                        var newAttendeeList = await GetAttendeeList(watch.LanId);
                      //  Console.WriteLine(JsonConvert.SerializeObject(newAttendeeList, Formatting.Indented));
                        if (!lastCheck.Keys.Contains(watch.LanId))
                            lastCheck.Add(watch.LanId, newAttendeeList);
                        else
                        {
                            var lastCheckList = lastCheck[watch.LanId];
                            foreach (var user in newAttendeeList)
                            {
                                var oldUserList = lastCheckList.Where(u => u.Id == user.Id);
                                    if (oldUserList.Count() == 0)
                                    {

                                        var server = _client.GetGuild(watch.ServerId);
                                        if (server == null)
                                            continue;
                                        var channel = (ITextChannel)server?.GetChannel(watch.ChannelId);
                                        if (channel == null)
                                            continue;

                                        if (string.IsNullOrWhiteSpace(user.Seat))
                                            await channel.SendMessageAsync($"New attendee: {user.User.SteamName}.".Replace("@", "\\@"));
                                        else
                                            await channel.SendMessageAsync($"New attendee: {user.User.SteamName} Seat: {user.Seat}.".Replace("@", "\\@"));
                                  
                                    }
                                    else if (oldUserList.First().Seat != user.Seat)
                                    {
                                        var server = _client.GetGuild(watch.ServerId);
                                        if (server == null)
                                            continue;
                                        var channel = (ITextChannel)server?.GetChannel(watch.ChannelId);
                                        if (channel == null)
                                            continue;
                                        if (string.IsNullOrWhiteSpace(oldUserList.First().Seat))
                                            await channel.SendMessageAsync($"Attendee {user.User.SteamName} has taken seat {user.Seat}.".Replace("@", "\\@"));
                                        else
                                            await channel.SendMessageAsync($"Attendee {user.User.SteamName} changed seat from {oldUserList.First().Seat} to {user.Seat}.".Replace("@", "\\@"));
                                   
                                }
                            }

                            lastCheck.Remove(watch.LanId);
                            lastCheck.Add(watch.LanId, newAttendeeList);
                        }
                    }

                    
                }
                catch(Exception e)
                {
                    Console.WriteLine(DateTime.Now.ToString() + "Failed checking for attendees.." + e.Message);
                }
                
                await System.Threading.Tasks.Task.Delay(1000 * 60 * 15);
            }
        }
    }
}
