using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pastel;
using System.Drawing;
using Microsoft.EntityFrameworkCore;
using static Program;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Diagnostics;
using System.Reactive.Concurrency;


// POCO class for server information
public class BotConfiguration
{
    public string? BotToken { get; set; }
    public ulong UserId { get; set; }
}

public class ServerInfo
{
    public ulong GuildId { get; set; }
    public string? GuildName { get; set; }
}

// POCO class for user information
public class UserInfo
{
    public ulong UserId { get; set; }
    public string? UserName { get; set; }
}

// POCO class for data to be stored in SQLite database
public class DatabaseData
{
    public int Id { get; set; } 
    public ulong GuildId { get; set; }
    public string? GuildName { get; set; }
    public ulong UserId { get; set; }
    public string? UserName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; } //Addresses that are posted 
    public string? Url { get; set; } // URL posted in Message
    public string? MessageContent { get; set; } //Content of the Message
    public int MessageCount { get; set; }  // If you are tracking how many messages a user has sent
    public ulong MessageId { get; set; }  // Unique identifier for each message
    public string? ContentType { get; set; }  // The type of content (phone, address, url, etc.)
    public string? Content { get; set; }  // The actual content (the phone number, address, url string, etc.)
   // public string? Notes { get; set; }   // Allow the user to make notes in the database Add these notes later but this causes errors
}

class Program
{
    private DiscordShardedClient? _client;
    private CommandService? _commands;
    private IServiceProvider? _services;
    private string? botToken;
    private ulong yourUserId;
    private List<SocketGuild>? guilds;
    private IConfiguration? _configuration;
    private readonly ulong _guildId;
    private readonly HashSet<ulong> _registeredGuilds = new HashSet<ulong>();

    // Define a list to store phone number information
    private readonly List<DatabaseData> databaseData = new List<DatabaseData>();

    static async Task Main(string[] args)
    {
        Console.Title = "Stealing Numbers";

        // Ensure buffer width is at least as wide as the console window
        int bufferWidth = Math.Max(Console.WindowWidth, 120);
        int bufferHeight = Math.Min(1000, short.MaxValue - 1); // Ensure buffer height is within valid bounds

        Console.SetBufferSize(bufferWidth, bufferHeight);

        // Display "colorize me" text with different colors for each part
        Console.Write("colorize ".Pastel("#1E90FF"));
        Console.Write("me".Pastel("#FF4500"));
        Console.WriteLine(); // Move to the next line

        await new Program().RunBotAsync();
    }

    public async Task RunBotAsync()
    {
        try
        {
            // Specify the full path to the secrets.json file in the User Secrets directory
            var secretsJsonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", "092350d0-7f81-4fbe-9ec0-987c097f6b7e", "secrets.json");

            // Load configuration from secrets.json
            //Console.WriteLine("Secrets JSON Path: " + secretsJsonPath);
            _configuration = new ConfigurationBuilder()
                .AddJsonFile(secretsJsonPath)
                .Build();

            // Retrieve bot token from configuration
            botToken = _configuration["BotConfiguration:BotToken"]; // Updated key path
            if (string.IsNullOrEmpty(botToken))
            {
                Console.WriteLine("Bot token not found in configuration.");
                return;
            }

            // Retrieve and validate user ID from configuration
            if (!ulong.TryParse(_configuration["BotConfiguration:UserId"], out yourUserId)) // Updated key path
            {
                Console.WriteLine("Invalid User ID in configuration.");
                return;
            }

            // Initialize the DiscordShardedClient with configuration settings
            _client = new DiscordShardedClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                TotalShards = 1, // Set the number of shards appropriately
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages // Add other necessary intents
            });

            _commands = new CommandService();

            // Create a service collection and configure services
            var serviceProvider = new ServiceCollection()
                .AddSingleton(_client) // Add your Discord client
                .AddSingleton(_commands) // Add your CommandService
                .AddOptions() // Add options
                .Configure<BotConfiguration>(_configuration.GetSection("BotConfiguration")) // Replace "BotConfiguration" with your config section
                .BuildServiceProvider();

