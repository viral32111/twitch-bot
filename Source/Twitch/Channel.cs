using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Collections.Generic;

using MongoDB.Driver;
using Microsoft.Extensions.Logging;

/* Channel Tags:
room-id=127154290
emote-only=0
followers-only=-1
r9k=0
rituals=0
slow=10
subs-only=0
*/

namespace TwitchBot.Twitch;

public class Channel {

	// Required static data from IRC message tags & API responses
	public readonly long Identifier;

	// Required dynamic data from IRC message tags & API responses
	public bool IsEmoteOnly { get; private set; }
	public int FollowersOnlyRequiredMinutes { get; private set; }
	public bool IsSubscribersOnly { get; private set; }
	public bool RequireUniqueMessages { get; private set; }
	public int SlowModeCooldownSeconds { get; private set; }

	// Optional dynamic data from IRC message tags
	public bool IsCelebrating { get; private set; } = false;

	// Required static data from IRC message parameter & API responses
	// TODO: A channel is just a user with additional properties, so we should inherit from GlobalUser and get rid of these properties
	public readonly string DisplayName;
	public readonly string Name;

	// Other relevant objects
	private readonly Client Client;

	// Creates a channel from an IRC message
	public Channel(viral32111.InternetRelayChat.Message ircMessage, Client client) {

		// Set the static data
		Identifier = ExtractIdentifier(ircMessage);

		// Set the dynamic data
		UpdateProperties(ircMessage);

		// Set the name, forced to lowercase
		if (string.IsNullOrWhiteSpace(ircMessage.Middle)) throw new Exception("IRC message does not contain a name for this channel");
		DisplayName = ircMessage.Middle[1..]; // Substring needed because channel name begins with hashtag
		Name = DisplayName.ToLower();

		// Set relevant objects
		Client = client;

	}

	// Creates a channel from a channel information & chat settings API response
	public Channel(JsonObject channelInformation, JsonObject chatSettings, Client client) {

		// Set relevant objects
		Client = client;

		// Set the static data
		Identifier = long.Parse(channelInformation["broadcaster_id"]!.GetValue<string>());
		DisplayName = channelInformation["broadcaster_name"]!.GetValue<string>();
		Name = channelInformation["broadcaster_login"]!.GetValue<string>(); // No need to force lowercase since this is always lowercase

		// Set the dynamic data
		IsEmoteOnly = chatSettings["emote_mode"]!.GetValue<bool>();
		FollowersOnlyRequiredMinutes = chatSettings["follower_mode"]!.GetValue<bool>() ? chatSettings["follower_mode_duration"]!.GetValue<int>() : 0;
		IsSubscribersOnly = chatSettings["subscriber_mode"]!.GetValue<bool>();
		RequireUniqueMessages = chatSettings["unique_chat_mode"]!.GetValue<bool>();
		SlowModeCooldownSeconds = chatSettings["slow_mode"]!.GetValue<bool>() ? chatSettings["slow_mode_wait_time"]!.GetValue<int>() : 0;

	}

	// Extracts the channel identifier from the IRC message tags
	public static int ExtractIdentifier(viral32111.InternetRelayChat.Message ircMessage) {
		if (!ircMessage.Tags.TryGetValue("room-id", out string? channelIdentifier) || channelIdentifier == null) throw new Exception("IRC message does not contain an identifier tag for this channel");
		return int.Parse(channelIdentifier);
	}

	// Creates a channel by fetching the required data from the Twitch API
	public static async Task<Channel> FetchFromAPI(long identifier, Client client) {

		// Fetch information about this channel from the Twitch API
		JsonObject channelInfoResponse = await API.Request("channels", queryParameters: new() {
			{ "broadcaster_id", identifier.ToString() }
		});

		// Fetch chat settings for this channel from the Twitch API
		JsonObject chatSettingsResponse = await API.Request("chat/settings", queryParameters: new() {
			{ "broadcaster_id", identifier.ToString() },
			{ "moderator_id", client.User!.Identifier.ToString() },
		});

		// Use the above API responses to create the channel in state for the first time
		return State.InsertChannel(new(channelInfoResponse["data"]![0]!.AsObject(), chatSettingsResponse["data"]![0]!.AsObject(), client));

	}

	public override string ToString() => $"'{DisplayName}' ({Identifier})";

