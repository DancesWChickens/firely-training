// CSVModel — Data model for one row of the sample CSV file
//
// CsvHelper reads the CSV and automatically maps each column to the matching
// property using the [Name(...)] attribute. The string inside [Name(...)] must
// exactly match the column header in the CSV file (case-sensitive).
//
// { get; set; } is a C# auto-property: shorthand for a field with a getter and
// a setter. CsvHelper uses the setter to populate the value when reading.

using CsvHelper.Configuration.Attributes;

public class CSVModel
{
    // The NHANES sequence number — a unique row identifier in the source data.
    [Name("SEQN")]
    public double Seqn { get; set; }

    // ISO 8601 date/time string for when the observations were recorded,
    // e.g. "1999-01-16T00:00:00". Used as the Observation's effective date.
    [Name("TIMESTAMP")]
    public string Timestamp { get; set; } = string.Empty;

    // Our synthetic patient identifier (not a real NHANES id).
    // Used as the FHIR business identifier and as the Patient's local Id.
    [Name("PATIENT_ID")]
    public string PatientId { get; set; } = string.Empty;

    // Patient's last name.
    [Name("PATIENT_FAMILYNAME")]
    public string PatientFamilyName { get; set; } = string.Empty;

    // Patient's first name.
    [Name("PATIENT_GIVENNAME")]
    public string PatientGivenName { get; set; } = string.Empty;

    // "M" or "F" — mapped to the FHIR AdministrativeGender enum in Mapper.cs.
    [Name("PATIENT_GENDER")]
    public string PatientGender { get; set; } = string.Empty;

    // WBC = White Blood Cell count, in units of 10*3/uL (LOINC 6690-2).
    [Name("WBC")]
    public double Wbc { get; set; }

    // RBC = Red Blood Cell count, in units of 10*6/uL (LOINC 789-8).
    [Name("RBC")]
    public double Rbc { get; set; }

    // Hb = Hemoglobin, in units of g/dL (LOINC 718-7).
    [Name("HB")]
    public double Hb { get; set; }
}
