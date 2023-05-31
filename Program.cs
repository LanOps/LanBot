using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using LanBot;
using LanBot.DTO;

string? token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
ulong.TryParse(Environment.GetEnvironmentVariable("DISCORD_GUILD"), out ulong guildId);

if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("Please specify a token in the DISCORD_TOKEN environment variable.");
    Environment.Exit(1);
    return;
}

if (guildId == 0)
{
    Console.WriteLine("Please specify a token in the DISCORD_GUILD environment variable.");
    Environment.Exit(1);
    return;
}

var config = new DiscordConfiguration()
{
    Token = token,
    TokenType = TokenType.Bot,
    Intents = DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents
};

DiscordClient client = new(config);
var channelMatcher = new Regex("(?:-)(\\d+)", RegexOptions.Compiled);

client.MessageCreated += async (client, eventArgs) =>
{
    var command = "!attendees";
    if (eventArgs.Message.Content.ToLowerInvariant().StartsWith(command))
    {
        try
        {
            string id = eventArgs.Message.Content.Trim().Length > command.Length?eventArgs.Message.Content.Substring(command.Length).Trim():eventArgs.Channel.Name;

            var users = await LanOps.GetAttendeeList(id);
            string msg = $"Attendee list for LanOps {id}\r\n";
            for (int i = 0; i < users.Count(); i++)
            {
                if (string.IsNullOrWhiteSpace(users[i].Seat) || string.IsNullOrWhiteSpace(users[i].Seat))
                    msg += $"\r\n{(i + 1)}. {users[i].Username}";
                else
                    msg += $"\r\n{(i + 1)}. {users[i].Username} Seat: {users[i].Seat}";
            }

            await eventArgs.Message.RespondAsync($"`{msg}`".Replace("@", "\\@"));
        }
        catch (Exception e)
        {
            await eventArgs.Message.RespondAsync("`Failed to get the attendee list :<`");
            Console.WriteLine(e);
        }
    }

};

DiscordActivity status = new("with fire", ActivityType.Playing);

// Now we connect and log in.
await client.ConnectAsync(status, UserStatus.Online);


Dictionary<string, List<Participant>> lastCheck = new();

while (true)
{
    var changed = false;
    var guild = await client.GetGuildAsync(guildId);
    var channels = await guild.GetChannelsAsync();

    var eventChannels = channels.Where(c => c.Name == "Events").FirstOrDefault()?.Children;
    if (eventChannels != null)
    {
        foreach (var channel in eventChannels)
        {
            var matchChannel = channelMatcher.Match(channel.Name);
            if (matchChannel.Success)
            {
                Console.WriteLine($"Checking {channel.Name}");
                var newAttendeeList = await LanOps.GetAttendeeList(channel.Name);
                if (!lastCheck.Keys.Contains(channel.Name))
                    lastCheck.Add(channel.Name, newAttendeeList);
                else
                {
                    var lastCheckList = lastCheck[channel.Name];
                    foreach (var userGroup in newAttendeeList.GroupBy(u=>u.Username))
                    {
                        var oldSeats = string.Join(", ",lastCheckList.Where(u => u.Username == userGroup.First().Username).Select(u => u.Seat).Where(s => !string.IsNullOrEmpty(s)).OrderBy(s=>s));
                        var newSeats = string.Join(", ", userGroup.Select(u => u.Seat).Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s));
                        if (!lastCheckList.Where(u=>u.Username == userGroup.First().Username).Any())
                        {
                            foreach (var user in userGroup)
                            {
                                if (string.IsNullOrWhiteSpace(newSeats))
                                    await channel.SendMessageAsync($"New attendee: {user.Username}.".Replace("@", "\\@"));
                                else
                                    await channel.SendMessageAsync($"New attendee: {user.Username} Seat{(newSeats.Length > 0 ? "s" : "")}: {newSeats}.".Replace("@", "\\@"));
                                changed = true;
                            }
                        }
                        else if (newSeats != oldSeats)
                        {

                            if (string.IsNullOrWhiteSpace(oldSeats))
                                await channel.SendMessageAsync($"Attendee {userGroup.First().Username} has taken seat{(newSeats.Length>0?"s":"")} {newSeats}.".Replace("@", "\\@"));
                            else 
                                await channel.SendMessageAsync($"Attendee {userGroup.First().Username} changed seats from {oldSeats} to {newSeats}.".Replace("@", "\\@"));
                            changed = true;
                        }
                    }

                    lastCheck.Remove(channel.Name);
                    lastCheck.Add(channel.Name, newAttendeeList);
                }
            }
        }

    }

   await Task.Delay(1000 * 60 * (changed ? 1 : 5)); // 1 or 5 Minutes
}




