# Picktime Automation

Books the archery club's indoor targets on Picktime, so no one has to book by hand during the indoor season.

## What it does

* It runs at 00:05 London time on Tuesday, Thursday and Friday. Picktime opens each slot exactly 7 days ahead, at midnight.
* It books 17:00, 18:00 and 19:00, seven days ahead.
* It books target 2b. For each hour where 2b is taken, it books 3a.
* It books only in the indoor season, 1 October to 31 March. A run outside the season books nothing, and says so in the logs.

The days, hours, targets and season are settings, so a change needs no code change.

For detail, read:

| Document | Question it answers |
| --- | --- |
| [docs/spec.md](docs/spec.md) | What does it do, and why? |
| [docs/plan.md](docs/plan.md) | How is it built? |
| [docs/tasks.md](docs/tasks.md) | What is done, and what is next? |

## How it is built

One Azure Function app: .NET 10 isolated worker, on the Windows Consumption plan. It has two functions. Both call the same booking service.

| Function | Trigger | Use |
| --- | --- | --- |
| `TargetBookingFunction` | Timer. The schedule is the `BookingSchedule` setting. | The scheduled run |
| `ManualBookingFunction` | HTTP: `POST /api/book` | Tests, and catching up a missed run |

The manual trigger:

* Takes an optional `bookingDate=yyyy-MM-dd` query parameter. Without it, it books 7 days from today in London. A date before today in London is refused with HTTP 400.
* Needs the function key in Azure, in the `x-functions-key` header. A local run needs no key.
* Returns the run's summary as JSON.

| Project | Contents |
| --- | --- |
| `PicktimeAutomation.AzureFunctions` | The two triggers, start-up and logging |
| `PicktimeAutomation.Services` | The booking rules and the Picktime API client |
| `PicktimeAutomation.Models` | Settings and data models |
| `PicktimeAutomation.ServicesTests`, `PicktimeAutomation.AzureFunctionsTests` | Tests |

## Run it locally

> [!WARNING]
> A local run uses the real Picktime API with your real settings.
>
> * Keep the timer disabled. `local.settings.json` must set `AzureWebJobs.TargetBookingFunction.Disabled` to `true`. Without it, `func start` can fire a missed scheduled run at once, and that run books (plan section 8.2).
> * A request for a date in the season makes real bookings. For a first check, use a date outside the season, as in step 6.

### Prerequisites

