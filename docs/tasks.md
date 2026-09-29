# Picktime Automation — Tasks

> **What this document is for:** It answers *what do we do next?* It splits the approved design into small, verifiable steps and tracks progress against them.

Spec: [spec.md](spec.md)
Last updated: 2026-09-29

<!--
How to use this file
* This file holds the order of work and its progress. It does not repeat the design.
* In "Refs", section numbers refer to spec.md. "Test N" refers to the numbered tests in spec section 12.
* One task is one working session and one pull request.
* A task that grows beyond that is split into smaller tasks here.
* Tick a task only when every "Done when" item is true and "Verify" has passed.
* Record deviations from the spec in "Notes", and update spec.md to match.
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
  - Refs: 4.1 defect 6, 11
  - Depends on: —
  - Done when:
    - The line that matches `.gitignore` is removed.
    - `bin/`, `obj/` and `local.settings.json` are ignored.
    - Both ignore files are tracked by git.
  - Verify: `git check-ignore -v PicktimeAutomation/PicktimeAutomation.AzureFunctions/local.settings.json`, then `git ls-files "*.gitignore"`
  - Notes: Done first so that the upgrade in T2 cannot stage `bin/` or `obj/` by accident.

- [ ] **T2 — Upgrade to .NET 10**
  - Refs: 4.1 defect 7, 13.1
  - Depends on: T1
  - Done when:
    - All five projects target `net10.0`.
    - The Functions worker packages are on their current major version.
    - The solution builds with no errors.
    - The Function App starts locally and the timer trigger is listed.
  - Verify: `dotnet build PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx`, then `func start` in `PicktimeAutomation/PicktimeAutomation.AzureFunctions`
  - Notes: The start check needs Azure Functions Core Tools v4. Install it before this task. Spec 13.2 is updated to match.

- [ ] **T3 — Move configuration out of source**
  - Refs: 4.1 defect 4, 7, 11, 12.2 rule 5, test 26
  - Depends on: T2
  - Done when:
    - Options classes exist for the `Picktime`, `Archer` and `Booking` settings in section 7.
    - They are bound with the options pattern and validated at start-up.
    - A missing `Picktime:ScanToken` stops start-up with a clear message.
    - No token, account id, location id, target id, name or email is a literal in source.
    - The two `Test1.cs` placeholders are deleted.
    - Test 26 passes.
  - Verify: the standard test command, then `git grep -n "eyJ" -- "*.cs"` returns nothing
  - Notes:

## Phase 2 — Core booking logic

Goal: the booking rules in section 6 are implemented and covered by their tests.

- [ ] **T4 — Update the models**
  - Refs: 8.1, 6.5
  - Depends on: T3
  - Done when:
    - `BookingOutcome`, `BookingResult` and the `BookingRequest` record exist as in section 8.1.
    - `BookingAttempt` and `BookingSummary` can express per-hour outcomes, counts and an overall verdict.
    - The solution builds.
  - Verify: `dotnet build PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx`
  - Notes: Spec step 12. Moved before the API client work, because T6 returns `BookingResult`.

- [ ] **T5 — Read availability**
  - Refs: 5.1, 8, tests 19–21
  - Depends on: T3
  - Done when:
    - `IPicktimeApiService.GetAvailableSlotsAsync` exists, with the slots response model.
    - The request carries every query parameter in section 5.1.
    - Tests 19–21 pass.
  - Verify: the standard test command
  - Notes:

- [ ] **T6 — Fix the booking request and parse its response**
  - Refs: 4.1 defect 1, 5.2, 8, 8.1, tests 22–25
  - Depends on: T4
  - Done when:
    - `start_date_time` comes from `DateTimeOfBooking`.
    - The wire payload matches section 5.2 exactly, including `alt_number_Ext` and `booking_addnl_fields`.
    - `CreateBookingAsync` returns a parsed `BookingResult`. Response parsing has moved out of `PicktimeBookingService`.
    - Tests 22–25 pass. Test 22 is the regression test for defect 1.
  - Verify: the standard test command
  - Notes:

- [ ] **T7 — Booking date and season gate**
  - Refs: 6.2, 6.3, 12.2 rules 1 and 4, tests 8, 15–18
  - Depends on: T3
  - Done when:
    - The booking date is computed from London time using `TimeProvider`, not `DateTime.Today`.
    - The season gate is a pure function of the booking date.
    - Tests 8 and 15–18 pass.
  - Verify: the standard test command
  - Notes:

- [ ] **T8 — Rewrite the booking service**
  - Refs: 6.4, 6.5, 6.6, 9.2, tests 1–7, 9–11
  - Depends on: T4, T5, T6, T7
  - Done when:
    - `PicktimeBookingService` follows the algorithm in section 6.4.
    - Availability reads run concurrently. Booking POSTs run one at a time, in hour order.
    - Each hour ends in exactly one outcome from section 6.5.
    - A failure on one hour never stops the other hours.
    - Tests 1–7 and 9–11 pass.
  - Verify: the standard test command
  - Notes:

- [ ] **T9 — Add the HTTP trigger**
  - Refs: 6.7, 12.7, tests 27–28
  - Depends on: T8
  - Done when:
    - `POST /api/book` exists, protected by a function key.
    - It accepts an optional `bookingDate` and returns the run summary as JSON.
    - It calls the same `BookArcheryIndoorTargetAsync` as the timer.
    - Tests 27 and 28 pass.
  - Verify: the standard test command
  - Notes:

## Phase 3 — Prove it against the real API

