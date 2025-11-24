using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

namespace TwitchBot.Twitch;

public static class API {
	public async static Task<JsonObject> Request(string endpoint, HttpMethod? method = null, Dictionary<string, string>? queryParameters = null, JsonObject? payload = null, Dictionary<string, string>? headers = null, int retryCounter = 0) {

		// Construct the URL
		string queryString = queryParameters != null ? $"?{queryParameters.ToQueryString()}" : "";
		Uri targetUrl = new($"{Program.Configuration.TwitchAPIBaseURL}/{endpoint}{queryString}");

		// Create the request, defaulting to GET
		HttpRequestMessage httpRequest = new(method ?? HttpMethod.Get, targetUrl.ToString());

		// Always expect a JSON response
		httpRequest.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

		// Add any additional headers
		if (headers != null) {
			foreach (var header in headers) {
				if (header.Key == "Authorization") {
					httpRequest.Headers.Authorization = AuthenticationHeaderValue.Parse(header.Value);
				} else {
					httpRequest.Headers.Add(header.Key, header.Value);
				}
			}
		}

		// Set the request body, if one is provided
		if (payload != null) httpRequest.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

		// Send the request
		HttpResponseMessage httpResponse = await Shared.httpClient.SendAsync(httpRequest);
		Program.Logger.LogTrace($"{httpRequest.Method} '{httpRequest.RequestUri?.ToString()}' '{payload?.ToJsonString()}' => {( int ) httpResponse.StatusCode} {httpResponse.StatusCode}");

		// We do not want to continue if the response is not successful
		httpResponse.EnsureSuccessStatusCode();

		// Read the response content as JSON
		System.IO.Stream responseStream = await httpResponse.Content.ReadAsStreamAsync();
		JsonNode? responseJson = JsonNode.Parse(responseStream);
		if (responseJson == null) throw new Exception($"Failed to parse JSON response from API request '{endpoint}'");

		// Convert to a JSON object before returning
		return responseJson.AsObject();

	}

	public async static Task<JsonObject> BotRequest(string endpoint, HttpMethod? method = null, Dictionary<string, string>? queryParameters = null, JsonObject? payload = null, int retryCounter = 0) {

		Dictionary<string, string> oauthHeaders = new() {
			{ "Client-Id", Program.Configuration.TwitchOAuthClientIdentifier },
			{ "Authorization", Shared.BotAccessToken!.GetAuthorizationHeader().ToString() }
		};

		try {
			return await Request(endpoint, method, queryParameters, payload, oauthHeaders, retryCounter);
		} catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized) {
			if (retryCounter >= 1) throw new Exception($"Bot API request '{method}' '{endpoint}' failed due to unauthorized access even after refreshing the token!");

			Program.Logger.LogWarning("Bot access token has expired, refreshing & saving...");
			await Shared.BotAccessToken.DoRefresh();
			Shared.BotAccessToken.Save(Shared.BotAccessTokenFilePath);

			Program.Logger.LogInformation($"Retrying API request '{method}' '{endpoint}' in 10 seconds...");
			await Task.Delay(10000);

			return await BotRequest(endpoint, method, queryParameters, payload, retryCounter + 1);
		}
	}

	public async static Task<JsonObject> BroadcasterRequest(string endpoint, HttpMethod? method = null, Dictionary<string, string>? queryParameters = null, JsonObject? payload = null, int retryCounter = 0) {

		Dictionary<string, string> oauthHeaders = new() {
			{ "Client-Id", Program.Configuration.TwitchOAuthClientIdentifier },
			{ "Authorization", Shared.BroadcasterAccessToken!.GetAuthorizationHeader().ToString() }
		};

		try {
			return await Request(endpoint, method, queryParameters, payload, oauthHeaders, retryCounter);
		} catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized) {
			if (retryCounter >= 1) throw new Exception($"Broadcaster API request '{method}' '{endpoint}' failed due to unauthorized access even after refreshing the token!");

			Program.Logger.LogWarning("Broadcaster access token has expired, refreshing & saving...");
			await Shared.BroadcasterAccessToken.DoRefresh();
			Shared.BroadcasterAccessToken.Save(Shared.BroadcasterAccessTokenFilePath);

			Program.Logger.LogInformation($"Retrying API request '{method}' '{endpoint}' in 10 seconds...");
			await Task.Delay(10000);

			return await BroadcasterRequest(endpoint, method, queryParameters, payload, retryCounter + 1);
		}
	}

}