| Tool | Version | Needed for |
| --- | --- | --- |
| [.NET SDK](https://dotnet.microsoft.com/download) | 10 | Building and testing |
| [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local) | 4.x | Running the Function app (`func start`) |
| [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) | Current | Local storage. The timer trigger needs it. |

Azurite is bundled with Visual Studio 2026. You can also install it with `npm install -g azurite`, which needs a Node.js version that Azurite supports.

### Steps

Run the commands from the repository root unless a step says otherwise.

1. Clone the repository:

   ```
   git clone https://github.com/OllieRumbol/PicktimeAutomation.git
   cd PicktimeAutomation
   ```

   On Windows, clone into a short path, such as `C:\src`. The Functions build makes deep folders inside the project, and a long clone path makes the build fail with "exceeds the OS max path limit".

2. Restore and build:

   ```
   dotnet build PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx
   ```

3. Create `PicktimeAutomation/PicktimeAutomation.AzureFunctions/local.settings.json`. Git ignores this file, so it is never committed. Start from this template, and replace each `<...>` value and each example value with your own:

   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
       "AzureWebJobs.TargetBookingFunction.Disabled": "true",
       "BookingSchedule": "0 5 0 * * TUE,THU,FRI",
       "Picktime:BaseUrl": "https://www.picktime.com/",
       "Picktime:ScanToken": "<your-scan-token>",
       "Picktime:AccountId": "<your-account-id>",
       "Picktime:LocationId": "<your-location-id>",
       "Archer:FirstName": "Jane",
       "Archer:LastName": "Example",
       "Archer:Email": "jane@example.com",
       "Booking:DaysAhead": "7",
       "Booking:Hours:0": "17",
       "Booking:Hours:1": "18",
       "Booking:Hours:2": "19",
       "Booking:Targets:0:Name": "2b",
       "Booking:Targets:0:ResourceId": "<2b-resource-id>",
       "Booking:Targets:1:Name": "3a",
       "Booking:Targets:1:ResourceId": "<3a-resource-id>",
       "Booking:SeasonStart": "10-01",
       "Booking:SeasonEnd": "03-31"
     }
   }
   ```

   Do not add `APPLICATIONINSIGHTS_CONNECTION_STRING` locally. Without it, the logs show in the `func start` console.

4. Start Azurite in a second terminal, and leave it running. With the copy bundled with Visual Studio 2026 (Community edition path, in PowerShell):

   ```
   & "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\Azure Storage Emulator\azurite.exe" --location "$env:LOCALAPPDATA\Azurite"
   ```

   With the npm copy: `azurite --location <a folder for its data>`. If you start the app from Visual Studio, Visual Studio starts Azurite for you.

5. Start the Function app from the Functions project folder:

   ```
   cd PicktimeAutomation/PicktimeAutomation.AzureFunctions
   func start
   ```

   The output must show `Function TargetBookingFunction is disabled`. If it does not, stop the app at once with Ctrl+C, and fix `local.settings.json`.

6. Make a safe first check. In a third terminal, send a request for a date outside the season (1 April to 30 September) that is not in the past:

   ```
   curl.exe -s -X POST -d "{}" "http://localhost:7071/api/book?bookingDate=2027-04-15"
   ```

   This books nothing and calls nothing. The response is HTTP 200 with `"verdict":"Skipped"` and every count at 0. The `func start` console shows three lines: `Booking run started. ... InSeason=False`, `The run is skipped. ...` and `Booking run finished. ... Verdict=Skipped`.

## Run the tests

From the repository root:

```
dotnet test PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx
```

The tests use fakes. They need no settings and no Azurite, and they call nothing.

## Settings

Every setting the app reads, by name. Start-up checks them and stops with a message that names a setting that is missing or not valid. Plan section 2 gives the validation rules.

| Setting | Where | Purpose |
| --- | --- | --- |
| `BookingSchedule` | Both | When the timer runs. An NCRONTAB expression with six fields, starting with seconds. `0 5 0 * * TUE,THU,FRI` is 00:05 on Tuesday, Thursday and Friday. |
| `Picktime:BaseUrl` | Both | The Picktime API address |
| `Picktime:ScanToken` | Both | The secret token that Picktime needs on every call |
| `Picktime:AccountId` | Both | The club's Picktime account |
| `Picktime:LocationId` | Both | The club's Picktime location |
| `Archer:FirstName` | Both | The archer's first name on each booking |
| `Archer:LastName` | Both | The archer's last name on each booking |
| `Archer:Email` | Both | The archer's email. Picktime sends the confirmations here. |
| `Booking:DaysAhead` | Both | How many days ahead to book |
| `Booking:Hours:0`, `:1`, ... | Both | The hours to book, one key for each hour (0 to 23) |
| `Booking:Targets:0:Name`, `Booking:Targets:0:ResourceId`, ... | Both | The targets, in preference order. One name and one Picktime resource id for each target. |
| `Booking:SeasonStart`, `Booking:SeasonEnd` | Both | The first and last day of the season, as `MM-dd` |
| `AzureWebJobsStorage` | Both | The storage for the timer. Locally `UseDevelopmentStorage=true` (Azurite). In Azure, set when the Function app is created. |
| `FUNCTIONS_WORKER_RUNTIME` | Both | `dotnet-isolated` |
| `AzureWebJobs.TargetBookingFunction.Disabled` | **Local only** | `true`. Stops the timer during local runs. **Never set it in Azure**, where the timer must run. |
| `WEBSITE_TIME_ZONE` | Azure only | `GMT Standard Time`, so the timer runs on London time, with British Summer Time |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Azure only | Sends the logs to Application Insights. Set by Azure when Application Insights is linked. |

The hours and targets are lists. Settings hold only flat text, so each item is its own key, numbered from 0 (plan section 2).

In Azure, the settings are Function app application settings: Function app → Settings → Environment variables. A change restarts the app. See [Operating rules](#operating-rules) for when not to make one.

## Deployment

The workflow [`.github/workflows/build-and-deploy.yml`](.github/workflows/build-and-deploy.yml) does it all:

1. On every pull request and every push to `main`: restore, build, test and publish. A failing test, or a package with a known vulnerability, stops the run.
2. On a push to `main` only, after step 1 passes: sign in to Azure with OpenID Connect and deploy. No secret or publish profile is used.

So **a merge to `main` deploys**.

The sign-in uses three repository variables. They identify the Azure managed identity. They are not secrets.

* `AZURE_CLIENT_ID`
* `AZURE_TENANT_ID`
* `AZURE_SUBSCRIPTION_ID`

To roll back, revert the bad commit on `main`. The workflow then tests and deploys the previous version. Plan section 8.4 has the detail.

## Checking runs

| Check | Where | Use it for |
| --- | --- | --- |
| Confirmation emails | The archer's inbox | The quickest check. One email for each booking. |
| Invocations | Function app → Functions → the function → Invocations | The normal check. One row for each run. Select a row to see that run's log lines. |
| Dashboard tile | The pinned tile on the Azure dashboard | The run summaries for the last 30 days |
| Logs (KQL) | Application Insights → Logs | Every run in the last 90 days. Run the query in plan section 5.4. |

Log stream in the portal does not work, because the app sends its logs through OpenTelemetry (plan section 5.2).

## Operating rules

A run day is a day in `BookingSchedule`: today Tuesday, Thursday and Friday, in London time. The automation must never hold two bookings for the same hour. These rules keep it that way (spec section 9).

| Do not | When | Why |
| --- | --- | --- |
| Use the manual trigger | Between 00:05 and 01:00 on a run day | A late scheduled run can start at any time in this window. Both runs would book the same date. |
| Use the manual trigger for a booking date that is already booked | At any time | The run sees its own bookings as taken, and books the fallback target for the same hours. |
| Deploy, merge to `main`, or change an app setting | Between 00:05 and 01:00 on a run day | Each one restarts the app. A run stopped part way runs again, and can book the fallback target for hours it already booked. |
| Book by hand | Between 00:00 and 01:00 on a run day | The run sees your booking as taken, and books the fallback target for the same hour. |

## License

[MIT](LICENSE)