            // Assign the service provider to the _services field
            _services = serviceProvider;

            // Register event handlers
            _client.Log += LogAsync;
            _commands.Log += LogAsync;
            _client.ShardReady += ShardReadyAsync;
            _client.InteractionCreated += HandleInteractionAsync;

            // Subscribe to the Ready event for each shard
            foreach (var shard in _client.Shards)
            {
                shard.Ready += async () =>
                {
                    foreach (var guild in shard.Guilds)
                    {
                        // Check if commands are already registered for this guild
                        if (!CommandsRegisteredForGuild(guild.Id))
                        {
                            await RegisterCommandsForGuild(guild);
                            MarkCommandsAsRegisteredForGuild(guild.Id);
                        }
                    }
                };
            }

            // Login and start the client
            await _client.LoginAsync(TokenType.Bot, botToken);
            await _client.StartAsync();

            Console.WriteLine("Bot is running. Type 'menu' in the console to access the menu.");

            // Command line interface loop
            while (true)
            {
                var input = Console.ReadLine();

                switch (input.ToLower(System.Globalization.CultureInfo.CurrentCulture))
                {
                    case "menu":
                        ShowMenu();
                        break;
                    case "1":
                        await ListGuilds();
                        break;
                    case "2":
                        // ShowHelp();
                        break;
                    default:
                        Console.WriteLine("Invalid input. Type 'menu' to access the menu.");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred: " + ex.Message);
        }
        //return;
    }

    private async Task RegisterCommandsAsync(DiscordSocketClient shard)
    {
        _client.ShardReady += ShardReadyAsync;
        _client.InteractionCreated += HandleInteractionAsync;

        // Add command modules
        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

        // Register commands for each guild in the shard
        foreach (var guild in shard.Guilds)
        {
            await RegisterCommandsForGuild(guild);
        }
    }

    private bool CommandsRegisteredForGuild(ulong guildId)
    {
        return _registeredGuilds.Contains(guildId);
    }

    private void MarkCommandsAsRegisteredForGuild(ulong guildId)
    {
        _registeredGuilds.Add(guildId);
    }

