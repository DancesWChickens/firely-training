# Firely Training Exercises (FHIR + C#)

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![FHIR R4](https://img.shields.io/badge/FHIR-R4-2E7D32)
![C#](https://img.shields.io/badge/C%23-Training-239120?logo=c-sharp&logoColor=white)
[![Build](https://github.com/DancesWChickens/firely-training/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/DancesWChickens/firely-training/actions/workflows/build.yml)
![GitHub stars](https://img.shields.io/github/stars/DancesWChickens/firely-training?style=social)

A beginner-friendly C# project for learning how to work with FHIR resources using the Firely .NET SDK.

This project includes four progressive exercises:

1. Build a `Patient` resource in memory and serialize it to FHIR JSON.
2. Deserialize a FHIR `Observation` from JSON and serialize it back.
3. Use a FHIR REST client to search, create, read, update, delete, and page through resources.
4. Read CSV lab data, map it to FHIR `Patient` + `Observation`, upload idempotently, and call `$everything`.

## Tech Stack

- .NET 10
- Firely .NET SDK (`Hl7.Fhir.R4`)
- CsvHelper
- Firely Server (Docker)

## Project Structure

- `Program.cs`: menu/entry point
- `Exercise1.cs`: build + serialize Patient
- `Exercise2.cs`: deserialize + serialize Observation
- `Exercise3.cs`: FHIR REST client workflows (search/CRUD/paging)
- `CSVModel.cs`: CSV column mapping model
- `Mapper.cs`: CSV -> FHIR mapping logic
- `Exercise4.cs`: CSV pipeline + upload + `$everything`
- `observation.json`: sample Observation input for Exercise 2/3
- `sample-data.csv`: sample lab data for Exercise 4

## Prerequisites

1. Install .NET 10 SDK.
2. Start a local Firely Server at `http://localhost:8080`.
3. Ensure MongoDB backing store is running if your Firely Server setup requires it.

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run
```

You will see a menu to run exercises 1-4.

## Notes on Exercise 4

- Upload uses business identifiers so re-running does not create duplicate resources.
- Patient identifiers use:
  - `urn:training:firely:patient-id`
- Observation identifiers use:
  - `urn:training:firely:observation-id`
- `$everything` is called for one uploaded patient to inspect linked resources.

## Common Troubleshooting

- If `dotnet run` cannot connect to the server:
  - Confirm Firely Server is running and reachable at `http://localhost:8080`.
- If `$everything` returns `501 NotImplemented`:
  - Enable the PatientEverything plugin in your Firely Server config.
- If builds fail after package changes:
  - Run `dotnet restore` then `dotnet build`.

## Learning Goal

This project is based on FHIRStarters https://github.com/FirelyTeam/fhirstarters by Firely designed to help a C# beginner understand:

- FHIR resource modeling in code
- JSON serialization/deserialization for FHIR
- Safe REST interactions against a FHIR server
- Data ingestion and idempotent upload patterns
