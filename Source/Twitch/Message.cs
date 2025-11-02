using System;
using System.Threading.Tasks;

/* Message Tags:
id=f93c6fec-e157-4224-8035-1b6f148a1ff8
tmi-sent-ts=1667397167274
*/

namespace TwitchBot.Twitch;

public class Message {

	// Static data from IRC message tags
	public readonly Guid Identifier; // id
	public readonly DateTimeOffset SentAt; // tmi-sent-ts

	// From IRC message parameter
	public readonly string Content;

	// Other relevant objects
	public readonly ChannelUser Author;

	// Creates a message from an IRC message
	public Message(viral32111.InternetRelayChat.Message ircMessage, ChannelUser author) {

		// Set relevant objects
		Author = author;

		// Set identifier
		Identifier = ExtractIdentifier(ircMessage);

		// Extract sent at from unix timestamp
		if (!ircMessage.Tags.TryGetValue("tmi-sent-ts", out string? messageSentTimestamp) || messageSentTimestamp == null) throw new Exception("IRC message does not contain a timestamp for this message");
		SentAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(messageSentTimestamp));

		// Set content
		if (ircMessage.Parameters == null) throw new Exception("IRC message does not contain content for this message");
		Content = ircMessage.Parameters;

	}

	// Extracts the channel identifier from the IRC message tags
	public static Guid ExtractIdentifier(viral32111.InternetRelayChat.Message ircMessage) {
		if (!ircMessage.Tags.TryGetValue("id", out string? messageIdentifier) || messageIdentifier == null) throw new Exception("IRC message does not contain an identifier tag for this message");
		return Guid.Parse(messageIdentifier);
	}

	public override string ToString() => $"'{Content}' ({Identifier})";

	// Sends a reply to this message
	public async Task Reply(string content) => await Author.Channel.SendMessage(content, replyTo: this);

}
