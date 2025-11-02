using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using TwitchBot.Twitch;

namespace TwitchBot.Features;

public class DailyStreamStreak {

	// List of all streams in a streak
	private readonly Stream[] Streams;

	// Initializes the above property
	public DailyStreamStreak(List<Stream> streams) {
		Streams = streams.ToArray();
	}

	// Gets the date & time the earliest stream started
	public DateTimeOffset GetStartDate() => Streams[^1].StartedAt;

	// Gets the days elapsed since earliest stream
	public int GetDuration() => ( int ) Math.Ceiling((DateTimeOffset.UtcNow - GetStartDate()).TotalDays);

	// Gets the number of streams in this streak
	public int GetStreamCount() => Streams.Length;

	// Gets the total duration of all streams in the streak
	public TimeSpan GetStreamDuration() => Streams.Aggregate(new TimeSpan(0, 0, 0), (cumulativeDuration, stream) => cumulativeDuration += stream.Duration);

	[ModuleInitializer]
	public static void Setup() {

		// Register the chat command
		ChatCommand.Register(StreakProgressCommand);

	}

	// Chat command to check stream progress
	[ChatCommand("streak")]
	public static async Task StreakProgressCommand(Message message) {

		// Fetch a list of this channel's streams
		Stream[] streams = await message.Author.Channel.FetchStreams();

		// Will hold the streams in the streak & when the previous stream finished
		List<Stream> streamsInStreak = new();
		DateTimeOffset? previousStreamFinishedAt = DateTimeOffset.UtcNow; // Start from now

		// Add all the daily back-to-back streams to the list
		foreach (Stream stream in streams) {
			if (previousStreamFinishedAt.Value.Subtract(stream.StartedAt).TotalDays > 1.5) break;
			streamsInStreak.Add(stream);
			previousStreamFinishedAt = stream.StartedAt.Add(stream.Duration);
		}

		// Create a streak from those streams
		DailyStreamStreak streak = new(streamsInStreak);

		// Send back an appropriate message
		if (streak.GetStreamCount() > 0) {
			await message.Reply($"I have been live everyday for the last {streak.GetDuration()} day(s), with a total of {Math.Floor(streak.GetStreamDuration().TotalHours)} hour(s) across {streak.GetStreamCount()} stream(s)!");
		} else {
			await message.Reply("This channel has no daily streaming streak yet.");
		}

	}

}