    // Additional method for DbContext
    private static MyDbContext GetDbContext(string guildName)
    {
        // Sanitize the guild name to be used as a database name
        var sanitizedGuildName = SanitizeFileName(guildName);
        var dbContext = new MyDbContext(sanitizedGuildName); // Pass sanitized guild name to the constructor
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private async Task RegisterCommandsForGuild(SocketGuild guild)
    {
        var serverInfoCommand = new SlashCommandBuilder()
            .WithName("serverinfo")
            .WithDescription("Display server information.")
            .Build();

        var userInfoCommand = new SlashCommandBuilder()
            .WithName("userinfo")
            .WithDescription("Display user information.")
            .AddOption("user", ApplicationCommandOptionType.User, "User to get information about")
            .Build();

        var startDbCommand = new SlashCommandBuilder()
            .WithName("startdb")
            .WithDescription("Start database operation.")
            .Build();

        try
        {
            await guild.CreateApplicationCommandAsync(serverInfoCommand);
            await guild.CreateApplicationCommandAsync(userInfoCommand);
            await guild.CreateApplicationCommandAsync(startDbCommand);
            Console.WriteLine($"Commands registered for guild: {guild.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error registering commands for guild {guild.Name}: {ex.Message}");
        }
    }

    private async Task LogAsync(LogMessage log)
    {
        //Console.WriteLine(log);
        Console.WriteLine(log.ToString());
        //return Task.FromResult(log);
    }

    private async Task ShardReadyAsync(DiscordSocketClient shardClient)
    {
        Console.WriteLine($"Shard {shardClient.ShardId} is ready.");

        // Register commands for each guild in the shard
        foreach (var guild in shardClient.Guilds)
        {
            if (!CommandsRegisteredForGuild(guild.Id))
            {
                await RegisterCommandsForGuild(guild);
                MarkCommandsAsRegisteredForGuild(guild.Id);
            }
        }
    }

    public class MyDbContext : DbContext
    {
        private readonly string _sanitizedGuildName;

        // Constructor now takes only guildName for simpler management
        public MyDbContext(string guildName)
        {
            _sanitizedGuildName = SanitizeFileName(guildName); // Sanitize the name to be file-system friendly
        }

        public DbSet<DatabaseData> DatabaseData { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Base directory where all databases will be stored
            var baseDir = @"D:\Databases\DiscordDB";
            //Console.WriteLine($"Base directory for databases: {baseDir}");                     // writing database path on every message?

            // Directory specific to the guild using the sanitized guild name
            var guildDir = Path.Combine(baseDir, _sanitizedGuildName);
            // Console.WriteLine($"Guild specific directory: {guildDir}");                       //this posts the directory before each before each channel 

            // Ensure the guild directory exists
            Directory.CreateDirectory(guildDir);

            // Define the specific database file path using only the sanitized guild name
            var dbPath = Path.Combine(guildDir, $"{_sanitizedGuildName}.db");
            //Console.WriteLine($"Database path: {dbPath}");                                        // writing database on each message ?

            // Use the database path in the SQLite connection string
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        // Existing SanitizeFileName method...
        private string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var validName = new string(name.Where(ch => !invalidChars.Contains(ch)).ToArray());
            return string.IsNullOrWhiteSpace(validName) ? "Guild" : validName; // Default to "Guild" if name is empty or all invalid characters
        }
    }

    private async Task HandleInteractionAsync(SocketInteraction arg)
    {
        if (arg is not SocketSlashCommand slashCommand) return;

        try
        {
            Console.WriteLine("Command invoked: " + slashCommand.Data.Name);

            switch (slashCommand.Data.Name.ToLower())
            {
                case "serverinfo":
                    await HandleServerInfoCommand(slashCommand);
                    break;

                case "userinfo":
                    await HandleUserInfoCommand(slashCommand);
                    break;

                case "startdb":
                    await HandleStartDbCommand(slashCommand);
                    break;

                default:
                    await slashCommand.RespondAsync("Unknown command.", ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred in HandleInteractionAsync: {ex.Message}");
            await slashCommand.RespondAsync("An error occurred while processing the command.", ephemeral: true);
        }
    }

    private async Task HandleServerInfoCommand(SocketSlashCommand slashCommand)
    {
        // Get server information
        var guild = (slashCommand.Channel as IGuildChannel)?.Guild as SocketGuild;
        if (guild != null)
        {
            var serverInfoMessage = $"Server Name: {guild.Name}\nServer ID: {guild.Id}";
            await slashCommand.RespondAsync(serverInfoMessage, isTTS: false, ephemeral: true);
        }
    }

    private async Task HandleUserInfoCommand(SocketSlashCommand slashCommand)
    {
        // Get user information
        var targetUser = slashCommand.Data.Options?.FirstOrDefault()?.Value as SocketUser;
        if (targetUser != null)
        {
            var userInfoMessage = $"User Name: {targetUser.Username}\nUser ID: {targetUser.Id}";
            await slashCommand.RespondAsync(userInfoMessage, isTTS: false, ephemeral: true);
        }
    }

    private async Task HandleStartDbCommand(SocketInteraction arg)
    {
        if (arg is SocketSlashCommand slashCommand)
        {
            try
            {
                var guildChannel = slashCommand.Channel as SocketGuildChannel;
                var guild = guildChannel?.Guild;
                if (guild != null)
                {
                    // Start collecting data for the guild
                    await CollectDataAsync(guild);
                }
                else
                {
                    await slashCommand.RespondAsync("Unable to access guild information.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while starting database command: {ex.Message}");
                await slashCommand.RespondAsync("An error occurred while processing the command.", ephemeral: true);
            }
        }
    }

    private void ShowMenu()
    {
        Console.WriteLine("Menu Options:");
        Console.WriteLine("1. List Servers");
        // Add more menu options here
    }

    private async Task ListGuilds()
    {
        Console.WriteLine("List of Servers:");
        guilds = _client.Guilds.ToList();
        for (int i = 0; i < guilds.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {guilds[i].Name} : {guilds[i].Id}");
        }

        Console.WriteLine("Enter the number of the server to collect data from:");
        var input = Console.ReadLine();
        if (int.TryParse(input, out int selectedIndex) && selectedIndex >= 1 && selectedIndex <= guilds.Count)
        {
            var selectedGuild = guilds[selectedIndex - 1];
            await CollectDataAsync(selectedGuild);
        }
        else
        {
            Console.WriteLine("Invalid input.");
        }
    }

    private async Task CollectDataAsync(SocketGuild selectedGuild)
    {
        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start(); // Start timing

        const int requestLimitPerSecond = 50; // As per Discord's rate limits
        var requestCounter = 0;
        var secondCounter = Stopwatch.StartNew();

        HashSet<string> uniquePhoneNumbers = new HashSet<string>();
        HashSet<string> uniqueLinks = new HashSet<string>();
        HashSet<ulong> processedMessageIds = new HashSet<ulong>();

        try
        {
            Console.WriteLine("Starting Data Collection");

            var phoneRegex = new Regex(@"(\+?[1-9][0-9]{0,2}[\s\(\)\-\.\,\/\|]*)?(\(?\d{3}\)?[\s\-\.\,\/\|]*\d{3}[\s\-\.\,\/\|]*\d{4})");
            var nameRegex = new Regex(@"(Name:|Name\s?:)\s?(.*?)($|\n)");
            var addressRegex = new Regex(@"\d{1,5}\s\w+\s\w*(?:\s\w+)?\s(?:Avenue|Lane|Road|Boulevard|Drive|Street|Ave|Dr|Rd|Blvd|Ln|St)\.?");
            var urlRegex = new Regex(@"https?:\/\/\S+");

            foreach (var textChannel in selectedGuild.TextChannels)
            {
                ulong? lastMessageId = null;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Fetching messages from Channel: {textChannel.Name}");
                Console.ResetColor();

                IEnumerable<IMessage> messages;
                do
                {
                    if (requestCounter >= requestLimitPerSecond)
                    {
                        var (remainingRequests, resetAfterSeconds) = GetRateLimitInfo();
                        var delayTime = CalculateDelay(remainingRequests, resetAfterSeconds);
                        if (delayTime > 0) await Task.Delay(delayTime);
                        requestCounter = 0;
                        secondCounter.Restart();
                    }

                    messages = await (lastMessageId == null ? textChannel.GetMessagesAsync(100).FlattenAsync() : textChannel.GetMessagesAsync(lastMessageId.Value, Direction.Before, 100).FlattenAsync());
                    requestCounter++;
                    if (!messages.Any()) break;

                    using (var dbContext = GetDbContext(selectedGuild.Name))
                    {
                        foreach (var message in messages)
                        {
                            if (!processedMessageIds.Contains(message.Id))
                            {
                                // Deduplicate at the content level
                                DeduplicateAndSaveData(dbContext, selectedGuild, message, phoneRegex, "phone", uniquePhoneNumbers);
                                DeduplicateAndSaveData(dbContext, selectedGuild, message, urlRegex, "url", uniqueLinks);                                
                                ExtractAndSaveData(dbContext, selectedGuild, message, nameRegex, "name");
                                ExtractAndSaveData(dbContext, selectedGuild, message, addressRegex, "address");

                                processedMessageIds.Add(message.Id); // Mark message as processed
                            }
                        }
                    }

                    lastMessageId = messages.Last().Id; // Update the last message ID for pagination
                } while (messages.Any());
            }

            stopwatch.Stop(); // Stop timing
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Data collection and saving complete for guild: {selectedGuild.Name}. Time elapsed: {stopwatch.Elapsed}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            stopwatch.Stop(); // Ensure stopwatch is stopped in case of an error
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred while collecting data for {selectedGuild.Name}: {ex.Message}. Time elapsed: {stopwatch.Elapsed}");
            Console.ResetColor();
        }
    }

    private (int RemainingRequests, double ResetAfterSeconds) GetRateLimitInfo(HttpResponseMessage response = null)
    {
        // These are header names provided by Discord
        const string RemainingHeader = "X-RateLimit-Remaining";
        const string ResetAfterHeader = "X-RateLimit-Reset-After";

        int remainingRequests = 50; // Fallback or safe assumption
        double resetAfterSeconds = 60; // Fallback or safe assumption

        if (response != null)
        {
            // Attempt to parse the actual remaining requests from headers
            if (response.Headers.TryGetValues(RemainingHeader, out var remainingValues))
            {
                int.TryParse(remainingValues.FirstOrDefault(), out remainingRequests);
            }

            // Attempt to parse the actual reset after seconds from headers
            if (response.Headers.TryGetValues(ResetAfterHeader, out var resetAfterValues))
            {
                double.TryParse(resetAfterValues.FirstOrDefault(), out resetAfterSeconds);
            }
        }
        else
        {
            // TODO: Implement logic to retrieve or estimate rate limit info from Discord.NET abstractions
            // This is where you would use Discord.NET's rate limit handling features or your own tracking logic
        }

        return (remainingRequests, resetAfterSeconds);
    }

    private int CalculateDelay(int remainingRequests, double resetAfterSeconds)
    {
        int buffer = 100; // Additional buffer time in milliseconds

        if (remainingRequests <= 1)
        {
            // If on the last request or out of requests, calculate delay for reset period plus buffer
            return (int)(resetAfterSeconds * 1000) + buffer;
        }
        else
        {
            // If there are multiple requests left, you might choose to continue without delay
            // or implement a delay based on your application's needs and behavior
            return 0;
        }
    }

    private void DeduplicateAndSaveData(MyDbContext dbContext, SocketGuild selectedGuild, IMessage message, Regex regex, string contentType, HashSet<string> uniqueContents)
    {
        foreach (Match match in regex.Matches(message.Content))
        {
            string content = match.Value.Trim();

            // Skip saving if this particular content has already been processed
            if (!uniqueContents.Contains(content))
            {
                SaveToDatabase(dbContext, selectedGuild, message, content, contentType);
                uniqueContents.Add(content); // Mark this content as processed
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        //Console.WriteLine("Sanitizing Data");
        var invalidChars = Path.GetInvalidFileNameChars();
        var validName = new string(name.Where(ch => !invalidChars.Contains(ch)).ToArray());
        return string.IsNullOrWhiteSpace(validName) ? "Guild" : validName; // Default to "Guild" if name is empty or all invalid characters
    }

    private void ExtractAndSaveData(MyDbContext dbContext, SocketGuild selectedGuild, IMessage message, Regex regex, string contentType)
    {
        foreach (Match match in regex.Matches(message.Content))
        {
            //Console.WriteLine("Extracting Data");
            string content = match.Value;
            SaveToDatabase(dbContext, selectedGuild, message, content, contentType);
        }
    }

    private void SaveToDatabase(MyDbContext dbContext, SocketGuild selectedGuild, IMessage message, string content, string contentType)
    {
        // Console.WriteLine("Saving To Databas");
        // Deduplication logic: Check if the same content for the specific type has already been stored for the same message
        if (dbContext.DatabaseData.Any(dd => dd.MessageId == message.Id && dd.ContentType == contentType && dd.Content == content))
        {
            //return;  // Skip saving as it's a duplicate
        }

        var databaseData = new DatabaseData
        {
            GuildId = selectedGuild.Id,
            GuildName = selectedGuild.Name,
            MessageId = message.Id,
            UserId = message.Author.Id,
            UserName = message.Author.Username,
            MessageContent = message.Content,
            ContentType = contentType,
            Content = content  // Content found by regex
        };

        // Assigning content based on its type
        switch (contentType)
        {
            case "phone":
                databaseData.PhoneNumber = content;
                break;
            case "name":
                databaseData.Name = content;
                break;
            case "address":
                databaseData.Address = content;
                break;
            case "url":
                databaseData.Url = content;
                break;
                // Add more cases for other content types as needed
        }

        dbContext.DatabaseData.Add(databaseData);
        dbContext.SaveChanges();
    }
}