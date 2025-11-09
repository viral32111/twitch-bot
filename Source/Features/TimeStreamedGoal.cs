using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using TwitchBot.Twitch;

namespace TwitchBot.Features;

public class TimeStreamedGoal {

	// Holds the data about goals for each channel
	private static readonly Dictionary<long, TimeStreamedGoal> channelGoals = new();

	// Metadata about this goal
	public readonly int TargetHours;
	public readonly int DailyTargetHours;
	public readonly DateTime StartDateTime;
	public readonly string FutureMessageTemplate;
	public readonly string MonthProgressMessageTemplate;
	public readonly string DayProgressMessageTemplate;
	public readonly string CompletedMessageTemplate;
	public readonly string AnnounceMessageTemplate;

	// Initialize the above metadata
	public TimeStreamedGoal(int targetHours, int dailyTargetHours, DateTime startDateTime, string futureMessageTemplate, string monthProgressMessageTemplate, string dayProgressMessageTemplate, string completedMessageTemplate, string announceMessageTemplate) {
		TargetHours = targetHours;
		DailyTargetHours = dailyTargetHours;
		StartDateTime = startDateTime;

		FutureMessageTemplate = futureMessageTemplate;
		MonthProgressMessageTemplate = monthProgressMessageTemplate;
		DayProgressMessageTemplate = dayProgressMessageTemplate;
		CompletedMessageTemplate = completedMessageTemplate;
		AnnounceMessageTemplate = announceMessageTemplate;
	}

	[ModuleInitializer] // Makes this method run when the program starts
	public static void Setup() {

		// TODO: Cannot use configuration value here yet as it is not initialized at this point
		// long channelId = 675961583; // viral32111_
		long channelId = 127154290; // Program.Configuration.TwitchPrimaryChannelIdentifier;

		// Setup the channel goal for RawrelTV
		channelGoals.Add(channelId, new(
			targetHours: 200,
			dailyTargetHours: 6,
			startDateTime: new(2025, 11, 1, 0, 0, 0),
			futureMessageTemplate: "My goal is to stream for at least {0} hours throughout November! Stay tuned for updates!",
			monthProgressMessageTemplate: "I have streamed {0} this month. I'm trying to stream for at least {1} hours, lets see how far we get!",
			dayProgressMessageTemplate: "I have streamed {0} this month. I'm trying to stream for at least {1} hours, lets see how far we get! I've got another {4} left to stream today.",
			completedMessageTemplate: "I have streamed {0} this month. I've hit my goal of {1} hours, thank you all!",
			announceMessageTemplate: "I have reached my goal of streaming for {0} hours throughout November!"
		));
		Program.Logger.LogInformation($"Added time streamed goal for channel '{channelId}'.");

		// Register the chat command
		ChatCommand.Register(GoalProgressCommand);

	}

	// Formats a TimeSpan into a human-readable string (e.g., "5h 30m 15s")
	private static string FormatTimeSpan(TimeSpan timeSpan) {
		var parts = new List<string>();

		int totalHours = ( int ) timeSpan.TotalHours;
		if (totalHours > 0) parts.Add($"{totalHours} hours");
		if (timeSpan.Minutes > 0) parts.Add($"{timeSpan.Minutes} minutes");
		if (timeSpan.Seconds > 0 || parts.Count == 0) parts.Add($"{timeSpan.Seconds} seconds");

		return string.Join(", ", parts);
	}

	// Calculates how much time has been streamed today
	private static TimeSpan GetTodayStreamTime(Stream[] streams) {
		DateTime todayStart = DateTime.UtcNow.Date; // Midnight today (UTC)
		DateTime todayEnd = todayStart.AddDays(1); // Midnight tomorrow (UTC)

		return streams.Aggregate(TimeSpan.Zero, (total, stream) => {
			DateTimeOffset streamStart = stream.StartedAt;
			DateTimeOffset streamEnd = stream.StartedAt + stream.Duration;

			// Skip streams that don't overlap with today
			if (streamEnd <= todayStart || streamStart >= todayEnd) return total;

			// Clamp stream times to today's boundaries
			DateTimeOffset effectiveStart = streamStart < todayStart ? todayStart : streamStart;
			DateTimeOffset effectiveEnd = streamEnd > todayEnd ? todayEnd : streamEnd;

			return total + (effectiveEnd - effectiveStart);
		});
	}

