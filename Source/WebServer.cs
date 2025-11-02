using System;
using System.Net;
using System.Threading.Tasks;

namespace TwitchBot;

public class WebServer {
	private static readonly HttpListener httpListener = new();

	public static async Task ListenFor(string url, Func<HttpListenerContext, Task<bool>> handleRequest, string responseMessage = "Success", string method = "GET", bool wantQueryString = false) {
		Uri expectedUrl = new(url);

		httpListener.Prefixes.Add(url);

		httpListener.Start();

		while (httpListener.IsListening) {
			HttpListenerContext context = await httpListener.GetContextAsync();

			string? requestMethod = context.Request?.HttpMethod;
			string? requestPath = context.Request?.Url?.AbsolutePath;
			string? requestQuery = context.Request?.Url?.Query;

			if (requestMethod != method) {
				await context.Response.Respond(HttpStatusCode.MethodNotAllowed, $"Only available for '{method}' method.");
				continue;
			}

			if (requestPath != expectedUrl.AbsolutePath) {
				await context.Response.Respond(HttpStatusCode.NotFound, $"Requested path '{requestPath}' does not exist.");
				continue;
			}

			if (string.IsNullOrEmpty(requestQuery)) {
				await context.Response.Respond(HttpStatusCode.BadRequest, $"No query string provided.");
				continue;
			}

			if (!await handleRequest(context)) continue; // The handler is expected to respond with their own error message

			await context.Response.Respond(HttpStatusCode.OK, responseMessage);

			httpListener.Close();
		}
	}
}
