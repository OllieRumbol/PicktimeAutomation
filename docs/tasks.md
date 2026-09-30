# Picktime Automation — Tasks

> **What this document is for:** It answers *what do we do next?* It splits the approved design into small, verifiable steps and tracks progress against them.

Last updated: 2026-09-30

<!--
How to use this file
* This file holds the order of work and its progress. It does not repeat the requirements or the design.
* In "Refs", "spec 6.2" means spec.md section 6.2, and "plan 3.1" means plan.md section 3.1.
  "Test N" refers to the numbered tests in plan section 7.
* One task is one working session and one pull request.
* A task that grows beyond that is split into smaller tasks here.
* Tick a task only when every "Done when" item is true and "Verify" has passed.
* Record deviations from the spec or plan in "Notes", and update that document to match.
* The first unticked task whose dependencies are done is the next task.
* Tasks marked (manual) are done by you, outside the code. Claude can prepare and check them.
-->

Unless a task says otherwise, run commands from the repository root. The standard test command is:

```
dotnet test PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx
```

## Phase 1 — Foundations

Goal: the solution runs on .NET 10, nothing sensitive is in source, and configuration is validated at start-up.

- [ ] **T1 — Fix the self-ignoring `.gitignore`**
  - Refs: spec 4.1 defect 6, plan 6
  - Depends on: —
  - Done when:
    - The line that matches `.gitignore` is removed.
    - `bin/`, `obj/` and `local.settings.json` are ignored.
    - Both ignore files are tracked by git.
  - Verify: `git check-ignore -v PicktimeAutomation/PicktimeAutomation.AzureFunctions/local.settings.json`, then `git ls-files "*.gitignore"`
  - Notes: Done first so that the upgrade in T2 cannot stage `bin/` or `obj/` by accident.

- [ ] **T2 — Upgrade to .NET 10**
  - Refs: spec 4.1 defect 7, plan 3, plan 8.1, plan 8.2
  - Depends on: T1
  - Done when:
    - All five projects target `net10.0`.
    - The Functions worker packages are on their current major version.
    - The solution builds with no errors.
    - The Function App starts locally and the timer trigger is listed.
  - Verify: `dotnet build PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx`, then `func start` in `PicktimeAutomation/PicktimeAutomation.AzureFunctions`
  - Notes: The start check needs Azure Functions Core Tools v4. Install it before this task.

- [ ] **T3 — Move configuration out of source**
  - Refs: spec 3 goal 4, spec 4.1 defect 4, spec 5.3, spec 6.1, plan 2, plan 6, plan 7.1, plan 7.2 rule 5, plan 7.6, test 26
  - Depends on: T2
  - Done when:
    - Options classes exist for the `Picktime`, `Archer` and `Booking` settings in plan section 2.
    - They are bound with the options pattern and validated at start-up.
    - A missing `Picktime:ScanToken` stops start-up with a clear message.
    - The timer trigger reads its schedule from the `BookingSchedule` setting, as `%BookingSchedule%`. The schedule is no longer written in code.
    - No token, account id, location id, target id, name, email or schedule is a literal in source.
    - `local.settings.json` holds every setting from plan section 2, and stays ignored.
    - The two `Test1.cs` placeholders are deleted.
    - Test 26 passes.
  - Verify: the standard test command, then `git grep -n "eyJ" -- "*.cs"` returns nothing, then `func start` lists the timer trigger with the schedule from `BookingSchedule`
  - Notes:

## Phase 2 — Core booking logic

Goal: the booking rules in spec section 6 are implemented and covered by their tests.

- [ ] **T4 — Update the models**
  - Refs: plan 3.1, spec 6.5
  - Depends on: T3
  - Done when:
    - `BookingOutcome`, `BookingResult` and the `BookingRequest` record exist as in plan section 3.1.
    - `BookingOutcome` includes `Unconfirmed`, and `BookingResult` has the three statuses `Succeeded`, `Rejected` and `Unknown`.
    - `BookingAttempt` and `BookingSummary` can express per-hour outcomes, counts and an overall verdict.
    - The solution builds.
  - Verify: `dotnet build PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx`
  - Notes: Moved before the API client work, because T6 returns `BookingResult`.

