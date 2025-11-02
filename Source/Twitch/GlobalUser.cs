using System;
using System.Drawing;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/* Global User Tags:
user-id=675961583
display-name=viral32111_
color=#FF0000
badge-info=
emotes=
emote-sets=0,300374282
*/

namespace TwitchBot.Twitch;

public class GlobalUser {

	// Static data from IRC message tags & API responses
	public readonly long Identifier;

	// Required dynamic data from IRC message tags & API responses
	public string DisplayName { get; private set; } = null!;
	public string LoginName { get; private set; } = null!;

	// Optional dynamic data from IRC message tags
	public Color? NameColor { get; private set; } = null;
	public string? BadgeInformation { get; private set; } = null;
	public string? Emotes { get; private set; } = null;
	public string[] EmoteSets { get; private set; } = Array.Empty<string>();

	// Creates a global user from an IRC message
	public GlobalUser(viral32111.InternetRelayChat.Message ircMessage) {
		Identifier = ExtractIdentifier(ircMessage);
		UpdateProperties(ircMessage);
	}

	// Creates a global user from a users API response
	public GlobalUser(JsonObject userData) {
		Identifier = long.Parse(userData["id"]!.GetValue<string>());
		DisplayName = userData["display_name"]!.GetValue<string>();
		LoginName = userData["login"]!.GetValue<string>(); // No need to force lowercase as this is always lowercase
	}

	// Extracts the user identifier from the IRC message tags
	public static int ExtractIdentifier(viral32111.InternetRelayChat.Message ircMessage) {
		if (!ircMessage.Tags.TryGetValue("user-id", out string? userIdentifier) || userIdentifier == null) throw new Exception("IRC message does not contain an identifier tag for this global user");
		return int.Parse(userIdentifier);
	}

	// Creates a global user by fetching the required data from the Twitch API, using an identifier
	// NOTE: Not specifying an identifier will fetch the current user (i.e. this bot's account)
	public static async Task<GlobalUser> FetchFromAPI(int? identifier = null) {
		JsonObject usersResponse = await API.Request("users", queryParameters: identifier != null ? new() {
			{ "id", identifier.ToString()! }
		} : null);

		return State.InsertGlobalUser(new(usersResponse["data"]![0]!.AsObject()));
	}

	// Creates a global user by fetching the required data from the Twitch API, using a login name
	public static async Task<GlobalUser> FetchFromAPI(string loginName) {
		JsonObject usersResponse = await API.Request("users", queryParameters: new() {
			{ "login", loginName }
		});

		return State.InsertGlobalUser(new(usersResponse["data"]![0]!.AsObject()));
	}

	public override string ToString() {
		return $"'{DisplayName}' ({Identifier})";
	}

	// Updates the dynamic data from the IRC message tags
	public void UpdateProperties(viral32111.InternetRelayChat.Message ircMessage) {
		if (!ircMessage.Tags.TryGetValue("display-name", out string? displayName) || string.IsNullOrWhiteSpace(displayName)) throw new Exception("IRC message does not contain a display name tag for this global user");
		DisplayName = displayName;
		LoginName = displayName.ToLower();

		// Sometimes empty
		if (ircMessage.Tags.TryGetValue("color", out string? nameColor) && nameColor != null) NameColor = ColorTranslator.FromHtml(nameColor);
		if (ircMessage.Tags.TryGetValue("badge-info", out string? badgeInformation) && badgeInformation != null) BadgeInformation = badgeInformation;
		if (ircMessage.Tags.TryGetValue("emotes", out string? emotes) && emotes == null) Emotes = emotes;
		if (ircMessage.Tags.TryGetValue("emote-sets", out string? emoteSets) && !string.IsNullOrWhiteSpace(emoteSets)) EmoteSets = emoteSets.Split(",");

	}

}
