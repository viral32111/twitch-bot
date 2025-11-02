using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

namespace TwitchBot.Twitch;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class ChatCommand : Attribute {
	private static readonly Dictionary<string, Func<Message, Task>> Commands = new();

	public readonly string Command;
	public readonly string[] Aliases;

	public ChatCommand(string command, string[]? aliases = null) {
		Command = command;
		Aliases = aliases ?? Array.Empty<string>();
	}

	public static void Register(Func<Message, Task> action) {
		ChatCommand? chatCommand = action.Method.GetCustomAttributes(false).OfType<ChatCommand>().FirstOrDefault();
		if (chatCommand == null) throw new Exception("Chat command action is missing the chat command attribute");

		if (Commands.ContainsKey(chatCommand.Command)) throw new Exception("Chat command is already registered");
		Commands.Add(chatCommand.Command, action);
		Program.Logger.LogDebug($"Registered chat command '{chatCommand.Command}'");

		foreach (string alias in chatCommand.Aliases) {
			if (Commands.ContainsKey(alias)) throw new Exception("Chat command alias is already registered");
			Commands.Add(alias, action);
			Program.Logger.LogDebug($"Registered alias '{alias}' for chat command: '{chatCommand.Command}'");
		}
	}

	public static async Task Invoke(string command, Message message) {
		Commands.TryGetValue(command, out Func<Message, Task>? action);
		if (action != null) await action.Invoke(message);
	}

	public static bool Exists(string command) => Commands.ContainsKey(command);
}
