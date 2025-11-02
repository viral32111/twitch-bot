using System;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;

using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;

using TwitchBot.Database;
using Microsoft.Extensions.Logging;

namespace TwitchBot.Twitch;

public class Stream {

	// The name of the MongoDB collection
	private static readonly string MongoCollectionName = "Streams";

	// Regular expression for parsing the duration strings from the Twitch API
	private static readonly Regex DurationPattern = new(@"^(?:(?'hours'\d+)h)?(?:(?'minutes'\d+)m)?(?:(?'seconds'\d+)s)?$");

	// The identifier of this stream
	[BsonId]
	public readonly long Identifier;

	// The channel this stream belongs to
	// NOTE: Not stored in MongoDB, see the property below
	[BsonIgnore]
	public Channel Channel {
		private set { ChannelIdentifier = value.Identifier; }
		get { return Channel; }
	}

	// The identifier of the channel this stream belongs to
#pragma warning disable IDE0052
	[BsonElement("channelId", Order = 1)]
	private long ChannelIdentifier;
#pragma warning restore IDE0052

	// Date & time the stream started, stored as a DateTime in MongoDB
	[BsonElement("startedAt", Order = 2), BsonDateTimeOptions(Representation = MongoDB.Bson.BsonType.DateTime)]
	public readonly DateTime StartedAt;

	// Duration of the stream, stored as an Int32 (representing seconds) in MongoDB
	// NOTE: This is not read-only, as it can change if the stream is live
	[BsonElement("duration", Order = 3), BsonTimeSpanOptions(MongoDB.Bson.BsonType.Int32, MongoDB.Bson.Serialization.Options.TimeSpanUnits.Seconds)]
	public TimeSpan Duration;

	// Construct from data returned by Twitch API
	public Stream(JsonObject apiData, Channel? channel = null) {

		// Extract required properties from the API data
		if (!apiData.TryGetPropertyValue("id", out JsonNode? apiDataId) || apiDataId == null) throw new Exception("API data does not contain a property for identifier");
		if (!apiData.TryGetPropertyValue("created_at", out JsonNode? apiDataCreatedAt) || apiDataCreatedAt == null) throw new Exception("API data does not contain a property for created at");
		if (!apiData.TryGetPropertyValue("duration", out JsonNode? apiDataDuration) || apiDataDuration == null) throw new Exception("API data does not contain a property for duration");

		// Parse identifier as an integer
		if (!long.TryParse(apiDataId.GetValue<string>(), out Identifier)) throw new Exception("Failed to parse stream identifier");

		// Parse started at as a date & time in UTC
		if (!DateTime.TryParse(apiDataCreatedAt.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out StartedAt)) throw new Exception("Failed to parse date & time string");

		// Parse duration using the regular expression
		Match durationMatch = DurationPattern.Match(apiDataDuration.GetValue<string>());
		if (!durationMatch.Success) throw new Exception("Failed to parse human-readable duration");
		if (!int.TryParse(durationMatch.Groups["hours"].ValueOr("0"), out int durationHours)) throw new Exception("Failed to parse hours component in human-readable duration");
		if (!int.TryParse(durationMatch.Groups["minutes"].ValueOr("0"), out int durationMinutes)) throw new Exception("Failed to parse minutes component in human-readable duration");
		if (!int.TryParse(durationMatch.Groups["seconds"].ValueOr("0"), out int durationSeconds)) throw new Exception("Failed to parse seconds component in human-readable duration");
		Duration = new(durationHours, durationMinutes, durationSeconds);

		// If the channel wasn't given...
		if (channel == null) {

			// Extract property from API data & convert to integer
			if (!apiData.TryGetPropertyValue("user_id", out JsonNode? apiDataUserId) || apiDataUserId == null) throw new Exception("API data does not contain a property for user id");
			if (!long.TryParse(apiDataUserId.GetValue<string>(), out long channelIdentifier)) throw new Exception("Failed to parse channel identifier");

			// Try use the channel from state
			if (!State.TryGetChannel(channelIdentifier, out channel) || channel == null) throw new Exception($"Channel does not exist in state");

		}

		// Set the channel property
		Channel = channel;

	}

	// Construct from data returned by MongoDB
	[BsonConstructor(["Identifier", "ChannelIdentifier", "StartedAt", "Duration"])]
	public Stream(long identifier, long channelIdentifier, DateTime startedAt, TimeSpan duration) {
		Identifier = identifier;
		ChannelIdentifier = channelIdentifier;
		StartedAt = startedAt;
		Duration = duration;

		// Try use the channel from state
		/* if ( !State.TryGetChannel( channelIdentifier, out Channel? channel ) || channel == null ) throw new Exception( $"Channel does not exist in state" );
		Channel = channel; */
	}

	// Convert stream to human-readable representation
	public override string ToString() => $"{Identifier} @ {StartedAt} ({Duration})";

	// Add this stream to MongoDB
	public async Task DatabaseInsert() {
		IMongoCollection<Stream> mongoCollection = Mongo.Database.GetCollection<Stream>(MongoCollectionName);
		await mongoCollection.InsertOneAsync(this);
	}

	// Update this stream in MongoDB, can also insert if it doesn't exist
	public async Task DatabaseUpdate(bool insertIfMissing = false) {
		IMongoCollection<Stream> mongoCollection = Mongo.Database.GetCollection<Stream>(MongoCollectionName);
		await mongoCollection.ReplaceOneAsync(Builders<Stream>.Filter.Eq(stream => stream.Identifier, Identifier), this, new ReplaceOptions() {
			IsUpsert = insertIfMissing
		});
	}

	// Fetch all streams from MongoDB, can be for a specific channel
	public static async Task<List<Stream>> DatabaseFind(Channel? forChannel = null, int limit = 0) {
		IMongoCollection<Stream> mongoCollection = Mongo.Database.GetCollection<Stream>(MongoCollectionName);
		FilterDefinition<Stream> findFilter = forChannel != null ? Builders<Stream>.Filter.Eq(stream => stream.ChannelIdentifier, forChannel.Identifier) : FilterDefinition<Stream>.Empty;

		return await mongoCollection.Find(findFilter).Limit(limit).ToListAsync();
	}

}
