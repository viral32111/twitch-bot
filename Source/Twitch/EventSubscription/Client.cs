using System;
using System.Text;
using System.Text.Json.Nodes;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

namespace TwitchBot.Twitch.EventSubscription;

// https://dev.twitch.tv/docs/eventsub/handling-WebSocket-events

public class Client {

	// The underlying WebSocket client & background task for receiving messages
	private readonly ClientWebSocket webSocketClient = new();
	private Task? receiveMessagesTask = null;

	// List of previously seen message identifiers, allows us to mitigate replay attacks
	private readonly List<string> messageIdentifierHistory = new();

	// Event that fires when we've received our session identifier
	public delegate Task OnReadyHandler(Client client);
	public event OnReadyHandler? OnReady;

	// Event that fires when we receive a channel update notification
	public delegate Task OnChannelUpdateHandler(Client client, Channel channel, string title, string language, int categoryId, string categoryName, bool isMature); // TODO: Add these properties to the channel class
	public event OnChannelUpdateHandler? OnChannelUpdate;

	// Event that fires when we receive a stream start notification
	public delegate Task OnStreamStartHandler(Client client, Channel channel, DateTimeOffset startedAt);
	public event OnStreamStartHandler? OnStreamStart;

	// Event that fires when we receive a stream finish notification
	public delegate Task OnStreamFinishHandler(Client client, Channel channel);
	public event OnStreamFinishHandler? OnStreamFinish;

	// Our session identifier is set when we receive the welcome message
	public string? SessionIdentifier = null;

	// Opens a connection to the EventSub WebSocket server & starts receiving messages in the background
	public async Task ConnectAsync(string url, TimeSpan timeout, CancellationToken cancellationToken) {
		await webSocketClient.ConnectAsync(new($"wss://{url}"), cancellationToken).WaitAsync(timeout, cancellationToken); // Timeout so we aren't left hanging
		receiveMessagesTask = ReceiveMessages(cancellationToken);
	}

	// Closes the WebSocket connection & waits for the background task to finish
	public async Task CloseAsync(WebSocketCloseStatus closeStatus, CancellationToken cancellationToken) {
		await webSocketClient.CloseAsync(closeStatus, "We'll meet again. Don't know where, don't know when.", cancellationToken);
		if (receiveMessagesTask != null) await receiveMessagesTask;
	}

	// Subscribes to an event, should only be done once we're ready
	public async Task SubscribeAsync(string subscriptionType, JsonObject condition) {
		await API.Request("eventsub/subscriptions", HttpMethod.Post, payload: new() {
			{ "type", subscriptionType },
			{ "version", 1 },
			{ "condition", condition },
			{ "transport", new JsonObject() {
				{ "method", "WebSocket" },
				{ "session_id", SessionIdentifier }
			} }
		});
		Program.Logger.LogDebug($"Subscribed to event '{subscriptionType}' with condition '{condition.ToJsonString()}' on WebSocket session '{SessionIdentifier}'");
	}

	// Subscribes to a channel event, which requires a broadcaster user ID
	public Task SubscribeForChannel(string subscriptionType, Channel channel) => SubscribeAsync(subscriptionType, new() {
		{ "broadcaster_user_id", channel.Identifier.ToString() }
	});

	/*
	public static async Task FetchSubscriptions() {
		JsonObject response = await API.Request( "eventsub/subscriptions" );
		response[ "data" ]!
	}
	*/

