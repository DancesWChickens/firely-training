// Exercise 4 — CSV to FHIR Pipeline
//
// This exercise reads a CSV file of synthetic lab results, maps each row to FHIR
// Patient and Observation resources, displays a summary (or detailed table) in
// the console, then uploads everything to the local FHIR server with idempotency:
// re-running the exercise won't create duplicates.
//
// Pipeline at a glance:
//   1. Read CSV with CsvHelper → List<CSVModel>
//   2. Map records → Patient dictionary + Observation list (via Mapper.cs)
//   3. Display: compact summary table or detailed per-patient blood-value tables
//   4. Upload: conditional create for patients, then remap observation subjects,
//      then conditional create for observations
//   5. Call $everything on the first uploaded patient to show all linked resources
//   6. Optionally show before/after counts of measure-related resources

using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using CsvHelper;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Rest;

public static class Exercise4
{
    // The namespace URIs for our business identifiers.
    // These are not real URLs — they're treated as opaque strings by FHIR servers.
    // Using a URN (urn:) prefix avoids any confusion with real web addresses.
    private const string IdentifierSystem = "urn:training:firely:patient-id";
    private const string ObservationIdentifierSystem = "urn:training:firely:observation-id";

    public static async System.Threading.Tasks.Task Run()
    {
        // Ask two upfront yes/no questions to control output verbosity and snapshot scope.
        var detailedOutput = AskYesNo("Show detailed output? (y/N): ");
        var includeMeasureTrainingData = AskYesNo("Include measure training data in server snapshot? (y/N): ");

        // ── 1. Read CSV ──────────────────────────────────────────────────────
        // StreamReader opens the CSV file as plain text.
        // CultureInfo.InvariantCulture ensures numbers are parsed with '.' as the
        // decimal separator regardless of the OS locale settings.
        TextReader reader = new StreamReader("sample-data.csv");
        var csvReader = new CsvReader(reader, CultureInfo.InvariantCulture);

        // GetRecords<CSVModel>() maps each row to a CSVModel object using the
        // [Name(...)] attributes defined in CSVModel.cs.
        // ToList() forces immediate evaluation and closes the streaming read.
        var records = csvReader.GetRecords<CSVModel>().ToList();

        // ── 2. Map to FHIR resources ─────────────────────────────────────────
        // DistinctBy ensures we create one Patient per unique patient id,
        // even if that patient has multiple rows in the CSV (multiple visits).
        // ToDictionary stores them keyed by Id so we can look them up later.
        var patients = records
            .DistinctBy(r => r.PatientId)
            .Select(Mapper.MapPatient)
            .ToDictionary(p => p.Id!);

        // SelectMany flattens the 3 Observations returned per row into one list.
        var observations = records
            .SelectMany(Mapper.MapObservations)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("=== Mapping Summary ===");
        Console.WriteLine($"CSV rows: {records.Count}");
        Console.WriteLine($"FHIR Patients: {patients.Count}");
        Console.WriteLine($"FHIR Observations: {observations.Count}");

        // ── 3. Display per-patient blood-value table ─────────────────────────
        const string wbcCode = "6690-2";
        const string rbcCode = "789-8";
        const string hbCode  = "718-7";

        // Group the observations by the patient reference string ("Patient/xxx")
        // so we can quickly look up all observations for a given patient below.
        var obsByPatient = observations
            .GroupBy(o => o.Subject!.Reference!)
            .ToDictionary(g => g.Key);

        if (detailedOutput)
        {
            Console.WriteLine();
            Console.WriteLine("=== Detailed Blood Values ===");
            foreach (var patient in patients.Values.OrderBy(p => p.Id))
                PrintPatientDetail(patient, obsByPatient, wbcCode, rbcCode, hbCode);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("=== Patient Summary ===");
            PrintPatientSummary(patients.Values, obsByPatient);
        }

        // ── 4. Serialize first patient as FHIR JSON ──────────────────────────
        // In detailed mode we print the raw FHIR JSON so you can see exactly what
        // the server will receive — useful for learning the FHIR wire format.
        if (detailedOutput)
        {
            Console.WriteLine();
            Console.WriteLine("=== First Patient as FHIR JSON ===");
            var jsonOptions = new JsonSerializerOptions().ForFhir();
            jsonOptions.WriteIndented = true;
            Console.WriteLine(JsonSerializer.Serialize<Resource>(patients.Values.First(), jsonOptions));
        }

        // ── 5. Send to FHIR server ───────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("=== Upload to http://localhost:8080 ===");

        // Same FhirClientSettings pattern as Exercise 3.
        // ReturnPreference.Representation means the server sends back the saved
        // resource (with its server-assigned id) in the response body.
        var settings = new FhirClientSettings
        {
            PreferredFormat   = ResourceFormat.Json,
            VerifyFhirVersion = false,
            ReturnPreference  = ReturnPreference.Representation
        };

        var client = new FhirClient("http://localhost:8080", settings);

        if (includeMeasureTrainingData)
        {
            Console.WriteLine("Before upload:");
            await PrintMeasureTrainingDataSnapshot(client);
        }

        // Conditional create: If a Patient with this identifier already exists
        // (e.g. on a re-run), the server returns it unchanged instead of creating
        // a duplicate. The Firely SDK sends an If-None-Exist header automatically.
        //
        // serverPatients maps our CSV patient id to the server's returned Patient
        // (which carries the authoritative server-assigned id we need shortly).
        var serverPatients = new Dictionary<string, Patient>(); // csv id → server Patient
        var patientCreated = 0;
        var patientReused = 0;
        var patientFailed = 0;

        // Stopwatch measures how long the patient upload loop takes in milliseconds.
        var patientTimer = Stopwatch.StartNew();

        foreach (var (csvId, patient) in patients)
        {
            try
            {
                // ExistsByIdentifier runs a search to check if the patient is already
                // on the server. We count it as "reused" or "created" accordingly,
                // but in both cases we call ConditionalCreateAsync below.
                if (await ExistsByIdentifier<Patient>(client, IdentifierSystem, csvId))
                    patientReused++;
                else
                    patientCreated++;

                // SearchParams.Add("identifier", ...) builds the condition string
                // "identifier=system|value" that the server uses in the If-None-Exist header.
                var condition = new SearchParams().Add("identifier", $"{IdentifierSystem}|{csvId}");
                var serverPatient = await client.ConditionalCreateAsync(patient, condition);
                serverPatients[csvId] = serverPatient!;

                if (detailedOutput)
                {
                    var pname = serverPatient!.Name[0];
                    Console.WriteLine($"  Patient {csvId} ({pname.Given.First()} {pname.Family}) -> server id: {serverPatient.Id}");
                }
            }
            catch (Exception ex)
            {
                patientFailed++;
                Console.WriteLine($"  Patient {csvId} failed: {ex.Message}");
            }
        }
        patientTimer.Stop();

        // Remap observation subjects from CSV ids to server-assigned ids.
        //
        // Before upload, each Observation's Subject.Reference is "Patient/<csv-id>"
        // (a temporary id we set in MapObservations). The FHIR server doesn't know
        // those csv ids, so we must replace them with the real server ids returned
        // by ConditionalCreateAsync above before uploading the observations.
        var observationSkipped = 0;
        foreach (var obs in observations)
        {
            // Extract the csv patient id from the current Subject reference.
            var csvId = obs.Subject!.Reference!.Split('/')[1];
            if (serverPatients.TryGetValue(csvId, out var mappedPatient))
                obs.Subject = new ResourceReference($"Patient/{mappedPatient.Id}");
            else
                // If we didn't get a server patient for this csv id (e.g. it failed
                // to upload), we'll skip its observations during upload.
                observationSkipped++;
        }

        var observationCreated = 0;
        var observationReused = 0;
        var observationFailed = 0;
        var observationTimer = Stopwatch.StartNew();

        foreach (var obs in observations)
        {
            // Double-check: only upload observations whose subject was successfully
            // remapped to a server id. LastOrDefault() gets the id segment after '/'.
            var csvId = obs.Subject?.Reference?.Split('/').LastOrDefault();
            if (csvId is not null && !serverPatients.Values.Any(p => p.Id == csvId))
            {
                observationSkipped++;
                continue;
            }

            // Retrieve the deterministic identifier key built in Mapper.cs
            // (format: PATIENT_ID:TIMESTAMP:LOINC).
            var obsIdentifier = obs.Identifier
                .FirstOrDefault(i => i.System == ObservationIdentifierSystem)
                ?.Value;

            try
            {
                // If for some reason there's no identifier (shouldn't happen), fall
                // back to a plain create without a condition.
                if (string.IsNullOrWhiteSpace(obsIdentifier))
                {
                    await client.CreateAsync(obs);
                    observationCreated++;
                    continue;
                }

                if (await ExistsByIdentifier<Observation>(client, ObservationIdentifierSystem, obsIdentifier))
                    observationReused++;
                else
                    observationCreated++;

                // Same conditional create pattern as for patients.
                var condition = new SearchParams()
                    .Add("identifier", $"{ObservationIdentifierSystem}|{obsIdentifier}");
                await client.ConditionalCreateAsync(obs, condition);
            }
            catch (Exception ex)
            {
                observationFailed++;
                if (detailedOutput)
                    Console.WriteLine($"  Observation upsert failed ({obsIdentifier ?? "no-id"}): {ex.Message}");
            }
        }
        observationTimer.Stop();

        Console.WriteLine("Upload summary:");
        Console.WriteLine($"  Patients: created {patientCreated}, reused {patientReused}, failed {patientFailed} ({patientTimer.ElapsedMilliseconds} ms)");
        Console.WriteLine($"  Observations: created {observationCreated}, reused {observationReused}, failed {observationFailed}, skipped {observationSkipped} ({observationTimer.ElapsedMilliseconds} ms)");

        // ── 6. $everything on first patient ──────────────────────────────────
        // $everything is a FHIR operation that returns all resources linked to a
        // patient in a single Bundle. We call it via a raw HttpClient because the
        // SDK's built-in OperationAsync had issues with relative-URI routing.
        if (serverPatients.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("=== $everything skipped (no uploaded patients) ===");
            return;
        }

        var firstPatient = serverPatients.Values.First();
        var fname = firstPatient.Name[0];
        Console.WriteLine();
        Console.WriteLine($"=== $everything for {fname.Given.First()} {fname.Family} (id: {firstPatient.Id}) ===");

        try
        {
            var everythingUrl = $"http://localhost:8080/Patient/{firstPatient.Id}/$everything";

            // HttpClient is the standard .NET HTTP client. We set an Accept header
            // so the server knows to respond with FHIR JSON.
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Accept.ParseAdd("application/fhir+json");

            var response = await http.GetAsync(everythingUrl);
            if (response.StatusCode == System.Net.HttpStatusCode.NotImplemented)
            {
                Console.WriteLine("  $everything not available on this server (501 NotImplemented).");
                return;
            }

            // EnsureSuccessStatusCode throws an exception for any non-2xx response.
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            // Deserialize the response JSON into a FHIR Bundle using the Firely serializer.
            var bundleOptions = new JsonSerializerOptions().ForFhir();
            var bundle = JsonSerializer.Deserialize<Bundle>(content, bundleOptions);

            if (bundle is not null)
            {
                Console.WriteLine($"  Bundle.total = {bundle.Total}");

                // GroupBy groups the bundle entries by their FHIR resource type name,
                // then we print how many of each type the server returned.
                var countsByType = (bundle.Entry ?? [])
                    .GroupBy(e => e.Resource?.TypeName ?? "Unknown")
                    .OrderBy(g => g.Key);

                foreach (var group in countsByType)
                    Console.WriteLine($"  {group.Key}: {group.Count()}");

                if (detailedOutput)
                {
                    // Show the first 25 entries individually so you can see what's in there.
                    Console.WriteLine("  Sample entries:");
                    foreach (var entry in (bundle.Entry ?? []).Take(25))
                        Console.WriteLine($"  - {entry.Resource?.TypeName}/{entry.Resource?.Id}");

                    var extra = (bundle.Entry?.Count ?? 0) - 25;
                    if (extra > 0)
                        Console.WriteLine($"  ... and {extra} more");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  $everything call failed: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Measure Training Data ===");
        if (!includeMeasureTrainingData)
        {
            Console.WriteLine("  Excluded by option.");
            return;
        }

        Console.WriteLine("After upload:");
        await PrintMeasureTrainingDataSnapshot(client);
    }

    // AskYesNo — prints a prompt and returns true if the user types "y" or "yes".
    // Returns false for anything else (including Enter/blank = default No).
    private static bool AskYesNo(string prompt)
    {
        Console.Write(prompt);
        var input = Console.ReadLine();
        return string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    }

    // PrintPatientSummary — compact one-line-per-patient table showing name,
    // gender, number of observation rows, and date range.
    private static void PrintPatientSummary(
        IEnumerable<Patient> patients,
        Dictionary<string, IGrouping<string, Observation>> obsByPatient)
    {
        Console.WriteLine($"{"Patient",-24} {"Gender",-8} {"Rows",4} {"First Date",12} {"Last Date",12}");
        Console.WriteLine(new string('-', 68));

        foreach (var patient in patients.OrderBy(p => p.Id))
        {
            var name = patient.Name[0];
            var label = $"{name.Given.First()} {name.Family}";
            var patientRef = $"Patient/{patient.Id}";
            var rows = new List<string>();

            if (obsByPatient.TryGetValue(patientRef, out var patObs))
            {
                // Collect distinct effective date strings and sort them to find the range.
                rows = patObs
                    .Select(o => o.Effective?.ToString() ?? string.Empty)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
            }

            var firstDate = rows.FirstOrDefault();
            var lastDate = rows.LastOrDefault();
            // Truncate to 10 characters (YYYY-MM-DD) for display alignment.
            var first = firstDate is null ? "-" : firstDate[..Math.Min(10, firstDate.Length)];
            var last = lastDate is null ? "-" : lastDate[..Math.Min(10, lastDate.Length)];

            Console.WriteLine($"{label,-24} {patient.Gender,-8} {rows.Count,4} {first,12} {last,12}");
        }
    }

    // PrintPatientDetail — verbose per-patient block with a row-per-date table
    // showing all three blood values side by side.
    private static void PrintPatientDetail(
        Patient patient,
        Dictionary<string, IGrouping<string, Observation>> obsByPatient,
        string wbcCode,
        string rbcCode,
        string hbCode)
    {
        var name = patient.Name[0];
        var gender = patient.Gender?.ToString() ?? "Unknown";
        Console.WriteLine();
        Console.WriteLine($"┌─ {name.Given.First()} {name.Family} ({patient.Id} | {gender})");
        Console.WriteLine($"│  {"Date",-22} {"WBC (10*3/uL)",14} {"RBC (10*6/uL)",14} {"HB (g/dL)",10}");
        Console.WriteLine($"│  {new string('-', 64)}");

        var patientRef = $"Patient/{patient.Id}";
        if (obsByPatient.TryGetValue(patientRef, out var patObs))
        {
            // Group observations by their effective date so each row of the table
            // shows the three values recorded at the same time.
            var rows = patObs
                .GroupBy(o => o.Effective?.ToString() ?? string.Empty)
                .OrderBy(g => g.Key);

            foreach (var row in rows)
            {
                var wbc = GetValue(row, wbcCode);
                var rbc = GetValue(row, rbcCode);
                var hb = GetValue(row, hbCode);
                // Trim the timestamp to 19 characters (YYYY-MM-DDTHH:MM:SS).
                var ts = row.Key?[..Math.Min(19, row.Key.Length)] ?? "";
                Console.WriteLine($"│  {ts,-22} {wbc,14} {rbc,14} {hb,10}");
            }
        }

        Console.WriteLine("└");
    }

    // GetValue — finds the Observation in the group that matches the given LOINC code
    // and returns its numeric value formatted to 2 significant decimal places.
    private static string GetValue(IEnumerable<Observation> obs, string loincCode)
    {
        var match = obs.FirstOrDefault(o => o.Code.Coding[0].Code == loincCode);
        if (match?.Value is Quantity q)
            return q.Value?.ToString("0.##") ?? "-";
        return "-";
    }

    // ExistsByIdentifier<TResource> — searches the server for a resource of type
    // TResource with the given identifier. Returns true if at least one is found.
    //
    // TResource : Resource, new() is a generic type constraint that means "TResource
    // must be a FHIR Resource and must have a default (no-argument) constructor."
    // This is required by the Firely SDK's SearchAsync method.
    private static async System.Threading.Tasks.Task<bool> ExistsByIdentifier<TResource>(
        FhirClient client,
        string system,
        string value)
        where TResource : Resource, new()
    {
        var criteria = new[] { $"identifier={system}|{value}" };
        var result = await client.SearchAsync<TResource>(criteria);
        if (result is null)
            return false;

        // bundle.Total is the count the server reports; if it's present, trust it.
        // Otherwise fall back to counting the actual entries in this page.
        if (result.Total.HasValue)
            return result.Total.Value > 0;

        return (result.Entry?.Count ?? 0) > 0;
    }

    // PrintMeasureTrainingDataSnapshot — queries the server for five resource types
    // related to clinical quality measures and prints their current counts.
    // Called before and after the upload to show whether any measure data exists.
    private static async System.Threading.Tasks.Task PrintMeasureTrainingDataSnapshot(FhirClient client)
    {
        var measureCount = await CountResources<Measure>(client);
        var libraryCount = await CountResources<Library>(client);
        var measureReportCount = await CountResources<MeasureReport>(client);
        var planDefinitionCount = await CountResources<PlanDefinition>(client);
        var activityDefinitionCount = await CountResources<ActivityDefinition>(client);

        Console.WriteLine("  Resource counts on server:");
        Console.WriteLine($"    Measure: {measureCount}");
        Console.WriteLine($"    Library: {libraryCount}");
        Console.WriteLine($"    MeasureReport: {measureReportCount}");
        Console.WriteLine($"    PlanDefinition: {planDefinitionCount}");
        Console.WriteLine($"    ActivityDefinition: {activityDefinitionCount}");
    }

    // CountResources<TResource> — asks the server for a count-only search result
    // (_summary=count) and returns the number. This is more efficient than fetching
    // full resources just to count them.
    private static async System.Threading.Tasks.Task<int> CountResources<TResource>(FhirClient client)
        where TResource : Resource, new()
    {
        var result = await client.SearchAsync<TResource>(new[] { "_summary=count" });
        if (result?.Total is int total)
            return total;

        return result?.Entry?.Count ?? 0;
    }
}
