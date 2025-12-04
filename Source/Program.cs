using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using MongoDB.Driver;

using viral32111.InternetRelayChat;

using TwitchBot.Database;
using TwitchBot.Twitch;
using TwitchBot.Twitch.OAuth;

using Microsoft.Extensions.Logging;
using TwitchBot.Features;

namespace TwitchBot;

public class Program {

	/// <summary>
	/// Global instance of the configuration.
	/// </summary>
	public static Configuration Configuration { get; private set; } = new();

	/// <summary>
	/// Global instance of a logger.
	/// </summary>
	public static ILogger Logger = Log.CreateLogger("TwitchBot");

	private static Task? UpdateTitleTask;

	// Windows-only
	[DllImport("Kernel32")]
	private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);
	private delegate bool EventHandler(CtrlType signal);
	private static EventHandler? consoleCtrlHandler;

	private static readonly Twitch.Client client = new();
	private static readonly Twitch.EventSubscription.Client eventSubClient = new();

	// The main entry-point of the program
	public static async Task Main(string[] arguments) {
		Logger.LogInformation($"Running version {GetAssemblyVersion()}.");

		// Load the configuration
		Configuration = Configuration.Load();

		// Display directory paths
		Logger.LogInformation($"The data directory is '{Configuration.DataDirectory}'.");
		Logger.LogInformation($"The cache directory is '{Configuration.CacheDirectory}'.");

		// Create required directories
		Shared.CreateDirectories();

		// Deprecation notice for the stream history file
		string streamHistoryFile = Path.Combine(Configuration.DataDirectory, "stream-history.json");
		if (File.Exists(streamHistoryFile)) Logger.LogWarning($"The stream history file ({streamHistoryFile}) is deprecated, it can safely be deleted.");

		// Exit now if this launch was only to initialize files
		if (arguments.Contains("--init")) {
			Logger.LogInformation("Initialized configuration & directories, exiting...");
			Environment.Exit(0);
			return;
		}

		// Ensure the OAuth identifier & secret exists
		if (string.IsNullOrWhiteSpace(Configuration.TwitchOAuthClientIdentifier) || string.IsNullOrWhiteSpace(Configuration.TwitchOAuthClientSecret)) {
			Logger.LogError("Twitch application client identifier and/or secret are null, empty, or otherwise invalid!");
			Environment.Exit(1);
			return;
		}

		// Ensure the primary channel ID exists
		if (Configuration.TwitchPrimaryChannelIdentifier <= 0) {
			Logger.LogError("Primary Twitch channel identifier is invalid!");
			Environment.Exit(1);
			return;
		}

		// Download the Cloudflare Tunnel client
		/*
		if ( !Cloudflare.IsClientDownloaded( Configuration.CloudflareTunnelVersion, Configuration.CloudflareTunnelChecksum ) ) {
			Logger.LogWarning( $"Cloudflare Tunnel client does not exist or is corrupt, downloading version {Configuration.CloudflareTunnelVersion}..." );
			await Cloudflare.DownloadClient( Configuration.CloudflareTunnelVersion, Configuration.CloudflareTunnelChecksum );
			Logger.LogInformation( $"Cloudflare Tunnel client downloaded to{Cloudflare.GetClientPath( Configuration.CloudflareTunnelVersion )}'." );
		} else {
			Logger.LogInformation( $"Using cached Cloudflare Tunnel client at{Cloudflare.GetClientPath( Configuration.CloudflareTunnelVersion )}'." );
		}
		*/

		// List all collections in MongoDB
		List<string> databaseCollectionNames = await Mongo.Database.ListCollectionNames().ToListAsync();
		Logger.LogInformation($"Found {databaseCollectionNames.Count} collection(s) in the database: {string.Join(", ", databaseCollectionNames)}.");

		// Open Redis connection
		// TODO: What are we even using Redis for??
		/*
		await Redis.Open();
		Logger.LogInformation( "Connected to Redis." );
		*/

		/**********************************************************************/

		// Attempt to load an existing broadcaster access token from disk
		try {
			Logger.LogInformation($"Loading broadcaster access token from '{Shared.BotAccessTokenFilePath}'...");
			Shared.BotAccessToken = UserAccessToken.Load(Shared.BotAccessTokenFilePath);

			// If the token is no longer valid, then refresh & save it
			if (!await Shared.BotAccessToken.Validate()) {

				Logger.LogWarning("The broadcaster access token is no longer valid, refreshing it...");
				await Shared.BotAccessToken.DoRefresh();

				Logger.LogInformation("Saving the refreshed broadcaster access token...");
				Shared.BotAccessToken.Save(Shared.BotAccessTokenFilePath);

			} else {
				Logger.LogInformation("The broadcaster access token is still valid, no refresh required.");
			}

		} catch (FileNotFoundException) {
			Logger.LogWarning($"Broadcaster access token file '{Shared.BotAccessTokenFilePath}' does not exist, requesting fresh token...");
			Shared.BotAccessToken = await UserAccessToken.RequestAuthorization(Configuration.TwitchOAuthRedirectURL, Configuration.TwitchOAuthScopes);
			Shared.BotAccessToken.Save(Shared.BotAccessTokenFilePath);
		}

		/**********************************************************************/

		// Attempt to load an existing broadcaster access token from disk
		try {
			Logger.LogInformation($"Loading broadcaster access token from '{Shared.BroadcasterAccessTokenFilePath}'...");
			Shared.BroadcasterAccessToken = UserAccessToken.Load(Shared.BroadcasterAccessTokenFilePath);

			// If the token is no longer valid, then refresh & save it
			if (!await Shared.BroadcasterAccessToken.Validate()) {

				Logger.LogWarning("The broadcaster access token is no longer valid, refreshing it...");
				await Shared.BroadcasterAccessToken.DoRefresh();

				Logger.LogInformation("Saving the refreshed broadcaster access token...");
				Shared.BroadcasterAccessToken.Save(Shared.BroadcasterAccessTokenFilePath);

			} else {
				Logger.LogInformation("The broadcaster access token is still valid, no refresh required.");
			}

		} catch (FileNotFoundException) {
			Logger.LogWarning($"Broadcaster access token file '{Shared.BroadcasterAccessTokenFilePath}' does not exist, requesting fresh token...");
			Shared.BroadcasterAccessToken = await UserAccessToken.RequestAuthorization(Configuration.TwitchOAuthRedirectURL, Configuration.TwitchOAuthScopes);
			Shared.BroadcasterAccessToken.Save(Shared.BroadcasterAccessTokenFilePath);
		}

		// If the scopes have changed, we need to re-authorize
		string[] currentScopes = Shared.BroadcasterAccessToken.Scopes;
		string[] requiredScopes = Configuration.TwitchOAuthScopes;
		if (!requiredScopes.All(scope => currentScopes.Contains(scope))) {
			Logger.LogWarning("The broadcaster access token does not have all of the required scopes, requesting fresh token...");
			Shared.BroadcasterAccessToken = await UserAccessToken.RequestAuthorization(Configuration.TwitchOAuthRedirectURL, Configuration.TwitchOAuthScopes);
			Shared.BroadcasterAccessToken.Save(Shared.BroadcasterAccessTokenFilePath);
		}

		/**********************************************************************/

		// Fetch this account's information
		client.User = await GlobalUser.FetchFromAPI();
		Logger.LogInformation($"I am {client.User}.");

		// Register event handlers for the Twitch client
		client.SecuredEvent += OnSecureCommunication;
		client.OpenedEvent += OnOpen;
		client.OnReady += OnReady;
		client.OnGlobalUserJoinChannel += OnGlobalUserJoinChannel;
		client.OnGlobalUserLeaveChannel += OnGlobalUserLeaveChannel;
		client.OnChannelChatMessage += OnChannelChatMessage;
		client.OnChannelUserUpdate += OnChannelUserUpdate;
		client.OnChannelUpdate += OnChannelUpdate;
		Logger.LogInformation("Registered Twitch client event handlers.");

		// Register event handlers for the EventSub client
		eventSubClient.OnReady += OnEventSubClientReady;
		eventSubClient.OnChannelUpdate += OnEventSubClientChannelUpdate;
		eventSubClient.OnStreamStart += OnEventSubClientStreamStart;
		eventSubClient.OnStreamFinish += OnEventSubClientStreamFinish;
		Logger.LogInformation("Registered Twitch EventSub client event handlers.");

		// TODO: Solution for Linux & Docker environment stop signal
		if (Shared.IsWindows()) {
			consoleCtrlHandler += new EventHandler(OnApplicationExit);
			SetConsoleCtrlHandler(consoleCtrlHandler, true);
			Logger.LogInformation("Registered application exit event handler.");
		}

		// Connect to Twitch chat
		Logger.LogDebug($"Connecting to Twitch chat ({Configuration.TwitchChatAddress}:{Configuration.TwitchChatPort})...");
		await client.OpenAsync(Configuration.TwitchChatAddress, Configuration.TwitchChatPort, true);
		Logger.LogInformation($"Connected to Twitch chat at '{Configuration.TwitchChatAddress}:{Configuration.TwitchChatPort}'.");

		// Keep the program running until we disconnect from Twitch chat
		await client.WaitAsync();

	}

	private static bool OnApplicationExit(CtrlType signal) {
		//Logger.LogInformation( "Stopping Cloudflare Tunnel client..." );
		//Cloudflare.StopTunnel();

		// Close the connection to the database
		// TODO: I guess Mongo does this automatically
		/*Database.Close().Wait();
		Logger.LogInformation( "Closed connection to the database." );*/

		// Close Redis connection
		/*
		Redis.Close().Wait();
		Logger.LogInformation( "Disconnected from Redis." );
		*/

		// Close the EventSub WebSocket connection
		//Logger.LogInformation( "Closing EventSub WebSocket connection..." );
		//eventSubClient.CloseAsync( WebSocketCloseStatus.NormalClosure, CancellationToken.None ).Wait();

		// Stop the update title task
		if (UpdateTitleTask != null) {
			Logger.LogDebug("Stopping update title background task...");
			UpdateTitleTask.Wait();
			Logger.LogInformation("Stopped update title background task.");
		}

		// Close chat connection
		Logger.LogDebug("Disconnecting...");
		client.CloseAsync().Wait();
		Logger.LogInformation("Disconnected from Twitch chat.");

		// Exit application
		Logger.LogInformation("Exiting...");
		Environment.Exit(0);

		return false;
	}

	private static async void OnSecureCommunication(object sender, SecuredEventArgs e) {
		string protocolName = Shared.SslProtocolNames[e.Protocol];
		string cipherName = Shared.CipherAlgorithmNames[e.CipherAlgorithm];

		string serverName = e.RemoteCertificate.Subject.Replace("CN=", "");
		string issuerName = e.RemoteCertificate.Issuer.Replace("CN=", "");

		Logger.LogDebug($"Established secure communication with '{serverName}' (verified by '{issuerName}' until {e.RemoteCertificate.GetExpirationDateString()}), using {protocolName} ({cipherName}-{e.CipherStrength}).");
	}

	// Fires after the underlying connection is ready (i.e. TLS established & receiving data)
	private static async void OnOpen(object sender, OpenedEventArgs e) {
		if (Shared.BotAccessToken == null) throw new Exception("Open event ran without previously fetching user access token");

		// Request all of Twitch's IRC capabilities
		Logger.LogDebug("Requesting capabilities...");
		await client.RequestCapabilities([
			Capability.Commands,
			Capability.Membership,
			Capability.Tags
		]);
		Logger.LogDebug("All requested capabilities were accepted.");

		// Send our credentials to authenticate
		Logger.LogDebug("Authenticating...");
		if (await client.Authenticate(client.User!.DisplayName, Shared.BotAccessToken.Access)) {
			Logger.LogInformation($"Authenticated with Twitch chat as user {client.User}.");
		} else {
			Logger.LogError($"Failed to authenticate as user {client.User}!");
			await client.CloseAsync();
		}
	}

	// Fires after authentication is successful & we have been informed about ourselves...
	private static async Task OnReady(Twitch.Client client, GlobalUser user) {
		Logger.LogInformation($"Ready as user {user}.");

		// Fetch the primary channel
		Channel primaryChannel = await Channel.FetchFromAPI(Configuration.TwitchPrimaryChannelIdentifier, client);

		// Join the primary channel
		Logger.LogDebug($"Joining primary channel {primaryChannel}...");
		if (await client.JoinChannel(primaryChannel)) {
			Logger.LogInformation($"Joined primary channel {primaryChannel}.");

			// await eventSubClient.ConnectAsync( Configuration.TwitchEventSubWebSocketURL, new( 0, 0, 10 ), CancellationToken.None );

			// UpdateTitleTask = TimeStreamedGoal.UpdateTitleWithRemainingHours(primaryChannel);

		} else {
			Logger.LogError("Failed to join primary channel!");
		}
	}

	// Fires when a global user joins a channel's chat
	// NOTE: Can be ourselves after calling Client.JoinChannel() or other users on Twitch when they join the stream
	private static async Task OnGlobalUserJoinChannel(Twitch.Client client, GlobalUser globalUser, Channel channel, bool isMe) {
		Logger.LogInformation($"Global user {globalUser} joined channel {channel}.");
	}

	// Fires when a global user leaves a channel's chat
	private static async Task OnGlobalUserLeaveChannel(Twitch.Client client, GlobalUser globalUser, Channel channel) {
		Logger.LogInformation($"Global user {globalUser} left channel {channel}.");
	}

	// Fires when a message in a channel's chat is received
	private static async Task OnChannelChatMessage(Twitch.Client client, Twitch.Message message) {
		Logger.LogInformation($"Channel user {message.Author} in channel {message.Author.Channel} said {message}.");

		// Run chat command, if this message is one
		if (message.Content[0] == Configuration.ChatCommandPrefix) {
			string command = message.Content[1..];
			if (ChatCommand.Exists(command)) await ChatCommand.Invoke(command, message);
			else Logger.LogWarning($"Chat command '{command}' is unknown");
		}
	}

	// Fires after a channel user is updated in state...
	private static async Task OnChannelUserUpdate(Twitch.Client client, ChannelUser user) {
		Logger.LogInformation($"Channel user {user.Global} updated.");
	}

	// Fires after a channel is updated in state...
	private static async Task OnChannelUpdate(Twitch.Client client, Channel channel) {
		Logger.LogInformation($"Channel {channel} updated.");
	}

	// Fires when the EventSub client is ready
	private static async Task OnEventSubClientReady(Twitch.EventSubscription.Client eventSubClient) {
		Logger.LogInformation($"EventSub client is ready, our session identifier is '{eventSubClient.SessionIdentifier}'.");

		Channel? channel = State.GetChannel(Configuration.TwitchPrimaryChannelIdentifier);
		if (channel == null) throw new Exception("Cannot find channel");

		await eventSubClient.SubscribeForChannel(Twitch.EventSubscription.SubscriptionType.ChannelUpdate, channel);
		await eventSubClient.SubscribeForChannel(Twitch.EventSubscription.SubscriptionType.StreamStart, channel);
		await eventSubClient.SubscribeForChannel(Twitch.EventSubscription.SubscriptionType.StreamFinish, channel);
	}

	// Fires when the EventSub client receives a channel update notification
	private static async Task OnEventSubClientChannelUpdate(Twitch.EventSubscription.Client eventSubClient, Channel channel, string title, string language, int categoryId, string categoryName, bool isMature) {
		Logger.LogInformation($"Channel {channel} updated their information ('{title}', '{language}', '{categoryId}', '{categoryName}', '{isMature}')");
	}

	// Fires when the EventSub client receives a stream start notification
	private static async Task OnEventSubClientStreamStart(Twitch.EventSubscription.Client eventSubClient, Channel channel, DateTimeOffset startedAt) {
		Logger.LogInformation($"Channel {channel} started streaming at '{startedAt}'.");
	}

	// Fires when the EventSub client receives a stream finish notification
	private static async Task OnEventSubClientStreamFinish(Twitch.EventSubscription.Client eventSubClient, Channel channel) {
		Logger.LogInformation($"Channel {channel} finished streaming.");
	}

	/*private static async Task OnError( Client client, string message ) {

		Logger.LogInformation( $"An error has occurred: '{message}'." );

		//Logger.LogInformation( "Stopping Cloudflare Tunnel client..." );
		//Cloudflare.StopTunnel(); // TODO: Kill tunnel on error?

		// Close the connection to the database
		Database.Close().Wait();
		Logger.LogInformation( "Closed connection to the database." );

		// Close Redis connection
		Redis.Close().Wait();
		Logger.LogInformation( "Disconnected from Redis." );

		// Close chat connection
		Logger.LogInformation( "Disconnecting..." );
		await client.CloseAsync();

		// Exit application
		Logger.LogInformation( "Exiting..." );
		Environment.Exit( 1 );

	}*/

	private static string GetAssemblyVersion() {
		Version? version = Assembly.GetExecutingAssembly().GetName().Version ?? throw new InvalidOperationException("Unable to get assembly version");
		if (version?.Major == null || version?.Minor == null || version?.Build == null) throw new InvalidOperationException("One or more assembly versions are null");
		return $"{version.Major}.{version.Minor}.{version.Build}";
	}

}

public enum CtrlType {
	CTRL_C_EVENT = 0,
	CTRL_BREAK_EVENT = 1,
	CTRL_CLOSE_EVENT = 2,
	CTRL_LOGOFF_EVENT = 5,
	CTRL_SHUTDOWN_EVENT = 6
}
