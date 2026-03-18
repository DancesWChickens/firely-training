// ─────────────────────────────────────────────────────────────────────────────
// Exercise2.cs  –  Deserializing a FHIR resource from a JSON file
//
// Goal: learn how to read a FHIR JSON file from disk and turn it into a typed
// C# object using the Firely SDK's System.Text.Json integration.
//
// Key concepts:
//   - Deserialization : converting text/JSON → a C# object
//   - Serialization   : converting a C# object → text/JSON (we round-trip here)
//   - ForFhir()       : extension method that configures JsonSerializerOptions
//                       to handle FHIR-specific quirks (resourceType field, etc.)
//   - WriteIndented   : pretty-prints the JSON with line breaks and indentation
//   - FileStream      : opens a file on disk for reading without loading the
//                       whole file into memory at once
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

public static class Exercise2
{
	public static void Run()
	{
		// Build the full path to observation.json in the app's working directory
		var filePath = Path.Combine(Directory.GetCurrentDirectory(), "observation.json");

		// Configure the JSON serializer to understand FHIR's JSON format
		var options = new JsonSerializerOptions().ForFhir();
		options.WriteIndented = true; // makes the output human-readable

		// "using" ensures the file is closed automatically when we're done
		using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

		// Deserialize: read the JSON and produce a typed Observation object
		var observation = JsonSerializer.Deserialize<Observation>(fileStream, options);

		if (observation is null)
		{
			Console.WriteLine("Could not deserialize observation.json.");
			return;
		}

		// Serialize back to JSON and print – proves the round-trip works
		// We cast to Resource (base type) so the serializer includes "resourceType"
		var json = JsonSerializer.Serialize<Resource>(observation, options);
		Console.WriteLine("Loaded and pretty-serialized observation.json:");
		Console.WriteLine(json);
	}
}
