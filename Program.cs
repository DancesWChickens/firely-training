// ─────────────────────────────────────────────────────────────────────────────
// Program.cs  –  Entry point / main menu
//
// This is the top-level file that runs when you start the app.
// It shows a menu and calls the matching Exercise class depending on your input.
//
// "await" is used for exercises that make network calls (3 and 4); it lets the
// app wait for the server response without blocking the whole process.
// ─────────────────────────────────────────────────────────────────────────────

while (true)
{
	Console.WriteLine("Select an exercise:");
	Console.WriteLine("1 - Exercise 1 (Patient)");
	Console.WriteLine("2 - Exercise 2 (Serialization)");
	Console.WriteLine("3 - Exercise 3 (FHIR Client)");
	Console.WriteLine("4 - Exercise 4 (Server Interaction)");
	Console.WriteLine("Q - Quit");

	var input = Console.ReadLine();

	// Trim whitespace and compare case-insensitively so "q", "Q", or " Q " all work
	switch (input?.Trim().ToUpperInvariant())
	{
		case "1":
			Exercise1.Run();
			break;

		case "2":
			Exercise2.Run();
			break;

		case "3":
			// async: waits for server calls to complete before continuing
			await Exercise3.Run();
			break;

		case "4":
			await Exercise4.Run();
			break;

		case "Q":
			return;

		default:
			Console.WriteLine("Invalid option");
			break;
	}

	Console.WriteLine();
}