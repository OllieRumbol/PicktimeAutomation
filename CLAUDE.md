# PicktimeAutomation

## Safety

The app is live in Azure and makes real bookings. These rules apply in every session.

* Booking requests are real. Never send a request to the Picktime API, run `func start`, or call the HTTP trigger. The owner does every real run.
* Tests use stub handlers and the base URL `https://picktime.invalid/`. They call nothing.
* Never run an Azure CLI or GitHub command that changes anything. The owner manages Azure, app settings, repository variables and branch protection.
* Never write a token, id, booking id, function key, name or email into the repository or the conversation.
* Before any local `func start`, `local.settings.json` must set `AzureWebJobs.TargetBookingFunction.Disabled` to `true`.
* A merge to `main` deploys. No merge, deployment or app setting change between 00:05 and 01:00 London time on a Tuesday, Thursday or Friday (spec section 9).

## Project

* What it does, how to run it and its settings: `README.md`.
* Requirements, design and tasks: `docs/spec.md`, `docs/plan.md` and `docs/tasks.md`.
* Standard test command, from the repository root: `dotnet test PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx`