Goal: one real booking made from a local run.

- [ ] **T10 — Find the minimum header set (manual)**
  - Refs: 5.4, 14.1, 7
  - Depends on: T6
  - Done when:
    - The Postman steps in section 14.1 are done, and the result is recorded in sections 5.4 and 7.
    - The code sends only the required headers.
  - Verify: the standard test command, then T12
  - Notes: The Postman part is manual and can be done at any time. The code change needs T6.

- [ ] **T11 — Capture a rejected booking (manual)**
  - Refs: 14.2, 9.2, test 24
  - Depends on: T6
  - Done when:
    - A real rejection is captured: HTTP status code, `status` and `message`.
    - It is recorded in section 5.2.
    - The error handling and the test 24 fixture match it.
  - Verify: the standard test command
  - Notes: The example slot in section 14.2 (17:00 on 2b, 29 September 2026) has passed. Use any slot that is already taken.

- [ ] **T12 — Make one real booking from a local run (manual)**
  - Refs: 6.7, 10.1
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
  - Refs: 9.1, 9.2
  - Depends on: T8
  - Done when:
    - The availability `GET` retries up to 3 times on network error, timeout or HTTP 5xx.
    - The booking `POST` has no automatic retry, and the setup makes it impossible to add one by accident.
  - Verify: the standard test command, and a review of the HTTP client registration
  - Notes:

- [ ] **T14 — Handle an ambiguous booking outcome**
  - Refs: 9.1, 12.4, tests 12–14
  - Depends on: T13
  - Done when:
    - An ambiguous `POST` failure re-reads availability instead of resending.
    - Tests 12–14 pass.
  - Verify: the standard test command
  - Notes: These tests prevent a duplicate booking. They are the most important tests in the suite.

- [ ] **T15 — Logging and observability**
  - Refs: 4.1 defect 9, 5.3, 6.2, 10.2, 10.3
  - Depends on: T8
  - Done when:
    - Application Insights sampling is off in `host.json`.
    - Each run logs the start line from section 6.2, availability per target, and each attempt.
    - Each run writes one structured summary event, as in section 10.3.
    - `booking_email_confirmation` is logged per booking.
    - HTTP 401 or 403 logs an error that names authentication as the cause.
    - The `scantoken` is never logged. Checked by review.
  - Verify: the standard test command, then one local run through the HTTP trigger with the log output checked
  - Notes: Spec steps 20–23.

## Phase 5 — Infrastructure and deployment

Goal: the Function runs in Azure on the correct schedule, deployed by CI.

- [ ] **T16 — Create the Azure resources (manual)**
  - Refs: 13.1, 7
  - Depends on: —
  - Done when:
    - The resources in section 13.1 exist, on a Windows Consumption plan in UK South.
    - Every application setting from sections 7 and 13.1 is set, including `WEBSITE_TIME_ZONE`.
  - Verify: check the settings list in the portal, Function App → Environment variables
  - Notes: Azure CLI is not installed. The portal works.

- [ ] **T17 — Add the GitHub Actions workflow**
  - Refs: 13.4
  - Depends on: T16
  - Done when:
    - One workflow builds and tests on push to `main` and on pull request.
    - It deploys only on `main`, and only when the tests pass.
    - The publish profile is a repository secret.
  - Verify: a pull request shows a green build, and the deploy job is skipped
  - Notes:

- [ ] **T18 — Deploy and check the schedule**
  - Refs: 6.2, 13.1
  - Depends on: T15, T17
  - Done when:
    - The Function is deployed from `main`.
    - The logs show the next scheduled run at 00:05 London time, not 00:05 UTC.
  - Verify: Function App → Log stream after deployment
  - Notes:

## Phase 6 — Verify in the season

Goal: the automation is proved in production and left running.

- [ ] **T19 — Book end to end in Azure (manual)**
  - Refs: 6.7, 10.1
  - Depends on: T18
  - Done when: a booking fired through the Azure HTTP trigger shows on the Picktime site, and its confirmation email arrives.
  - Verify: `POST https://<function-app>.azurewebsites.net/api/book?code=<function key>`
  - Notes:

- [ ] **T20 — Pin the season query (manual)**
  - Refs: 10.4
  - Depends on: T18
  - Done when: the section 10.4 query is pinned to an Azure dashboard.
  - Verify: open the dashboard and see the recent runs
  - Notes:

- [ ] **T21 — Check one unattended scheduled run (manual)**
  - Refs: 6.1, 6.5, 10
  - Depends on: T19
  - Done when: after one scheduled run, the logs, the Picktime emails and the Picktime site all agree.
  - Verify: the section 10.4 query, the inbox and the Picktime site
  - Notes:

- [ ] **T22 — Rewrite the README**
  - Refs: 17
  - Depends on: T18
  - Done when: `README.md` says what the project does, how to run it locally, and which settings it needs.
  - Verify: follow the README from a fresh clone
  - Notes:

- [ ] **T23 — Close out**
  - Refs: 17
  - Depends on: T19–T22
  - Done when:
    - Every item in the spec's definition of done (section 17) is true.
    - Remaining items are recorded, or closed.
  - Verify: go through section 17 item by item
  - Notes: Spec step 37 said to record the result in `TODO.md`. Progress now lives in this file. Decide whether `TODO.md` is kept as a backlog or deleted.

---

## Blocked

<!-- Tasks that cannot start, and what they wait for. Remove when unblocked. -->

## Completed log

<!-- One line per completed task: date, task, pull request or commit. -->