- [ ] **T5 — Read availability**
  - Refs: spec 4.1 defect 2, spec 5.1, plan 3, plan 4.2, plan 7.6, tests 19–21, 25
  - Depends on: T3
  - Done when:
    - `IPicktimeApiService.GetAvailableSlotsAsync` exists, with the slots response model, as in plan section 3.
    - It takes a `CancellationToken`.
    - The request carries every query parameter in spec section 5.1.
    - A malformed slots response is reported as a failed read, not thrown.
    - Tests 19–21 and 25 pass.
  - Verify: the standard test command
  - Notes:

- [ ] **T6 — Fix the booking request and parse its response**
  - Refs: spec 4.1 defect 1, spec 5.2, plan 2, plan 3, plan 3.1, plan 4.1, plan 7.4, plan 7.6, tests 22–24, 30
  - Depends on: T4
  - Done when:
    - `start_date_time` comes from `DateTimeOfBooking`.
    - The wire payload matches spec section 5.2 exactly. `alt_number_Ext` uses an explicit JSON property name. `booking_addnl_fields` is a constant, with no setting.
    - `CreateBookingAsync` returns a `BookingResult`, and takes a `CancellationToken`.
    - The result is `Succeeded`, `Rejected` or `Unknown`, classified as in plan section 4.1.
    - Response parsing has moved out of `PicktimeBookingService`. `BookingSuccessfulResponse` is kept.
    - Tests 22–24 and 30 pass. Test 22 is the regression test for defect 1.
  - Verify: the standard test command
  - Notes:

- [ ] **T7 — Booking date and season gate**
  - Refs: spec 6.2, spec 6.3, plan 3, plan 3.3, plan 7.2 rules 1, 3 and 4, plan 7.3, plan 7.5, tests 8, 15–18
  - Depends on: T3
  - Done when:
    - `BookArcheryIndoorTargetAsync` takes an optional `bookingDate` and a `CancellationToken`, as in plan section 3.
    - With no date, the booking date is London today plus `DaysAhead`, computed with the injected `TimeProvider`, not `DateTime.Today`.
    - The season gate is a pure function of the booking date.
    - Tests 8 and 15–18 pass.
  - Verify: the standard test command
  - Notes:

- [ ] **T8 — Rewrite the booking service**
  - Refs: spec 4.1 defects 2 and 3, spec 6.4, spec 6.5, spec 6.6, plan 3.4, plan 4.2, plan 7.3, tests 1–7, 9–11
  - Depends on: T4, T5, T6, T7
  - Done when:
    - `PicktimeBookingService` follows the algorithm in spec section 6.4, using the configured target chain.
    - Availability reads run concurrently. Booking POSTs run one at a time, in hour order.
    - A failed availability read treats that target as full, and logs a warning naming the target.
    - Each hour ends in exactly one outcome from spec section 6.5.
    - A failure on one hour never stops the other hours.
    - The timer trigger calls the new entry point with no date.
    - Tests 1–7 and 9–11 pass.
  - Verify: the standard test command
  - Notes:

- [ ] **T9 — Add the HTTP trigger**
  - Refs: spec 6.7, plan 3, plan 4.2, plan 6, plan 7.7, tests 27–29
  - Depends on: T8
  - Done when:
    - `POST /api/book` exists, protected by a function key.
    - It accepts an optional `bookingDate` and returns the run summary as JSON.
    - An invalid or past `bookingDate` returns HTTP 400 with the reason, and the booking service is not called.
    - It calls the same `BookArcheryIndoorTargetAsync` as the timer.
    - Both triggers catch and log an exception from the booking service, so the host does not crash.
    - Tests 27–29 pass.
  - Verify: the standard test command
  - Notes:

