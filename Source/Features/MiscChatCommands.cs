using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using TwitchBot.Twitch;

namespace TwitchBot.Features;

public class MiscChatCommands {

	[ModuleInitializer]
	public static void Setup() {

		// Register the chat commands
		ChatCommand.Register(HelloWorldCommand);
		ChatCommand.Register(RandomNumberCommand);
		ChatCommand.Register(ApertureScienceCommand);
		ChatCommand.Register(SocialMediaCommand);
		ChatCommand.Register(WhoAmICommand);

	}

	[ChatCommand("hello")]
	public static async Task HelloWorldCommand(Message message) {
		await message.Reply("Hello World!");
	}

	[ChatCommand("random")]
	public static async Task RandomNumberCommand(Message message) {
		await message.Reply($"Your random number is {Shared.RandomGenerator.Next(100)}");
	}

	[ChatCommand("cake")]
	public static async Task ApertureScienceCommand(Message message) {
		await message.Reply("This was a triumph! I'm making a note here: Huge success! It's hard to overstate my satisfaction. We do what we must because we can. For the good of all of us. Except the ones who are dead. But there's no sense crying over every mistake. You just keep on trying 'til you run out of cake.");
	}

	[ChatCommand("socials", new string[] { "twitter" })]
	public static async Task SocialMediaCommand(Message message) {
		await message.Reply("You can find me on Twitter! https://twitter.com/RawrelTV");
	}

	[ChatCommand("whoami")]
	public static async Task WhoAmICommand(Message message) {
		await message.Reply($"You are {message.Author.Global.DisplayName}, your name color is {message.Author.Global.NameColor}, your account identifier is {message.Author.Global.Identifier}, you are {(message.Author.IsSubscriber == true ? "subscribed" : "not subscribed")}, you are {(message.Author.IsModerator == true ? "a moderator" : "not a moderator")}.");
	}

}
