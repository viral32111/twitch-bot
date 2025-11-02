using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace TwitchBot;

public class Storage {

	// Will hold the file path for saving the JSON structure to
	private readonly string filePath;

	// Will hold the parsed JSON structure
	private readonly JsonObject rootStructure;

	// The options for serializing & deserializing JSON objects
	public static readonly JsonSerializerOptions serializerOptions = new() {
		PropertyNamingPolicy = null, // Keep property names as they are
		PropertyNameCaseInsensitive = false,
		ReadCommentHandling = JsonCommentHandling.Skip, // Ignore any human comments
		AllowTrailingCommas = true, // Ignore minor human mistakes
		WriteIndented = true // Make human editing easier
	};

	// Constructor takes a JSON object
	public Storage(string path, JsonObject structure) {
		filePath = path;
		rootStructure = structure;
	}

	// Parses a JSON structure from a file
	public static Storage ReadFile(string filePath) {

		// Open the specified file for reading...
		using (FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None)) {

			// Parse the JSON structure within the file
			JsonObject? jsonStructure = JsonSerializer.Deserialize<JsonObject>(fileStream, serializerOptions);

			// Error if parsing the JSON structure failed
			if (jsonStructure == null) {
				throw new Exception($"Failed to parse JSON structure from file '{filePath}'");
			}

			// Return the JSON structure so it can be used
			return new Storage(filePath, jsonStructure);

		}

	}

	// Creates a new file using the provided JSON structure
	public static Storage CreateFile(string filePath, JsonObject jsonStructure) {

		Storage storage = new(filePath, jsonStructure);
		storage.Save();

		return storage;

	}

	// Saves the JSON structure to a file
	public void Save() {

		// Create (or open) the specified file for writing...
		using (FileStream fileStream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None)) {

			// Write the provided JSON structure to the file
			JsonSerializer.Serialize(fileStream, rootStructure, serializerOptions);

		}

	}

	// Retrieves a type of value from the JSON structure
	// NOTE: This throws an error if the property does not exist, which is intentional behaviour
	public T Get<T>(string path) {

		// Retrieve the property value at the provided path
		JsonNode? propertyValue = GetProperty(path);

		// Error if the value does not exist
		if (propertyValue == null) throw new Exception($"Could not find property '{path}' in structure");

		if (typeof(T) == typeof(string[])) {
			return ( T ) ( object ) propertyValue.AsArray().ToArray();
		} else if (typeof(T).IsArray) {
			Console.WriteLine($"{path} | {typeof(T)} | {typeof(T).IsArray}");
			return ( T ) ( object ) propertyValue.AsArray().ToArrayOf<T>();
		} else if (typeof(T) == typeof(JsonObject)) {
			return ( T ) ( object ) propertyValue.AsObject();
		} else {
			return propertyValue.AsValue().GetValue<T>();
		}

		// Return the value as a string
		/*if ( typeof( T ) == typeof( string ) ) {
			return ( T ) ( object ) propertyValue.ToString();

			// Return the type as a integer
		} else if ( typeof( T ) == typeof( int ) ) {
			return ( T ) ( object ) ( int ) propertyValue.AsValue();

			// Return the type as a long
		} else if ( typeof( T ) == typeof( long ) ) {
			return ( T ) ( object ) ( long ) propertyValue.AsValue();

			// Return the type as a JSON object
		} else if ( typeof( T ) == typeof( JsonObject ) ) {
			return ( T ) ( object ) propertyValue.AsObject();

			// Return the value as an array
		} else if ( typeof( T ).IsArray ) {
			Console.WriteLine( "blyat" );
			return ( T ) ( object ) propertyValue.AsArray().ToArray<T>();

			// Error if the type is unhandled
			// TODO: Fallback to cast here as some types only need that
		} else {
			throw new Exception( "Unhandled property data type" );
		}*/

	}

	// Retrieves a nested property
	public JsonNode? GetProperty(string path) {

		// Split the nested path up into individual property names
		List<string> propertyNames = path.Split('.').ToList();

		// Contains the previously found JSON object, starts with the root structure
		JsonObject previousJsonObject = rootStructure;

		// Repeat until there are no property names left...
		while (propertyNames.Count > 0) {

			// Store the most recent property name
			string propertyName = propertyNames[0];

			// Attempt to retreive the property from the previous JSON object
			if (!previousJsonObject.TryGetPropertyValue(propertyName, out JsonNode? propertyValue)) {
				return null;
			}

			// Return nothing if the retreived property value is null
			if (propertyValue == null) return null;

			// Return this property as a value if this is the last iteration
			if (propertyNames.Count == 1) {
				return propertyValue;

				// Otherwise, store this property as a JSON object for the next iteration
			} else {
				previousJsonObject = propertyValue.AsObject();
			}

			// Remove this property name from the list
			propertyNames.RemoveAt(0);

		}

		// Return nothing if no value was returned
		return null;

	}

	// Sets a non-nested property to the provided value
	public void SetProperty(string name, JsonNode? value) {
		rootStructure[name] = value;
	}

	// Checks if a nested property exists
	public bool HasProperty(string path) {
		return GetProperty(path) != null;
	}

}
