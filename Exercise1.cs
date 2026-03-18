// ─────────────────────────────────────────────────────────────────────────────
// Exercise1.cs  –  Building a FHIR Patient resource in memory
//
// Goal: learn how to create a FHIR resource using the Firely .NET SDK classes,
// without touching a server at all.
//
// Key concepts:
//   - Patient        : a FHIR resource that represents a person receiving care
//   - HumanName      : structured name (given + family)
//   - Identifier     : a business-level id (e.g. MRN, UUID) separate from
//                      the server-assigned technical id
//   - Extension      : a way to attach extra data that the base spec doesn't
//                      have a built-in field for (used here for gender identity)
//   - FhirJsonSerializer : Firely helper that turns a resource object into
//                          a FHIR-compliant JSON string
// ─────────────────────────────────────────────────────────────────────────────

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

public static class Exercise1
{
    public static void Run()
    {
        // Build a patient and print it as FHIR JSON so we can inspect the structure
        var patient = CreatePatient();

        // FhirJsonSerializer knows the FHIR JSON spec so the output is valid FHIR,
        // not just generic JSON
        var serializer = new FhirJsonSerializer();
        Console.WriteLine(serializer.SerializeToString(patient));
    }

    // CreatePatient is public so Exercise 3 and 4 can reuse it without
    // duplicating the construction logic
    public static Patient CreatePatient()
    {
        // Make a timestamp + short random suffix so every run creates a
        // distinctly named patient (useful when sending to a real server)
        var runStamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8]; // first 8 chars of a GUID
        var uniqueToken = $"{runStamp}-{uniqueSuffix}";

        // Object initializer syntax: sets properties inside { } at creation time
        Patient patient = new()
        {
            BirthDate = "1970-01-01",            // FHIR date format: YYYY-MM-DD
            Gender = AdministrativeGender.Male   // enum from the Firely SDK
        };

        // Add a UUID-based business identifier so this patient can be found
        // by something other than the server-assigned id
        patient.Identifier.Add(new Identifier
        {
            System = "urn:ietf:rfc:3986",          // standard URI system
            Value = $"urn:uuid:{Guid.NewGuid()}"   // a fresh UUID each run
        });

        // HumanName: the first argument is the family name, second is given names
        patient.Name.Add(new HumanName($"Everyman-{uniqueToken}", new[] { "Adam" })
        {
            Use = HumanName.NameUse.Usual  // marks this as the everyday name
        });

        patient.Address.Add(new Address
        {
            Use = Address.AddressUse.Home,
            Line = new[] { "666 Main St" },
            City = "Anytown",
            State = "NY",
            PostalCode = "12345",
            Country = "USA"
        });

        // ??= means: only assign if the left side is currently null
        // We need GenderElement to exist before we can attach extensions to it
        patient.GenderElement ??= new Code<AdministrativeGender>();

        // Extensions let you attach data beyond the base FHIR spec.
        // Here we record gender identity (different from administrative gender)
        // using the HL7-defined extension URL as the key
        patient.GenderElement.Extension.Add(new Extension
        {
            Url = "http://hl7.org/fhir/StructureDefinition/patient-genderIdentity",
            Value = new CodeableConcept
            {
                Coding = new List<Coding>
                {
                    new Coding("http://hl7.org/fhir/administrative-gender", "other", "Other")
                },
                Text = "Non-binary"
            }
        });

        return patient;
    }
}

