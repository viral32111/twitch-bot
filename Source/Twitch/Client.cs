using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using viral32111.InternetRelayChat;

// A client for connecting to Twitch chat
// https://dev.twitch.tv/docs/irc

// on stream start/stop (change ig)
// on view count change (on viewer/user JOIN and PART - membership capability)
// on mod action (on user ban, on user timeout)
// on chat message sent
// on chat message delete (more of a mod action ig)
// on user follow
// on user subscribe
// on user channel point used
// on user send bits
// on stream metadata change (on title change, on game change, in tags change, etc.)
// on user modded/unmodded

namespace TwitchBot.Twitch;

public class Client : viral32111.InternetRelayChat.Client {

	// Regular expression for matching the "Your Host" command
	protected readonly Regex hostPattern = new(@"^Your host is (?'host'.+)$");

	// Ourselves
	public GlobalUser? User;
	protected string ExpectedHost = "irc.chat.twitch.tv";

	// Event that runs after the client is ready
	public delegate Task OnReadyHandler(Client client, GlobalUser user);
	public event OnReadyHandler? OnReady;

	// Event that runs after a user joins a channel
	public delegate Task OnGlobalUserJoinChannelHandler(Client client, GlobalUser user, Channel channel, bool isMe);
	public event OnGlobalUserJoinChannelHandler? OnGlobalUserJoinChannel;

	// Event that runs after a user leaves a channel
	public delegate Task OnGlobalUserLeaveChannelHandler(Client client, GlobalUser user, Channel channel);
	public event OnGlobalUserLeaveChannelHandler? OnGlobalUserLeaveChannel;

	// Event that runs after a chat message is received
	public delegate Task OnChannelChatMessageHandler(Client client, Message message);
	public event OnChannelChatMessageHandler? OnChannelChatMessage;

	// Event that runs after a user is updated in a channel
	public delegate Task OnChannelUserUpdateHandler(Client client, ChannelUser user);
	public event OnChannelUserUpdateHandler? OnChannelUserUpdate;

	// Event that runs after a channel is updated
	public delegate Task OnChannelUpdateHandler(Client client, Channel channel);
	public event OnChannelUpdateHandler? OnChannelUpdate;

	// Constructor to register event handlers
	public Client() {
		MessagedEvent += ProcessMessage;
	}

	// Request capabilities for this connection
	public async Task RequestCapabilities(string[] desiredCapabilities) {

		// Send the capabilities request, and wait for response message(s)
		viral32111.InternetRelayChat.Message responseMessage = (await SendWaitResponseAsync(new(command: Command.RequestCapabilities, parameters: string.Join(' ', desiredCapabilities))))[0];

		// Get a list of the granted capabilities from the response message
		string[]? grantedCapabilities = responseMessage.Parameters?.Split(' ');
		if (grantedCapabilities == null) throw new Exception("No granted capabilities in capabilities response message");

		// Fail if the desired capabilities do not match the granted capabilities
		if (!desiredCapabilities.SequenceEqual(grantedCapabilities)) throw new Exception("Not all desired capabilities were granted");

	}

	// Authenticates using our OAuth credentials
	public async Task<bool> Authenticate(string accountName, string accessToken) {

		// Send the access token as the password, the account name as the username, and wait for a response
		await SendAsync(new viral32111.InternetRelayChat.Message(viral32111.InternetRelayChat.Command.Password, parameters: $"oauth:{accessToken}"));
		viral32111.InternetRelayChat.Message? responseMessage = (await SendWaitResponseAsync(new viral32111.InternetRelayChat.Message(viral32111.InternetRelayChat.Command.Nick, parameters: accountName)))[0];

		// Check the response to see if authentication was successful or failed
		if (responseMessage.IsFromSystem() && responseMessage.Command == Command.Welcome) return true;
		else if (responseMessage.IsFromSystem() && responseMessage.Command == Command.Notice && responseMessage.Parameters == "Login authentication failed") return false;
		else throw new Exception("Authentication response IRC message was neither successful or a failure");

	}

	// Joins a channel's chat
	public async Task<bool> JoinChannel(Channel channel) {

		// IRC channels are prefixed with a hashtag
		string channelName = $"#{channel.Name}";

		// Request to join this channel & wait for a response
		viral32111.InternetRelayChat.Message responseMessage = (await SendWaitResponseAsync(new(Command.Join, middle: channelName)))[0];

		// Fire the channel join event if this message is for us, is about a join, and is for the channel we wanted to join
		if (responseMessage.IsAboutUser(User!.LoginName) && responseMessage.Command == Command.Join && responseMessage.Middle == channelName) {
			OnGlobalUserJoinChannel?.Invoke(this, User!, channel, true);
			// NOTE: Don't need to update global user or channel state here because this IRC message doesGAMn't contain anything useful
			return true;
		} else {
			return false;
		}

	}