## Phase 3 — Prove it against the real API

Goal: one real booking made from a local run.

- [ ] **T10 — Find the minimum header set (manual)**
  - Refs: spec 5.4, spec 7.1, plan 2
  - Depends on: T6
  - Done when:
    - The Postman steps in spec section 7.1 are done, and the result is recorded in spec section 5.4.
    - Plan section 2 lists any header setting that turned out to be required.
    - The code sends the `scantoken` header plus only the required headers.
  - Verify: the standard test command, then T12
  - Notes: The Postman part is manual and can be done at any time. The code change needs T6.

- [ ] **T11 — Capture a rejected booking (manual)**
  - Refs: spec 5.2, spec 7.2, plan 4.2, test 24
  - Depends on: T6
  - Done when:
    - A real rejection is captured: HTTP status code, `status` and `message`.
    - It is recorded in spec section 5.2.
    - The table in plan section 4.2 and the test 24 fixture match it, and the "Provisional" note is removed.
  - Verify: the standard test command
  - Notes:

- [ ] **T12 — Make one real booking from a local run (manual)**
  - Refs: spec 6.7, plan 5.1
  - Depends on: T9, T10, T11
  - Done when:
    - A booking fired through the local HTTP trigger returns `Booked` with a booking id.
    - The booking shows on the Picktime site.
    - The Picktime confirmation email arrives.
  - Verify: `func start`, then `POST http://localhost:7071/api/book` with a `bookingDate`
  - Notes: This makes a real booking. Cancel it by hand if it is not wanted.

## Phase 4 — Resilience and logging

Goal: failures are handled safely, and every run can be understood from its logs.

- [ ] **T13 — Retry the availability read only**
  - Refs: plan 4.1, plan 4.2
  - Depends on: T8
  - Done when:
    - The availability `GET` retries up to 3 times on network error, timeout or HTTP 5xx, using `Microsoft.Extensions.Http.Resilience`.
    - The booking `POST` has no automatic retry, and the setup makes it impossible to add one by accident.
  - Verify: the standard test command, and a review of the HTTP client registration
  - Notes:

- [ ] **T14 — Handle an unknown booking result**
  - Refs: spec 6.4, spec 6.5, spec 9, plan 4.1, plan 7.4, tests 12–14
  - Depends on: T13
  - Done when:
    - An `Unknown` result re-reads availability for that target and hour, instead of resending.
    - The hour ends as `Unconfirmed` when it has gone, or when a second attempt is also `Unknown`. It is logged as a warning.
    - After an `Unknown` result, no other target is tried for that hour.
    - Tests 12–14 pass.
  - Verify: the standard test command
  - Notes: These tests prevent a duplicate booking. They are the most important tests in the suite.

- [ ] **T15 — Logging and observability**
  - Refs: spec 3 goal 5, spec 4.1 defect 9, spec 5.2, spec 5.3, spec 6.2, spec 6.3, plan 4.2, plan 5.1, plan 5.2, plan 5.3
  - Depends on: T8
  - Done when:
    - The worker sends logs to Application Insights through OpenTelemetry, set up as in plan section 5.2: the two packages, `Program.cs`, `host.json` and `appsettings.json`.
    - The exporter is registered only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, and `func start` works without it.
    - Host sampling is off in `host.json`, and no sampling is configured in the worker.
    - Each run logs the start line from spec section 6.2, including the season gate result. A skipped run logs why.
    - Each run logs availability per target, and each attempt with its outcome, booking id and API message.
    - `booking_email_confirmation` is logged per booking.
    - Logging is structured, with named placeholders.
    - Each run writes one structured summary event, as in plan section 5.3. `BookingLoggingExtensions` is the only place it is written.
    - A malformed response body is logged at Warning level, cut to its first 1 KB.
    - HTTP 401 or 403 logs an error that names authentication as the cause.
    - The `scantoken` is never logged. Checked by review.
  - Verify: the standard test command, then one local run through the HTTP trigger with the log output checked
  - Notes:

