using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TwitchBot;

public static class Cloudflare {

	private static Process? tunnelClient = null;

	public static Uri StartTunnel(string clientVersion) { // , short localPortNumber

		ProcessStartInfo startInfo = new() {
			FileName = GetClientPath(clientVersion),
			Arguments = "tunnel --no-autoupdate --loglevel info --metrics 127.0.0.1: --hello-world", // --url http://127.0.0.1:3000
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = false,
			UseShellExecute = false,
			CreateNoWindow = true,

		};

		tunnelClient = Process.Start(startInfo);

		if (tunnelClient == null) throw new Exception("Failed to start the Cloudflare Tunnel client");

		Uri? tunnelUrl = null;

		while (tunnelUrl == null && tunnelClient.StandardError.EndOfStream == false) {

			// Read the line
			string? line = tunnelClient.StandardError.ReadLine();
			if (line == null) continue;

			Match urlMatch = Regex.Match(line, @"^.*(https:\/\/.+\.trycloudflare\.com).*$");
			if (urlMatch.Success) {
				tunnelUrl = new Uri(urlMatch.Groups[1].Value);
			}

		}

		return tunnelUrl!;

		/*Task readTask = Task.Run( () => {
			ReadStandardStream( tunnelClient.StandardError );
		} );

		readTask.Wait();*/

		//tunnelClient.Close();

		//Console.WriteLine( "closed" );

	}

	public static void StopTunnel() {

		if (tunnelClient == null) throw new Exception("Cloudflare Tunnel client not started");

		//tunnelClient.Close();
		tunnelClient.Kill();
		tunnelClient.WaitForExit();
		tunnelClient.Dispose();

	}

	/*private static void ReadStandardStream( StreamReader standardStream ) {

		// Run until there is nothing left to read
		while ( standardStream.EndOfStream == false ) {

			// Read the line
			string? line = standardStream.ReadLine();
			if ( line == null ) continue;

			Match urlMatch = Regex.Match( line, @"^.*(https:\/\/.+\.trycloudflare\.com).*$" );
			if ( urlMatch.Success ) {
				string tunnelUrl = urlMatch.Groups[ 1 ].Value;
				Console.WriteLine( tunnelUrl );
			}

			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine( line );
			Console.ForegroundColor = ConsoleColor.White;

		}

	}*/

	// Gets the path to the executable file of the specific client version for Windows or Linux
	public static string GetClientPath(string clientVersion) {
		if (Shared.IsWindows()) {
			return Path.Combine(Program.Configuration.CacheDirectory, $"cloudflared-{clientVersion}-windows-amd64.exe");
		} else {
			return Path.Combine(Program.Configuration.CacheDirectory, $"cloudflared-{clientVersion}-linux-amd64");
		}
	}

	// Gets the GitHub release download URL of the specific client version for Windows or Linux
	public static string GetDownloadUrl(string clientVersion) {
		string baseDownloadUrl = $"https://github.com/cloudflare/cloudflared/releases/download/{clientVersion}/";

		if (Shared.IsWindows()) {
			return string.Concat(baseDownloadUrl, "cloudflared-windows-amd64.exe");
		} else {
			return string.Concat(baseDownloadUrl, "cloudflared-linux-amd64");
		}
	}

	// Downloads a specific version of the Cloudflare Tunnel client from GitHub
	public async static Task DownloadClient(string clientVersion, string clientChecksum) {

		// The file path to store the executable file
		string executablePath = GetClientPath(clientVersion);

		// Create required directories in case they do not exist
		Shared.CreateDirectories();

		// Repeat...
		do {

			// Download the specified version of the client from GitHub releases
			HttpResponseMessage downloadResponse = await Shared.httpClient.GetAsync(GetDownloadUrl(clientVersion));

			// Save the downloaded client into an executable file in the data directory
			using (FileStream fileStream = new(executablePath, FileMode.Create, FileAccess.Write)) {
				await downloadResponse.Content.CopyToAsync(fileStream);
			}

			// ...until the executable is downloaded
		} while (!IsClientDownloaded(clientVersion, clientChecksum));

	}

	// Checks if a specific version of the client has already been downloaded
	public static bool IsClientDownloaded(string clientVersion, string clientChecksum) {

		// The file path to store the executable file
		string executablePath = GetClientPath(clientVersion);

		// Check failed if the file does not even exist
		if (!File.Exists(executablePath)) return false;

		// Validate the executable file checksum
		using (SHA256 sha256 = SHA256.Create()) {
			using (FileStream fileStream = new(executablePath, FileMode.Open, FileAccess.Read)) {

				// Calculate the checksum of the executable file
				string executableChecksum = Convert.ToHexString(sha256.ComputeHash(fileStream));

				// Do the checksums match?
				return string.Equals(executableChecksum, clientChecksum, StringComparison.OrdinalIgnoreCase);

			}
		}

	}

}
