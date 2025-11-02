using System;

/* Channel User Tags:
badges=moderator/1
mod=1
subscriber=0
turbo=0
user-type=mod
first-msg=0
returning-chatter=0
*/

namespace TwitchBot.Twitch;

public class ChannelUser {

	// Dynamic data from IRC message tags
	public bool IsModerator { get; private set; } // mod
	public bool IsSubscriber { get; private set; } // subscriber
	public bool IsTurbo { get; private set; } // turbo
	public bool IsFirstMessage { get; private set; } // first-msg
	public bool IsReturningChatter { get; private set; } // returning-chatter
	public string Badges { get; private set; } = null!; // badges
	public string Type { get; private set; } = null!; // user-type

	// Other relevant data
	public readonly GlobalUser Global;
	public readonly Channel Channel;

	// Creates a channel user (and thus global user) from an IRC message
	public ChannelUser(viral32111.InternetRelayChat.Message ircMessage, GlobalUser globalUser, Channel channel) {

		// Set relevant data
		Global = globalUser;
		Channel = channel;

		// Set dynamic data
		UpdateProperties(ircMessage);

	}

	public override string ToString() => $"'{Global.DisplayName}' ({Global.Identifier})";

	// Updates the dynamic data from the IRC message tags
	public void UpdateProperties(viral32111.InternetRelayChat.Message ircMessage) {

		Global.UpdateProperties(ircMessage);

		if (ircMessage.Tags.TryGetValue("mod", out string? isModerator) && !string.IsNullOrWhiteSpace(isModerator)) IsModerator = isModerator == "1";
		if (ircMessage.Tags.TryGetValue("subscriber", out string? isSubscriber) && !string.IsNullOrWhiteSpace(isSubscriber)) IsSubscriber = isSubscriber == "1";
		if (ircMessage.Tags.TryGetValue("turbo", out string? isTurbo) && !string.IsNullOrWhiteSpace(isTurbo)) IsTurbo = isTurbo == "1";
		if (ircMessage.Tags.TryGetValue("first-msg", out string? isFirstMessager) && !string.IsNullOrWhiteSpace(isFirstMessager)) IsFirstMessage = isFirstMessager == "1";
		if (ircMessage.Tags.TryGetValue("returning-chatter", out string? isReturningChatter) && !string.IsNullOrWhiteSpace(isReturningChatter)) IsReturningChatter = isReturningChatter == "1";
		if (ircMessage.Tags.TryGetValue("badges", out string? badges) && badges != null) Badges = badges;
		if (ircMessage.Tags.TryGetValue("user-type", out string? type) && type != null) Type = type;

	}

}