## Phase 5 — Infrastructure and deployment

Goal: the Function runs in Azure on the correct schedule, deployed by CI.

- [ ] **T16 — Create the Azure resources (manual)**
  - Refs: spec 8 assumption 8, plan 2, plan 3.3, plan 8.1, plan 8.3
  - Depends on: —
  - Done when:
    - The resources in plan section 8.1 exist, on a Windows Consumption plan in UK South, in a pay-as-you-go subscription.
    - Every application setting from plan sections 2 and 8.1 is set, including `WEBSITE_TIME_ZONE` and `BookingSchedule`.
    - A £1 monthly budget on the resource group emails an alert when actual cost reaches £1.
  - Verify: check the settings list in the portal (Function App → Environment variables), and the budget (Cost Management → Budgets)
  - Notes: Azure CLI is not installed. The portal works.

- [ ] **T17 — Add the GitHub Actions workflow**
  - Refs: spec 4.1 defect 8, spec 9, plan 6, plan 8.4
  - Depends on: T16
  - Done when:
    - One workflow builds and tests on push to `main` and on pull request.
    - It deploys only on `main`, and only when the tests pass.
    - The publish profile is a repository secret.
  - Verify: a pull request shows a green build, and the deploy job is skipped
  - Notes:

- [ ] **T18 — Deploy and check the schedule**
  - Refs: spec 6.1, spec 6.2, plan 3.3, plan 8.1
  - Depends on: T15, T17
  - Done when:
    - The Function is deployed from `main`.
    - The logs show the next scheduled run at 00:05 London time, not 00:05 UTC.
  - Verify: Function App → Log stream after deployment
  - Notes:

## Phase 6 — Verify in the season

Goal: the automation is proved in production and left running.

- [ ] **T19 — Book end to end in Azure (manual)**
  - Refs: spec 6.7, spec 9, plan 5.1
  - Depends on: T18
  - Done when: a booking fired through the Azure HTTP trigger shows on the Picktime site, and its confirmation email arrives.
  - Verify: `POST https://<function-app>.azurewebsites.net/api/book?code=<function key>`
  - Notes:

- [ ] **T20 — Pin the 90-day query (manual)**
  - Refs: spec 9, plan 5.2, plan 5.4, plan 8.3
  - Depends on: T19
  - Done when:
    - The plan section 5.4 query returns the T19 run with every column filled. This proves that named placeholders reach `customDimensions` and that nothing was sampled out.
    - The query is pinned to an Azure dashboard.
    - It is recorded whether the Invocations view shows data with OpenTelemetry enabled (plan section 5.2).
  - Verify: open the dashboard and see the T19 run with no empty columns
  - Notes:

- [ ] **T21 — Check one unattended scheduled run (manual)**
  - Refs: spec 3 goals 1–3, spec 6.1, spec 6.5, plan 5
  - Depends on: T19
  - Done when: after one scheduled run, the logs, the Picktime emails and the Picktime site all agree.
  - Verify: the plan section 5.4 query, the inbox and the Picktime site
  - Notes:

- [ ] **T22 — Rewrite the README**
  - Refs: spec 9
  - Depends on: T18
  - Done when: `README.md` says what the project does, how to run it locally, and which settings it needs.
  - Verify: follow the README from a fresh clone
  - Notes:

- [ ] **T23 — Close out**
  - Refs: spec 9
  - Depends on: T19–T22
  - Done when:
    - Every item in the spec's definition of done (spec section 9) is true.
    - Remaining items are recorded, or closed.
  - Verify: go through spec section 9 item by item
  - Notes: Decide whether `TODO.md` is kept as a backlog or deleted. Progress lives in this file.

---

## Blocked

<!-- Tasks that cannot start, and what they wait for. Remove when unblocked. -->

## Completed log

<!-- One line per completed task: date, task, pull request or commit. -->
