// Mapper — Converts CSVModel rows into typed FHIR resources
//
// This class is the bridge between raw spreadsheet data and the FHIR data model.
// It produces one Patient per unique patient id, and three Observations per row
// (WBC, RBC, Hemoglobin), each coded with their LOINC code and UCUM unit.

using Hl7.Fhir.Model;

public static class Mapper
{
    // These constants are well-known terminology system URIs used in FHIR.
    // LOINC  — standardised laboratory test codes (e.g. 718-7 = Hemoglobin).
    // UCUM   — standardised unit codes (e.g. g/dL = grams per decilitre).
    // ObsCat — the HL7 code system for Observation categories such as "laboratory".
    // ObsIdentifierSystem — a custom URN (not a real URL) that namespaces our
    //   synthetic observation business identifiers so they don't collide with ids
    //   from other systems.
    private const string LoincSystem         = "http://loinc.org";
    private const string UcumSystem          = "http://unitsofmeasure.org";
    private const string ObsCatSystem        = "http://terminology.hl7.org/CodeSystem/observation-category";
    private const string ObsIdentifierSystem = "urn:training:firely:observation-id";

    // MapPatient — builds a FHIR Patient from one CSV row.
    public static Patient MapPatient(CSVModel record)
    {
        var patient = new Patient
        {
            // Set the Patient's local technical id to the CSV patient id.
            // This is used as a temporary client-side id. When the resource is
            // uploaded, the server will assign its own id and this value is
            // discarded (or overwritten if we use conditional create).
            Id = record.PatientId,

            // Switch expression: map the CSV gender string to the FHIR enum.
            // ToUpperInvariant() normalises the input so "m" and "M" both work.
            // The _ (discard) arm is the default — anything else becomes Unknown.
            Gender = record.PatientGender.ToUpperInvariant() switch
            {
                "M" => AdministrativeGender.Male,
                "F" => AdministrativeGender.Female,
                _   => AdministrativeGender.Unknown
            }
        };

        // Add a FHIR business identifier so we can look the patient up by our
        // own id later (used for conditional create — prevents duplicates on re-run).
        // An Identifier has a System (the namespace/the authority that issued it)
        // and a Value (the actual id string within that namespace).
        patient.Identifier.Add(new Identifier
        {
            System = "urn:training:firely:patient-id",
            Value  = record.PatientId
        });

        // HumanName holds the patient's name in structured form.
        // Given is a list because a person can have multiple given names.
        // The square-bracket syntax [record.PatientGivenName] is a collection
        // expression (C# 12) — it creates a list with one item.
        patient.Name.Add(new HumanName
        {
            Family = record.PatientFamilyName,
            Given = [record.PatientGivenName]
        });

        return patient;
    }

    // MapObservations — yields three FHIR Observations from one CSV row.
    //
    // IEnumerable<Observation> means this method returns a sequence of Observations.
    // 'yield return' is a C# feature for lazy iteration: each call to the next
    // element in the sequence runs the method up to the next 'yield return' and
    // pauses there. The caller (Exercise4) uses this with SelectMany to flatten
    // all observations from all rows into one list.
    public static IEnumerable<Observation> MapObservations(CSVModel record)
    {
        // Build the subject reference once and reuse it for all three observations.
        // ResourceReference stores a relative URL pointing to the Patient.
        var subject   = new ResourceReference($"Patient/{record.PatientId}");

        // FhirDateTime wraps an ISO 8601 string as a FHIR date/time type.
        var effective = new FhirDateTime(record.Timestamp);

        // WBC — White Blood Cell count (LOINC 6690-2)
        yield return MakeObservation(
            subject, effective,
            code: "6690-2",
            display: "Leukocytes [#/volume] in Blood by Automated count",
            value: record.Wbc,
            unitCode: "10*3/uL",
            identifierValue: BuildObservationIdentifier(record, "6690-2"));

        // RBC — Red Blood Cell count (LOINC 789-8)
        yield return MakeObservation(
            subject, effective,
            code: "789-8",
            display: "Erythrocytes [#/volume] in Blood by Automated count",
            value: record.Rbc,
            unitCode: "10*6/uL",
            identifierValue: BuildObservationIdentifier(record, "789-8"));

        // Hemoglobin — (LOINC 718-7)
        yield return MakeObservation(
            subject, effective,
            code: "718-7",
            display: "Hemoglobin [Mass/volume] in Blood",
            value: record.Hb,
            unitCode: "g/dL",
            identifierValue: BuildObservationIdentifier(record, "718-7"));
    }

    // BuildObservationIdentifier — creates a deterministic string key for an observation.
    //
    // The key combines patient id + timestamp + LOINC code, making it unique for each
    // (patient, time, test) combination. Using the same key on re-run means the server
    // can find the existing observation via conditional create and skip the duplicate.
    private static string BuildObservationIdentifier(CSVModel record, string loincCode)
        => $"{record.PatientId}:{record.Timestamp}:{loincCode}";

    // MakeObservation — private factory that builds a complete Observation resource.
    //
    // Shared by all three yield-return calls above so the structure stays consistent.
    private static Observation MakeObservation(
        ResourceReference subject,
        FhirDateTime effective,
        string code,
        string display,
        double value,
        string unitCode,
        string identifierValue)
    {
        var obs = new Observation
        {
            // Status = Final means the result is authoritative and complete.
            Status    = ObservationStatus.Final,

            // CodeableConcept pairs a coding system URI with a code and human-readable display.
            // Here it encodes the LOINC test code.
            Code      = new CodeableConcept(LoincSystem, code, display),

            Subject   = subject,
            Effective = effective,

            // Quantity stores a numeric value with a unit and the unit's coding system.
            // Casting double to decimal is required because FHIR Quantity uses decimal.
            Value     = new Quantity((decimal)value, unitCode, UcumSystem)
        };

        // Category tags the observation as a "laboratory" result.
        obs.Category.Add(new CodeableConcept(ObsCatSystem, "laboratory"));

        // Business identifier for idempotency (see BuildObservationIdentifier above).
        obs.Identifier.Add(new Identifier
        {
            System = ObsIdentifierSystem,
            Value = identifierValue
        });

        return obs;
    }
}