	// Processes received messages
	private async void ProcessMessage(object sender, MessagedEventArgs e) {
		Program.Logger.LogTrace($"Processing IRC message '{e.Message}'...");

		// Are we being told our host?
		if (e.Message.IsFromSystem() && e.Message.Command == Command.YourHost && e.Message.Parameters != null) {

			// Run regular expression match on parameters to extract the hostname
			Match hostMatch = hostPattern.Match(e.Message.Parameters);
			if (!hostMatch.Success) throw new Exception("Failed to extract hostname from expected host IRC message");

			// Set the expected hostname to the match group
			ExpectedHost = hostMatch.Groups["host"].Value;
			Program.Logger.LogInformation($"Only accepting messages from host '{ExpectedHost}' from now on.");

			// One day this might change, so let's display it...
		} else if (e.Message.IsFromSystem() && e.Message.Command == Command.MoTD && e.Message.Parameters != null) {
			Program.Logger.LogDebug($"MoTD: '{e.Message.Parameters}'");

			// Update ourselves in state & fire the ready event, if we are being informed about our global self
		} else if (e.Message.IsFromSystem() && e.Message.Command == Command.GlobalUserState && e.Message.Tags.Count > 0) {
			OnReady?.Invoke(this, State.UpdateGlobalUser(e.Message));

			// Update a channel in state & fire the channel update event, if we are being informed of a channel update
		} else if (e.Message.IsFromSystem() && e.Message.Command == Command.RoomState && e.Message.Middle != null && e.Message.Tags.Count > 0) {
			OnChannelUpdate?.Invoke(this, State.UpdateChannel(e.Message, this));

			// Update a channel user in state & fire the channel user update event, if we are being informed of a channel user update
		} else if (e.Message.IsFromSystem() && e.Message.Command == Command.UserState && e.Message.Middle != null && e.Message.Tags.Count > 0) {
			Channel? channel = State.FindChannelByName(e.Message.Middle[1..]) ?? throw new Exception("Cannot update channel user because the channel is unknown");

			OnChannelUserUpdate?.Invoke(this, State.UpdateChannelUser(e.Message, channel));

			// Are we being informed about the users in a channel we just joined?
		} else if (e.Message.IsFromSystem() && e.Message.Command == Command.Names && e.Message.Middle != null && e.Message.Parameters != null) {

			// Find the channel in state
			Channel? channel = State.FindChannelByName(e.Message.Middle[(e.Message.Middle.LastIndexOf('#') + 1)..]);
			if (channel == null) throw new Exception("Received user list for an unknown channel");

			// Get a list of user names, excluding ourselves
			string[] userNames = e.Message.Parameters.Split(' ').Where(userName => userName != User!.LoginName).ToArray();

			// Find each user in state, or create them using the API if they don't exist
			List<GlobalUser> globalUsers = [];
			foreach (string userName in userNames) {
				GlobalUser? globalUser = State.FindGlobalUserByName(userName);
				globalUser ??= await GlobalUser.FetchFromAPI(userName); // TODO: The API supports specifying multiple names to reduce request count, we should do that!
				globalUsers.Add(globalUser);
			}

			// Display the users
			if (globalUsers.Count > 0) {
				Program.Logger.LogInformation($"There are {globalUsers.Count} global users in channel {channel.ToString()}: {string.Join(", ", globalUsers.Select(globalUser => globalUser.ToString()).ToArray())}");
			} else {
				Program.Logger.LogInformation($"There are no global users in channel {channel.ToString()}.");
			}

			// Did someone send a chat message in a channel?
		} else if (!e.Message.IsFromSystem() && e.Message.Command == "PRIVMSG" && e.Message.Middle != null && e.Message.Parameters != null && e.Message.Tags.Count > 0) {

			// Update state for channel, channel user & message
			Channel channel = State.UpdateChannel(e.Message, this);
			ChannelUser channelUser = State.UpdateChannelUser(e.Message, channel);
			Message message = State.InsertMessage(new(e.Message, channelUser));

			// Fire the chat message event
			OnChannelChatMessage?.Invoke(this, message);

			// Has a user (that isn't us) joined a channel's chat?
		} else if (!e.Message.IsFromSystem() && e.Message.User != null && e.Message.Command == "JOIN" && e.Message.Middle != null) {

			// Find the channel in state
			Channel? channel = State.FindChannelByName(e.Message.Middle[1..]) ?? throw new Exception("Received user join for an unknown channel");

			// Find the global user in state, or create using the API if they don't exist
			GlobalUser? globalUser = State.FindGlobalUserByName(e.Message.User);
			globalUser ??= await GlobalUser.FetchFromAPI(e.Message.User);

			// Fire the user join channel event
			OnGlobalUserJoinChannel?.Invoke(this, globalUser, channel, false);

			// Has a user left a channel's chat?
		} else if (!e.Message.IsFromSystem() && e.Message.User != null && e.Message.Command == "PART" && e.Message.Middle != null) {

			// Find the channel in state
			Channel? channel = State.FindChannelByName(e.Message.Middle[1..]) ?? throw new Exception("Received user leave for an unknown channel");

			// Find the global user in state, or create using the API if they don't exist
			GlobalUser? globalUser = State.FindGlobalUserByName(e.Message.User);
			globalUser ??= await GlobalUser.FetchFromAPI(e.Message.User);

			// Fire the user leave channel event
			OnGlobalUserLeaveChannel?.Invoke(this, globalUser, channel);

			// We don't know what to do with this message
		} else {
			Program.Logger.LogWarning($"Ignored IRC message: '{e.Message}'");
		}

	}

}
