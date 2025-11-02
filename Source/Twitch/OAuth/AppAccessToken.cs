using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace TwitchBot.Twitch.OAuth;

/*public static class Token {
	public static readonly UserAccessToken UserAccessToken;
	public static readonly AppAccessToken AppAccessToken;

	static Token() {
		//UserAccessToken = AuthorizationCode...
		//AppAccessToken = ClientCredentials...
	}
}*/

// TODO: String enum for scopes?

// TODO: String enum for type so it can be used in the authorization header?
public enum TokenType {
	Bearer = 0
}

// Client credentials grant flow
public class AppAccessToken {
	public TokenType Type;
	public string Access;
	public DateTimeOffset ExpiresAt;

	public AppAccessToken(TokenType type, string access, double expiresIn) {
		Type = type;
		Access = access;
		ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
	}

	public AppAccessToken(TokenType type, string access, DateTimeOffset expiresAt) {
		Type = type;
		Access = access;
		ExpiresAt = expiresAt;
	}

	public async Task<bool> Validate() {

		Shared.httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", Access);
		HttpResponseMessage validateResponse = await Shared.httpClient.GetAsync($"{Program.Configuration.TwitchOAuthBaseURL}/validate");

		//Stream responseStream = await validateResponse.Content.ReadAsStreamAsync();
		//JsonDocument responseDocument = await JsonDocument.ParseAsync( responseStream );

		// TODO: Properly validate the response body
		return validateResponse.StatusCode == HttpStatusCode.OK;

	}

#pragma warning disable CS1998
	public async Task Revoke() {
		throw new NotImplementedException();
	}

	public AuthenticationHeaderValue GetAuthorizationHeader() {
		if (Type == TokenType.Bearer) {
			return new("Bearer", Access);
		} else {
			throw new Exception($"Unknown token type '{Type}'");
		}
	}
}