	// Receives WebSocket messages, ideally in the background
	private async Task ReceiveMessages(CancellationToken cancellationToken) {
		byte[] receiveBuffer = new byte[8192]; // This seems a reasonable size

		// Repeat so long as we're connected & we haven't been cancelled
		while (webSocketClient.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested) {

			// Wait for a WebSocket message
			Array.Clear(receiveBuffer);
			WebSocketReceiveResult receiveResult = await webSocketClient.ReceiveAsync(receiveBuffer, cancellationToken);
			string receiveBufferText = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);
			Program.Logger.LogDebug($"Received EventSub WebSocket '{receiveResult.MessageType}' message of {receiveResult.Count} bytes{receiveBufferText}'");

			switch (receiveResult.MessageType) {

				// Parse any text as JSON & process it
				case WebSocketMessageType.Text:
					ProcessMessage(JsonNode.Parse(receiveBufferText)!.AsObject());
					break;

				// Complain if we receive binary data
				case WebSocketMessageType.Binary:
					throw new Exception("Received binary data on EventSub WebSocket");

				// Do nothing for close confirmation
				case WebSocketMessageType.Close:
					break;

				// Warn us about other unhandled message types
				default:
					Program.Logger.LogWarning($"Ignored WebSocket message type '{receiveResult.MessageType}' with data '{receiveBufferText}'");
					break;

			}

		}
	}

	// Processes the WebSocket messages that we care about
	private void ProcessMessage(JsonObject message) {

		// Extract useful data about this message
		string messageIdentifier = message["metadata"]!["message_id"]!.GetValue<string>();
		DateTimeOffset messageDateTime = DateTimeOffset.Parse(message["metadata"]!["message_timestamp"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		string messageType = message["metadata"]!["message_type"]!.GetValue<string>();
		JsonObject messageData = message["payload"]!.AsObject();
		Program.Logger.LogDebug($"Processing EventSub message ({messageIdentifier}) of type '{messageType}' with data '{messageData.ToJsonString()}'");

		// Fail if we have already processed this message
		if (messageIdentifierHistory.Contains(messageIdentifier)) throw new Exception("Received an EventSub message we have already received before");

		// Fail if the message is older than 10 minutes
		if (messageDateTime < DateTimeOffset.UtcNow.AddMinutes(-10)) throw new Exception("Received an old EventSub message");

		switch (messageType) {

			// Fire the ready event once we have our session identifier from the welcome message
			case "session_welcome":
				SessionIdentifier = messageData["session"]!["id"]!.GetValue<string>();
				// TODO: Begin keep-alive timeout countdown here, it should disconnect us unless we receive a 'session_keepalive' message before it finishes.
				OnReady?.Invoke(this);
				break;

			// Twitch letting us know the connection is still alive
			case "session_keepalive":
				// TODO: This should trigger a completion source to let the keep-alive timeout countdown know we have received a 'session_keepalive' message
				Program.Logger.LogDebug("The EventSub WebSocket connection is still alive");
				break;

			// Process notifications for events we've subscribed to
			case "notification":
				string subscriptionType = messageData["subscription"]!["type"]!.GetValue<string>();
				JsonObject eventData = messageData["event"]!.AsObject();
				ProcessNotification(subscriptionType, eventData);
				break;

			// Warn us about any other unhandled messages
			default:
				Program.Logger.LogWarning($"Ignored EventSub message '{messageType}' with data '{messageData.ToJsonString()}'");
				break;

		}

		// We have now processed this message
		messageIdentifierHistory.Add(messageIdentifier);

	}

	// Processes notifications for events we've subscribed to
	private void ProcessNotification(string subscriptionType, JsonObject eventData) {
		Program.Logger.LogDebug($"Processing EventSub notification of type '{subscriptionType}' with data '{eventData.ToJsonString()}'");

		if (subscriptionType == SubscriptionType.StreamStart) {
			if (eventData["type"]!.GetValue<string>() != "live") return;

			Channel? channel = State.GetChannel(int.Parse(eventData["broadcaster_user_id"]!.GetValue<string>()));
			if (channel == null) throw new Exception("Received EventSub notification for an unknown channel");

			DateTimeOffset startedAt = DateTimeOffset.Parse(eventData["started_at"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

			OnStreamStart?.Invoke(this, channel, startedAt);

		} else if (subscriptionType == SubscriptionType.StreamFinish) {
			Channel? channel = State.GetChannel(int.Parse(eventData["broadcaster_user_id"]!.GetValue<string>()));
			if (channel == null) throw new Exception("Received EventSub notification for an unknown channel");

			OnStreamFinish?.Invoke(this, channel);

		} else if (subscriptionType == SubscriptionType.ChannelUpdate) {
			Channel? channel = State.GetChannel(int.Parse(eventData["broadcaster_user_id"]!.GetValue<string>()));
			if (channel == null) throw new Exception("Received EventSub notification for an unknown channel");

			string title = eventData["title"]!.GetValue<string>();
			string language = eventData["language"]!.GetValue<string>();
			int categoryId = int.Parse(eventData["category_id"]!.GetValue<string>());
			string categoryName = eventData["category_name"]!.GetValue<string>();
			bool isMature = eventData["is_mature"]!.GetValue<bool>();

			OnChannelUpdate?.Invoke(this, channel, title, language, categoryId, categoryName, isMature);

		} else {
			Program.Logger.LogWarning($"Ignored EventSub notification '{subscriptionType}' with data '{eventData.ToJsonString()}'");
		}

	}

}