	// Chat command to check goal progress
	[ChatCommand("goal", ["time"])]
	public static async Task GoalProgressCommand(Message message) {

		// Get this channel's goal, if they have one
		if (!channelGoals.TryGetValue(message.Author.Channel.Identifier, out TimeStreamedGoal? goal)) {
			Program.Logger.LogWarning($"No time streamed goal exists for channel '{message.Author.Channel.Name}' ({message.Author.Channel.Identifier})");
			await message.Reply("This channel has no time streamed goal.");
			return;
		}

		// Is the goal's start date in the future?
		if (DateTime.UtcNow < goal.StartDateTime) {
			await message.Reply(string.Format(goal.FutureMessageTemplate, goal.TargetHours));
			return;
		}

		// Get the goal progress
		Stream[] streams = await message.Author.Channel.FetchStreams();
		TimeSpan totalTimeStreamed = TotalStreamDuration(streams, goal.StartDateTime);
		TimeSpan todayRemaining = TimeSpan.FromHours(goal.DailyTargetHours) - GetTodayStreamTime(streams);

		// Has the channel completed their goal?
		if (totalTimeStreamed.TotalHours >= goal.TargetHours) {
			await message.Reply(string.Format(goal.CompletedMessageTemplate, FormatTimeSpan(totalTimeStreamed), goal.TargetHours));

			// The channel has not completed their goal yet, and there is remaining time to stream today
		} else if (todayRemaining.TotalSeconds > 0) {
			await message.Reply(string.Format(goal.DayProgressMessageTemplate, FormatTimeSpan(totalTimeStreamed), goal.TargetHours, FormatTimeSpan(todayRemaining)));

			// The channel has not completed their goal yet
		} else {
			await message.Reply(string.Format(goal.MonthProgressMessageTemplate, FormatTimeSpan(totalTimeStreamed), goal.TargetHours));
		}

	}

	// Posts a message in the channel's chat when they hit their goal
	// NOTE: This method is meant to be called often at an interval (e.g. every 5 minutes) until the goal is completed
	public static async Task<bool> AnnounceGoalCompletion(Channel channel) {

		// Fail if this channel does not have a goal
		if (!channelGoals.TryGetValue(channel.Identifier, out TimeStreamedGoal? goal)) throw new Exception("Channel has no time streamed goal");

		// Get the goal progress
		// double totalHoursStreamed = await GetGoalProgress(channel, goal);
		TimeSpan totalTimeStreamed = await GetGoalProgress(channel, goal);

		// Has the channel completed their goal?
		if (totalTimeStreamed.TotalHours >= goal.TargetHours) {
			await channel.SendMessage(string.Format(goal.AnnounceMessageTemplate, goal.TargetHours));
			return true;

			// The channel has not completed their goal yet
		} else return false;

	}

	// Gets the progress of the current goal
	public static async Task<TimeSpan> GetGoalProgress(Channel channel, TimeStreamedGoal goal) {
		Stream[] streams = await channel.FetchStreams();

		// return Math.Floor(TotalStreamDuration(streams, goal.StartDate).TotalHours);
		return TotalStreamDuration(streams, goal.StartDateTime);
	}

	// Totals the duration of each stream after a goal's start date
	public static TimeSpan TotalStreamDuration(Stream[] streams, DateTimeOffset startDateTime) {
		return streams.Aggregate(new TimeSpan(0, 0, 0), (cumulativeDuration, stream) => {
			DateTimeOffset streamEndTime = stream.StartedAt + stream.Duration;
			if (streamEndTime <= startDateTime) return cumulativeDuration; // Skip streams that ended before the goal started

			// If stream started before goal, only count time from startDateTime onward
			DateTimeOffset effectiveStart = stream.StartedAt >= startDateTime ? stream.StartedAt : startDateTime;
			cumulativeDuration += streamEndTime - effectiveStart;

			return cumulativeDuration;
		});

	}

}
