using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets

namespace TwitchBot;

public static class UserSecrets {
	public static readonly string? TwitchOAuthSecret;

	static UserSecrets() {
		ConfigurationBuilder configurationBuilder = new();
		configurationBuilder.AddUserSecrets(Assembly.GetExecutingAssembly());

		IConfigurationRoot secrets = configurationBuilder.Build();

		TwitchOAuthSecret = secrets.GetValue<string>("AppClientSecret");

		PrintDeprecationNotice(secrets, "AppClientIdentifier");
		PrintDeprecationNotice(secrets, "AccountName");
	}

	private static void PrintDeprecationNotice(IConfigurationRoot secrets, string keyName) {
		if (!string.IsNullOrEmpty(secrets.GetValue<string>(keyName))) {
			Program.Logger.LogWarning($"The user secret '{keyName}' is deprecated, it should be removed from user secrets.");
		}
	}
}