	// Updates the dynamic data from the IRC message tags
	public void UpdateProperties(viral32111.InternetRelayChat.Message ircMessage) {

		// This isn't always there
		if (ircMessage.Tags.TryGetValue("emote-only", out string? isEmoteOnly) && !string.IsNullOrWhiteSpace(isEmoteOnly)) IsEmoteOnly = isEmoteOnly == "1";

		// Extract followers only mode as an integer, forced to be zero or greater (zero means disabled)
		if (ircMessage.Tags.TryGetValue("followers-only", out string? followersOnlyMinutes) && !string.IsNullOrWhiteSpace(followersOnlyMinutes)) FollowersOnlyRequiredMinutes = Math.Max(int.Parse(followersOnlyMinutes), 0);

		// Extract subscribers only mode as a boolean
		if (ircMessage.Tags.TryGetValue("subs-only", out string? isSubscribersOnly) && !string.IsNullOrWhiteSpace(isSubscribersOnly)) IsSubscribersOnly = isSubscribersOnly == "1";

		// Extract require unique messages only mode as a boolean
		if (ircMessage.Tags.TryGetValue("r9k", out string? requireUniqueMessages) && !string.IsNullOrWhiteSpace(requireUniqueMessages)) RequireUniqueMessages = requireUniqueMessages == "1";

		// Extract celebration in-progress as a boolean
		if (ircMessage.Tags.TryGetValue("rituals", out string? isCelebrating) && !string.IsNullOrWhiteSpace(isCelebrating)) IsCelebrating = isCelebrating == "1";

		// Extract slow mode cooldown as an integer, forced to be zero or greater (zero means disabled)
		if (ircMessage.Tags.TryGetValue("slow", out string? slowModeCooldownSeconds) && !string.IsNullOrWhiteSpace(slowModeCooldownSeconds)) SlowModeCooldownSeconds = Math.Max(int.Parse(slowModeCooldownSeconds), 0);

	}

	// Sends a chat message, can be as a reply to another message
	public async Task SendMessage(string message, Message? replyTo = null) => await Client.SendAsync(new("PRIVMSG",
		middle: $"#{Name}",
		parameters: message,
		tags: replyTo != null ? new() {
			{ "@reply-parent-msg-id", replyTo.Identifier.ToString() }
		} : null
	));

	// Fetches a list of streams for this channel
	public async Task<Stream[]> FetchStreams(int limit = 100) {

		// Update the VOD history & active streams in the database
		await UpdateVODSInDatabase();
		// await UpdateActiveLiveStreamsInDatabase();
		Program.Logger.LogDebug($"Updated VOD history & active streams for channel {this} in the database.");

		// Fetch our streams from the database
		List<Stream> streams = await Stream.DatabaseFind(forChannel: this, limit: limit);
		Program.Logger.LogDebug($"Fetched {streams.Count} stream(s) for channel {this} from the database.");

		// Sort the list in order of when the streams started
		streams.Sort((Stream currentStream, Stream nextStream) => nextStream.StartedAt.CompareTo(currentStream.StartedAt));

		// Convert the list to a fixed array so it cannot be modified before returning it
		return streams.ToArray();

	}

	// Updates this channel's VOD history in the database
	public async Task UpdateVODSInDatabase(bool traversePages = false) {

		// Will contain the cursor to the next page, if we are traversing
		string? nextPageCursor = null;

		do {

			// Fetch a list of the latest VODs for this channel from the Twitch API
			JsonObject vodsResponse = await API.Request("videos", queryParameters: new() {
				{ "user_id", Identifier.ToString() },
				{ "type", "archive" },
				{ "period", "all" },
				{ "sort", "time" },
				{ "first", "100" },
				{ "after", nextPageCursor ?? "" }
			});

			// Update the cursor to the next page (is set to null if this is the last page)
			nextPageCursor = vodsResponse["pagination"]!["cursor"]?.GetValue<string?>();

			// Create a list of VODs from the API results
			List<Stream> vods = vodsResponse["data"]!.AsArray().NotNull().Select(streamData => new Stream(streamData.AsObject(), this)).ToList();

			// Add all the VODs to the database, or update if they already exist
			foreach (Stream vod in vods) await vod.DatabaseUpdate(insertIfMissing: true);

			// Repeat above until we are on the last page, if we are traversing
		} while (nextPageCursor != null && traversePages == true);

	}

	// Updates this channel's current live stream in the database
	public async Task UpdateActiveLiveStreamsInDatabase() {

		// Fetch a list of the current streams for this channel from the Twitch API
		JsonObject streamsResponse = await API.Request("streams", queryParameters: new() {
			{ "user_id", Identifier.ToString() },
			{ "type", "live" },
			{ "first", "1" }, // Only one live stream can exist at a time for a channel (I think?)
		});

		// Create a list of streams from the API results
		List<Stream> streams = streamsResponse["data"]!.AsArray().NotNull().Select(streamData => new Stream(streamData.AsObject(), this)).ToList();

		// Add all the active live streams to the database, or update if they already exist
		foreach (Stream stream in streams) await stream.DatabaseUpdate(insertIfMissing: true);


	}

}
