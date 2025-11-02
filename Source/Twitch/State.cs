using System;
using System.Linq;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

/* Unknown Tags:
client-nonce=640a320bc852e4bc9034e93feac64b38
flags=
*/

namespace TwitchBot.Twitch;

public static class State {

	private static readonly Dictionary<Guid, Message> Messages = [];
	private static readonly Dictionary<long, Channel> Channels = [];
	private static readonly Dictionary<long, GlobalUser> GlobalUsers = [];
	private static readonly Dictionary<long, ChannelUser> ChannelUsers = [];

	/*********************************************************************************************/

	// This only has insert & no update because message's have nothing to update, they are purely static
	public static Message InsertMessage(Message message) {
		Messages.Add(message.Identifier, message);
		Program.Logger.LogDebug($"Inserted message {message} in state.");
		return message;
	}

	public static Message? GetMessage(Guid identifier) => Messages[identifier];

	/*********************************************************************************************/

	// This is needed for channels created by the channel information & chat settings API responses
	public static Channel InsertChannel(Channel channel) {
		Channels.Add(channel.Identifier, channel);
		Program.Logger.LogDebug($"Inserted channel {channel} in state.");
		return channel;
	}

	public static Channel UpdateChannel(viral32111.InternetRelayChat.Message ircMessage, Client client) {
		long identifier = Channel.ExtractIdentifier(ircMessage);

		if (Channels.TryGetValue(identifier, out Channel? channel) && channel != null) {
			channel.UpdateProperties(ircMessage);
			Program.Logger.LogDebug($"Updated channel '{identifier}' in state.");
		} else {
			channel = new(ircMessage, client);
			Channels.Add(channel.Identifier, channel);
			Program.Logger.LogDebug($"Created channel '{channel.Identifier}' in state.");
		}

		return channel;
	}

	public static Channel? GetChannel(long identifier) => Channels[identifier];
	public static Channel? FindChannelByName(string name) => Channels.Values.Where(channel => channel.Name == name.ToLower()).FirstOrDefault();

	public static bool TryGetChannel(long identifier, out Channel? channel) => Channels.TryGetValue(identifier, out channel);

	/*********************************************************************************************/

	// This is needed for global users created by the users API response
	public static GlobalUser InsertGlobalUser(GlobalUser globalUser) {
		GlobalUsers.Add(globalUser.Identifier, globalUser);
		Program.Logger.LogDebug($"Inserted global user {globalUser} in state.");
		return globalUser;
	}

	public static GlobalUser UpdateGlobalUser(viral32111.InternetRelayChat.Message ircMessage) {
		long identifier = GlobalUser.ExtractIdentifier(ircMessage);

		if (GlobalUsers.TryGetValue(identifier, out GlobalUser? globalUser) && globalUser != null) {
			globalUser.UpdateProperties(ircMessage);
			Program.Logger.LogDebug($"Updated global user {globalUser} in state.");
		} else {
			globalUser = new(ircMessage);
			GlobalUsers.Add(globalUser.Identifier, globalUser);
			Program.Logger.LogDebug($"Created global user {globalUser} in state.");
		}

		return globalUser;
	}

	public static GlobalUser? GetGlobalUser(long identifier) => GlobalUsers[identifier];
	public static GlobalUser? FindGlobalUserByName(string loginName) => GlobalUsers.Values.Where(globalUser => globalUser.LoginName == loginName).FirstOrDefault();

	/*********************************************************************************************/

	public static ChannelUser UpdateChannelUser(viral32111.InternetRelayChat.Message ircMessage, Channel channel) {

		// USERSTATE updates seem to never contain a user-id IRC message tag, so we can't use GlobalUser.ExtractIdentifier()
		if (!ircMessage.Tags.TryGetValue("display-name", out string? displayName) || string.IsNullOrWhiteSpace(displayName)) throw new Exception("Cannot possibly update a channel user without their display name IRC message tag");
		GlobalUser? globalUser = FindGlobalUserByName(displayName.ToLower());
		globalUser ??= UpdateGlobalUser(ircMessage); // for PRIVMSG
		Program.Logger.LogDebug($"Found global user {globalUser} in state.");

		// TODO: This doesn't even get the user for a specific channel, so our channel users are glorified global users right now...
		if (ChannelUsers.TryGetValue(globalUser.Identifier, out ChannelUser? channelUser) && channelUser != null) {
			channelUser.UpdateProperties(ircMessage);
			Program.Logger.LogDebug($"Updated channel user {globalUser} in state.");
		} else {
			channelUser = new(ircMessage, globalUser, channel);
			ChannelUsers.Add(channelUser.Global.Identifier, channelUser);
			Program.Logger.LogDebug($"Created channel user {channelUser.Global} in state.");
		}

		return channelUser;
	}

	public static ChannelUser? GetChannelUser(long identifier) => ChannelUsers[identifier];

}
