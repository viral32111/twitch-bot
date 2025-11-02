using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;

using TwitchBot.Twitch.OAuth;

using Microsoft.Extensions.Logging;

namespace TwitchBot;

public static class Shared {
	private static readonly string randomCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

	public static readonly Random RandomGenerator = new();
	public static readonly HttpClient httpClient = new();

	public static UserAccessToken? UserAccessToken;
	public static readonly string UserAccessTokenFilePath = Path.Combine(Program.Configuration.DataDirectory, "UserAccessToken.json");

	public static readonly Dictionary<SslProtocols, string> SslProtocolNames = new() {
		// We reference TLS v1.0 & v1.1 here simply for human-readable names, we are not establishing connections with these insecure protocols.
		#pragma warning disable SYSLIB0039
		{ SslProtocols.Tls, "TLSv1.0" },
		{ SslProtocols.Tls11, "TLSv1.1" },
		#pragma warning restore SYSLIB0039

		{ SslProtocols.Tls12, "TLSv1.2" },
		{ SslProtocols.Tls13, "TLSv1.3" }
	};

	public static readonly Dictionary<CipherAlgorithmType, string> CipherAlgorithmNames = new() {
		{ CipherAlgorithmType.Aes, "AES" },
		{ CipherAlgorithmType.Aes128, "AES" },
		{ CipherAlgorithmType.Aes192, "AES" },
		{ CipherAlgorithmType.Aes256, "AES" },

		{ CipherAlgorithmType.Des, "DES" },
		{ CipherAlgorithmType.TripleDes, "Triple-DES" },

		{ CipherAlgorithmType.Rc2, "RC2" },
		{ CipherAlgorithmType.Rc4, "RC4" }
	};

	public static string GenerateRandomString(int length) {
		StringBuilder builder = new(length);

		for (int i = 0; i < length; i++) builder.Append(randomCharacters[RandomNumberGenerator.GetInt32(0, randomCharacters.Length)]);

		return builder.ToString();
	}

	// Checks if the current operating system is Windows
	public static bool IsWindows() {
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
	}

	// Creates required directories if they do not exist
	public static void CreateDirectories() {

		// Create the persistent data directory
		if (!Directory.Exists(Program.Configuration.DataDirectory)) {
			Directory.CreateDirectory(Program.Configuration.DataDirectory);
			Program.Logger.LogInformation($"Created data directory '{Program.Configuration.DataDirectory}'.");
		}

		// Create the cache directory
		if (!Directory.Exists(Program.Configuration.CacheDirectory)) {
			Directory.CreateDirectory(Program.Configuration.CacheDirectory);
			Program.Logger.LogInformation($"Created cache directory '{Program.Configuration.CacheDirectory}'.");
		}

	}

	public static bool IsDocker() {
		return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/.dockerenv");
	}

}
