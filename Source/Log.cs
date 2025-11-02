using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace TwitchBot;

public static class Log {

	/// <summary>
	/// Creates a logger using the custom class with the specified category name.
	/// </summary>
	/// <param name="categoryName">The unique category name for this logger instance.</param>
	/// <returns>A logger instance.</returns>
	public static ILogger CreateLogger(string categoryName = "TwitchBot") => LoggerFactory.Create(builder => {
		builder.ClearProviders();

		builder.AddConsoleFormatter<CustomConsoleFormatter, SimpleConsoleFormatterOptions>(options => {
			options.UseUtcTimestamp = false;
			options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff zzz] ";

			options.ColorBehavior = LoggerColorBehavior.Enabled;

			// Not implemented
			options.SingleLine = true;
			options.IncludeScopes = true;
		});

		builder.AddConsole(options => options.FormatterName = "Custom");

#if DEBUG
		builder.SetMinimumLevel(LogLevel.Trace);
#else
			builder.SetMinimumLevel( LogLevel.Information );
#endif
	}).CreateLogger(categoryName);

	public sealed class CustomConsoleFormatter : ConsoleFormatter, IDisposable {
		private readonly IDisposable? optionsReloadToken;
		private SimpleConsoleFormatterOptions formatterOptions;

		public CustomConsoleFormatter(IOptionsMonitor<SimpleConsoleFormatterOptions> options) : base("Custom") =>
			(optionsReloadToken, formatterOptions) = (options.OnChange(ReloadLoggerOptions), options.CurrentValue);
		private void ReloadLoggerOptions(SimpleConsoleFormatterOptions options) => formatterOptions = options;
		public void Dispose() => optionsReloadToken?.Dispose();

		public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter) {
			string? message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
			if (message == null) return;

			WriteColor(textWriter, logEntry.LogLevel);
			WriteTimestamp(textWriter);
			WriteLogLevel(textWriter, logEntry.LogLevel);

			if (logEntry.Exception != null) {
				textWriter.Write(message);
				textWriter.Write(": ");
				textWriter.WriteLine(logEntry.Exception.Message);
			} else {
				textWriter.WriteLine(message);
			}

			if (formatterOptions.ColorBehavior != LoggerColorBehavior.Disabled) textWriter.Write("\x1B[0m");
		}

		private void WriteColor(TextWriter textWriter, LogLevel logLevel) {
			if (formatterOptions.ColorBehavior == LoggerColorBehavior.Disabled) return;

			textWriter.Write(logLevel switch {
				LogLevel.Trace => "\x1B[36;3m", // Italic & Cyan
				LogLevel.Debug => "\x1B[36;22m", // Cyan
				LogLevel.Information => "\x1B[37;22m", // White
				LogLevel.Warning => "\x1B[33;22m", // Yellow
				LogLevel.Error => "\x1B[31;22m", // Red
				LogLevel.Critical => "\x1B[31;1m", // Bold & Red
				_ => throw new ArgumentException($"Unrecognised log level '{logLevel}'")
			});
		}

		private void WriteLogLevel(TextWriter textWriter, LogLevel logLevel) {
			string logLevelString = logLevel switch {
				LogLevel.Trace => "TRACE",
				LogLevel.Debug => "DEBUG",
				LogLevel.Information => "INFO",
				LogLevel.Warning => "WARN",
				LogLevel.Error => "ERROR",
				LogLevel.Critical => "CRITICAL",
				_ => throw new ArgumentException($"Unrecognised log level '{logLevel}'")
			};

			textWriter.Write($"[{logLevelString}] ");
		}

		private void WriteTimestamp(TextWriter textWriter) {
			if (formatterOptions.TimestampFormat == null) return;

			DateTimeOffset currentDateTime = formatterOptions.UseUtcTimestamp == true ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
			textWriter.Write(currentDateTime.ToString(formatterOptions.TimestampFormat));
		}
	}

	/** Deprecated stuff **/

	[Obsolete("Use the ILogger instance")]
	private readonly static ConsoleColor originalConsoleColor = Console.ForegroundColor;

	[Obsolete("Use the ILogger instance")]
	public static void Write(string severity, string format, params object?[] objects) {
		Console.WriteLine("[{0:dd-MM-yyyy HH:mm:ss.fff zzz}] [{1}] {2}", DateTime.UtcNow, severity.ToUpper(), string.Format(format, objects));
	}

	[Conditional("DEBUG")]
	[Obsolete("Use the ILogger instance")]
	public static void Debug(string format, params object?[] objects) {
		Console.ForegroundColor = ConsoleColor.Cyan;
		Write("DEBUG", format, objects);
		Console.ForegroundColor = originalConsoleColor;
	}

	[Obsolete("Use the ILogger instance")]
	public static void Info(string format, params object?[] objects) {
		Console.ForegroundColor = ConsoleColor.White;
		Write("INFO", format, objects);
		Console.ForegroundColor = originalConsoleColor;
	}

	[Obsolete("Use the ILogger instance")]
	public static void Warn(string format, params object?[] objects) {
		Console.ForegroundColor = ConsoleColor.Yellow;
		Write("WARN", format, objects);
		Console.ForegroundColor = originalConsoleColor;
	}

	[Obsolete("Use the ILogger instance")]
	public static void Error(string format, params object?[] objects) {
		Console.ForegroundColor = ConsoleColor.Red;
		Write("ERROR", format, objects);
		Console.ForegroundColor = originalConsoleColor;
	}
}
