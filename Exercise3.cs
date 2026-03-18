// Exercise 3 — FHIR REST Client (Full CRUD + Paging)
//
// This exercise connects to a live FHIR server running locally in Docker and
// demonstrates the four core HTTP operations:
//   Create  → POST a new resource to the server (server assigns an id)
//   Read    → GET a resource back by its id
//   Update  → PUT a modified resource back to the server
//   Delete  → DELETE a resource by its id
//
// It also shows how to search for resources and page through large result sets.
//
// The Firely SDK wraps all of these HTTP calls in typed async methods, so you
// don't have to build the URLs or parse the JSON yourself.

using System.Text.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;

public static class Exercise3
{
	// The 'async' keyword means this method can pause while waiting for network
	// responses (await) without blocking the whole program.
	// System.Threading.Tasks.Task is used here instead of just 'Task' because
	// the FHIR library defines its own class also called 'Task', which would
	// cause a naming conflict.
	public static async System.Threading.Tasks.Task Run()
	{
		// The base URL of the FHIR server running in Docker on your machine.
		var endpoint = new Uri("http://localhost:8080");

		// FhirClientSettings configures how the SDK talks to the server.
		var settings = new FhirClientSettings
		{
			// Ask the server to respond with JSON (instead of XML).
			PreferredFormat = ResourceFormat.Json,

			// Strict mode: if you pass a search parameter the server doesn't
			// recognise, it should return an error instead of silently ignoring it.
			PreferredParameterHandling = SearchParameterHandling.Strict,

			// Skip the version handshake; useful when the server version doesn't
			// exactly match what the SDK expects.
			VerifyFhirVersion = false,

			// Give up after 30 seconds if the server doesn't respond.
			// 30_000 is 30,000 milliseconds — the underscore is just a readability
			// separator, like a comma in a large number.
			Timeout = 30_000,

			// After Create/Update, ask the server to send the full saved resource
			// back in the response body (instead of just a 201 Created with no body).
			ReturnPreference = ReturnPreference.Representation
		};

		// Create the FHIR client. All REST calls go through this object.
		var client = new FhirClient(endpoint, settings);

		try
		{
			Console.WriteLine($"FHIR REST client initialized for {endpoint}");
			Console.WriteLine("Requesting server metadata...");

			// CapabilityStatementAsync performs GET /metadata on the server.
			// The response is a CapabilityStatement resource that describes what
			// the server supports (resource types, operations, search parameters, etc.)
			var capability = await client.CapabilityStatementAsync();
			Console.WriteLine($"Connected to: {capability.Software?.Name} {capability.Software?.Version}");

			// --- SEARCH ---
			// SearchAsync<Patient> performs GET /Patient?birthdate=1987
			// The criteria array is a list of "parameter=value" strings.
			// Results come back wrapped in a Bundle, which is a FHIR container
			// for a collection of resources.
			string[] criteria = { "birthdate=1987" };
			Console.WriteLine("Running Patient search using criteria array: birthdate=1987...");
			var birthdateSearch = await client.SearchAsync<Patient>(criteria);
			DisplayPatientBundle(birthdateSearch, "Page 1");

			// --- PAGING ---
			// FHIR servers return large result sets in pages (like search engine pages).
			// Each Bundle contains links to the next/previous page.
			// ContinueAsync follows those links automatically.
			if (birthdateSearch is not null)
			{
				Console.Write("Max pages to display when looping (default 5): ");
				var maxPagesInput = Console.ReadLine();
				// int.TryParse safely converts the user's text to a number.
				// If it fails or the user types nothing, use 5 as the default.
				var maxPagesToDisplay = int.TryParse(maxPagesInput, out var parsedMaxPages) && parsedMaxPages > 0
					? parsedMaxPages
					: 5;

				Console.WriteLine("Requesting next page...");
				// PageDirection.Next follows the "next" link in the Bundle.
				var nextPage = await client.ContinueAsync(birthdateSearch, PageDirection.Next);
				if (nextPage is null)
				{
					Console.WriteLine("No next page available.");
				}
				else
				{
					DisplayPatientBundle(nextPage, "Page 2");

					Console.Write("Loop through all remaining pages? (y/N): ");
					var pageInput = Console.ReadLine();
					// StringComparison.OrdinalIgnoreCase makes the check case-insensitive
					// so "Y", "y", "YES", "yes" all count as yes.
					var loopAllPages = string.Equals(pageInput?.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(pageInput?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

					if (loopAllPages)
					{
						var pageNumber = 2;
						var currentPage = nextPage;

						// Keep fetching the next page until either ContinueAsync returns null
						// (no more pages) or we hit the user-defined limit.
						while (currentPage is not null && pageNumber < maxPagesToDisplay)
						{
							var followingPage = await client.ContinueAsync(currentPage, PageDirection.Next);
							if (followingPage is null)
							{
								Console.WriteLine("Reached final page.");
								break;
							}

							pageNumber++;
							DisplayPatientBundle(followingPage, $"Page {pageNumber}");
							currentPage = followingPage;
						}

						if (pageNumber >= maxPagesToDisplay)
						{
							Console.WriteLine($"Stopped paging after {maxPagesToDisplay} pages to avoid excessive output.");
						}
					}
				}
			}

			// --- CREATE ---
			// Reuse the Patient factory from Exercise 1 so we have something to work with.
			Console.WriteLine("Creating the Exercise 1 Patient on the server...");
			var patientToCreate = Exercise1.CreatePatient();

			try
			{
				// CreateAsync performs POST /Patient with the resource as the request body.
				// Because we set ReturnPreference = Representation above, the server sends
				// back the full saved Patient, including the server-assigned id.
				var createdResource = await client.CreateAsync(patientToCreate);

				// 'is Patient createdPatient' is a pattern match: it checks whether the
				// returned resource is a Patient and, if so, binds it to 'createdPatient'.
				if (createdResource is Patient createdPatient)
				{
					Console.WriteLine($"Patient created successfully. Technical id: {createdPatient.Id}");

					// --- CREATE OBSERVATION LINKED TO PATIENT ---
					// Load the sample Observation JSON from disk (same file as Exercise 2).
					Console.WriteLine("Creating Observation linked to that Patient...");
					var observationPath = Path.Combine(Directory.GetCurrentDirectory(), "observation.json");
					var jsonOptions = new JsonSerializerOptions().ForFhir();

					using var stream = new FileStream(observationPath, FileMode.Open, FileAccess.Read, FileShare.Read);
					var observationToCreate = JsonSerializer.Deserialize<Observation>(stream, jsonOptions);

					if (observationToCreate is null)
					{
						Console.WriteLine("Could not deserialize observation.json.");
					}
					else
					{
						// Set the Observation's subject to point at the newly created Patient.
						// ResourceReference is FHIR's way of linking one resource to another.
						// The reference string looks like "Patient/abc123".
						observationToCreate.Subject = new ResourceReference(createdPatient.TypeName + "/" + createdPatient.Id);

						try
						{
							var createdObservationResource = await client.CreateAsync(observationToCreate);
							if (createdObservationResource is Observation createdObservation)
							{
								Console.WriteLine($"Observation created successfully. Technical id: {createdObservation.Id}");
								Console.WriteLine($"Observation subject reference: {createdObservation.Subject?.Reference}");

								if (string.IsNullOrWhiteSpace(createdObservation.Id) || string.IsNullOrWhiteSpace(createdPatient.Id))
								{
									Console.WriteLine("Create response did not contain required technical ids.");
									return;
								}

								// Build the relative URLs we'll use for read/update/delete.
								// FHIR URLs follow the pattern ResourceType/id.
								var observationLocation = $"Observation/{createdObservation.Id}";
								var patientLocation = $"Patient/{createdPatient.Id}";

								// --- READ ---
								// ReadAsync<T> performs GET /Observation/{id} and deserializes
								// the response into a typed Observation object.
								Console.WriteLine("Reading Observation back from server...");
								var readObs = await client.ReadAsync<Observation>(observationLocation);
								if (readObs is null)
								{
									Console.WriteLine("Read returned no Observation.");
									return;
								}

								Console.WriteLine($"Read Observation {readObs.Id} with status {readObs.Status}");

								// --- UPDATE ---
								// Change a field locally, then push the whole resource back with PUT.
								// The server will create a new version in its history.
								Console.WriteLine("Updating Observation status to amended...");
								readObs.Status = ObservationStatus.Amended;
								var updatedObs = await client.UpdateAsync(readObs);
								if (updatedObs is null)
								{
									Console.WriteLine("Update returned no Observation.");
									return;
								}

								Console.WriteLine($"Observation updated. Technical id: {updatedObs.Id}, status: {updatedObs.Status}");

								// --- DELETE ---
								Console.Write("Delete created Observation and Patient now? (y/N): ");
								var deleteInput = Console.ReadLine();
								var shouldDelete = string.Equals(deleteInput?.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
									string.Equals(deleteInput?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

								if (shouldDelete)
								{
									// DeleteAsync performs DELETE /Observation/{id}.
									// After deletion the resource still exists in the server's history
									// but is no longer the "current" version — it becomes "deleted".
									Console.WriteLine("Deleting Observation and Patient...");
									await client.DeleteAsync(observationLocation);
									await client.DeleteAsync(patientLocation);
									Console.WriteLine("Observation and Patient deleted successfully.");

									// Demonstrate what happens when you try to read a deleted resource.
									// A server should return 410 Gone (it existed but was deleted) or
									// 404 Not Found. We catch the FhirOperationException and check the
									// HTTP status code to confirm the expected behaviour.
									Console.WriteLine("Trying to read deleted Observation...");
									try
									{
										await client.ReadAsync<Observation>(observationLocation);
										Console.WriteLine("Unexpected: deleted Observation could still be read.");
									}
									catch (FhirOperationException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound || ex.Status == System.Net.HttpStatusCode.Gone)
									{
										// 'when' is a catch filter — only this specific exception type with
										// a 404 or 410 status falls into this block.
										Console.WriteLine($"Read-after-delete behaved as expected: {(int)ex.Status} ({ex.Status}).");
									}
								}
								else
								{
									Console.WriteLine("Keeping created resources on the server.");
									Console.WriteLine($"  Patient: {patientLocation}");
									Console.WriteLine($"  Observation: {observationLocation}");
								}
							}
							else
							{
								Console.WriteLine("Observation create succeeded, but response was not an Observation.");
							}
						}
						catch (FhirOperationException ex)
						{
							// FhirOperationException is thrown when the server returns an HTTP error
							// (4xx or 5xx). The Status property holds the HTTP status code.
							Console.WriteLine($"Observation create failed with status {(int)ex.Status} ({ex.Status}).");
							Console.WriteLine($"Details: {ex.Message}");
						}
						catch (HttpRequestException ex)
						{
							// HttpRequestException is thrown for network-level failures
							// (e.g. server is down, DNS lookup failed).
							Console.WriteLine("Network error during observation create request.");
							Console.WriteLine($"Details: {ex.Message}");
						}
					}
				}
				else
				{
					Console.WriteLine("Create succeeded, but response did not contain a Patient resource.");
				}
			}
			catch (FhirOperationException ex)
			{
				Console.WriteLine($"Create request failed with status {(int)ex.Status} ({ex.Status}).");
				Console.WriteLine($"Details: {ex.Message}");
			}
			catch (HttpRequestException ex)
			{
				Console.WriteLine("Network error during create request.");
				Console.WriteLine($"Details: {ex.Message}");
			}

		}
		catch (HttpRequestException ex)
		{
			// Top-level catch for the CapabilityStatement call — if we can't even
			// reach the server there's no point continuing further.
			Console.WriteLine("Connection error: Unable to reach FHIR server at localhost:8080");
			Console.WriteLine($"Details: {ex.Message}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error: {ex.Message}");
		}
	}

	// Helper method to pretty-print the contents of a Bundle (a page of search results).
	// 'Bundle?' — the question mark means the bundle parameter is nullable (can be null).
	private static void DisplayPatientBundle(Bundle? bundle, string label)
	{
		if (bundle is null)
		{
			Console.WriteLine($"{label}: no bundle returned.");
			return;
		}

		// bundle.Total is the overall count across ALL pages, not just this page.
		// It can be null if the server chose not to include it.
		if (bundle.Total is not null)
		{
			Console.WriteLine($"{label}: total reported by server = {bundle.Total}");
		}
		else
		{
			Console.WriteLine($"{label}: total not provided by server.");
		}

		// bundle.Entry is the list of resources on this page.
		// The ?? operator returns the right-hand side if the left is null,
		// so we use an empty list as a fallback to avoid null reference errors below.
		var entries = bundle.Entry ?? new List<Bundle.EntryComponent>();
		Console.WriteLine($"{label}: returned {entries.Count} resources in this page.");

		foreach (var entry in entries)
		{
			// Each entry in the Bundle has a Resource property. We check whether it
			// is a Patient and extract it in one step with the 'is' pattern match.
			if (entry.Resource is Patient patient)
			{
				// Prefer the "usual" name if one is recorded; fall back to the first name.
				var name = patient.Name?.FirstOrDefault(n => n.Use == HumanName.NameUse.Usual)
					?? patient.Name?.FirstOrDefault();

				// Join the given name parts (there can be more than one) with a space,
				// then append the family name.
				var fullName = name is null
					? "(no name)"
					: $"{string.Join(" ", name.Given)} {name.Family}".Trim();

				Console.WriteLine($"  Patient {patient.Id}: {fullName}");
			}
		}
	}
}
