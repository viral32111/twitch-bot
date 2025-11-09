using System;
using System.Reflection;

using Microsoft.Extensions.Logging;

using MongoDB.Driver;

namespace TwitchBot.Database;

public static class Mongo {
	public static readonly string StreamsCollectionName = "Streams";

	public static readonly IMongoDatabase Database;

	static Mongo() {

		// Ensure the database configuration properties exist
		if (string.IsNullOrWhiteSpace(Program.Configuration.MongoDBDatabaseName) ||
			string.IsNullOrWhiteSpace(Program.Configuration.MongoDBServerAddress) ||
			Program.Configuration.MongoDBServerPort < 0 || Program.Configuration.MongoDBServerPort > 65536 ||
			string.IsNullOrWhiteSpace(Program.Configuration.MongoDBUserName) ||
			string.IsNullOrWhiteSpace(Program.Configuration.MongoDBUserPassword)
		) {
			Program.Logger.LogError("One or more of the database configuration properties are not set!");
			Environment.Exit(1);
			return;
		}

		// Construct the connection URL
		MongoUrl connectionUrl = new MongoUrlBuilder() {
			Server = new MongoServerAddress(Program.Configuration.MongoDBServerAddress, Program.Configuration.MongoDBServerPort),
			DirectConnection = true,

			Username = Program.Configuration.MongoDBUserName,
			Password = Program.Configuration.MongoDBUserPassword,
			DatabaseName = Program.Configuration.MongoDBDatabaseName,

			ApplicationName = Assembly.GetExecutingAssembly().GetName().Name,

			AuthenticationMechanism = "SCRAM-SHA-256",
			AuthenticationSource = "admin"
		}.ToMongoUrl();

		// Create the MongoDB client
		MongoClient mongoClient = new(connectionUrl);
		Program.Logger.LogInformation($"Initialized MongoDB client '{connectionUrl.Username}@{connectionUrl.Server}'.");

		// Get the database
		Database = mongoClient.GetDatabase(connectionUrl.DatabaseName);
		Program.Logger.LogInformation($"Got Mongo database '{connectionUrl.DatabaseName}'.");

	}
}
