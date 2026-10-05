# Picktime Automation — Tasks

> **What this document is for:** It answers *what do we do next?* It splits the approved design into small, verifiable steps and tracks progress against them.

Last updated: 2026-10-05

<!--
How to use this file
* This file holds the order of work and its progress. It does not repeat the requirements or the design.
* In "Refs", "spec 6.2" means spec.md section 6.2, and "plan 3.1" means plan.md section 3.1.
  "Test N" refers to the numbered tests in plan section 7.
* One task is one working session and one pull request.
* A task that grows beyond that is split into smaller tasks here.
* Tick a task only when every "Done when" item is true and "Verify" has passed.
* A task that changes behaviour also needs `/code-review` with no open blockers before it is ticked.
* Record deviations from the spec or plan in "Notes", and update that document to match.
* The first unticked task whose dependencies are done is the next task.
* Tasks marked (manual) are done by you, outside the code. Claude can prepare and check them.
-->

Unless a task says otherwise, run commands from the repository root. The standard test command is:

```
dotnet test PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx
```

**Before any local `func start`,** check that `local.settings.json` sets `AzureWebJobs.TargetBookingFunction.Disabled` to `true`. Without it, the timer can fire a missed run against the real Picktime API (plan section 8.2). `func start` must report "Function TargetBookingFunction is disabled".

## Phase 1 — Foundations

Goal: the solution runs on .NET 10, nothing sensitive is in source, and configuration is validated at start-up.

- [x] **T1 — Fix the self-ignoring `.gitignore`**
  - Refs: spec 4.1 defect 6, plan 6
  - Depends on: —
  - Done when:
    - The line that matches `.gitignore` is removed.
    - `bin/`, `obj/` and `local.settings.json` are ignored.
    - Both ignore files are tracked by git.
  - Verify: `git ls-files "*.gitignore"` lists both ignore files. This is the check that proves the fix; `git check-ignore -v PicktimeAutomation/PicktimeAutomation.AzureFunctions/local.settings.json` passes even before it.
  - Notes: Done first so that the upgrade in T2 cannot stage `bin/` or `obj/` by accident. Also ignores `TestResults/`, which the standard test command writes and which no ignore file covered outside the Functions project.

- [x] **T2 — Upgrade to .NET 10**
  - Refs: spec 4.1 defect 7, plan 3, plan 8.1, plan 8.2
  - Depends on: T1
  - Done when:
    - All five projects target `net10.0`.
    - The Functions worker packages are on their current major version.
    - `Program.cs` uses `FunctionsApplication.CreateBuilder(args)` with `ConfigureFunctionsWebApplication()`, as in plan section 3, with the package `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` that it needs.
    - The solution builds with no errors.
    - The Function App starts locally and the timer trigger is listed.
  - Verify: `dotnet build PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx`, then `func start` in `PicktimeAutomation/PicktimeAutomation.AzureFunctions`
  - Notes: The start check needs Azure Functions Core Tools v4. Install it before this task. Azurite must also be running before `func start`, as in plan section 8.2. Core Tools 4.14.0 and Azure CLI 2.90.0 were both installed by the time this task ran, so plan section 8.2 was updated to match. The same update replaced the `npm install -g azurite` route with Visual Studio's bundled Azurite, because this machine's Node.js v16.9.1 is below the minimum for current Azurite. Approved on 2026-10-01. Package versions: `Microsoft.Azure.Functions.Worker` 1.6.0 to 2.52.0, `.Worker.Sdk` 1.3.0 to 2.1.0, `.Worker.Extensions.Timer` 4.0.1 to 4.3.1, and `.Worker.Extensions.Http.AspNetCore` 2.1.1 added. No source change was needed beyond `Program.cs`.

- [x] **T3 — Move configuration out of source**
  - Refs: spec 3 goal 4, spec 4.1 defect 4, spec 5.3, spec 6.1, plan 2, plan 3, plan 6, plan 7.1, plan 7.2 rule 5, plan 7.8, test 35
  - Depends on: T2
  - Done when:
    - Options classes exist for the `Picktime`, `Archer` and `Booking` settings in plan section 2. There is no time zone setting.
    - They are bound with the options pattern, and checked at start-up against every validation rule in plan section 2, including `BookingSchedule`.
    - Service registration lives in `AddPicktimeServices(IServiceCollection)` in `PicktimeAutomation.Services`, called from `Program.cs`, as in plan section 3.
    - The timer trigger reads its schedule from the `BookingSchedule` setting, as `%BookingSchedule%`. The schedule is no longer written in code.
    - No token, account id, location id, target id, name, email or schedule is a literal in source.
    - `local.settings.json` holds every setting from plan section 2, with lists as flattened keys, and stays ignored.
    - The two `Test1.cs` placeholders are deleted.
    - `PicktimeAutomation.ServicesTests` references `PicktimeAutomation.Services`.
    - Test 35 passes.
  - Verify: the standard test command; then `git grep -n -i -E "eyJ|[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[a-z]{2,}|[0-9a-f]{8}-[0-9a-f]{4}-|oliver|bourne" -- "*.cs" "*.json"` returns nothing (token, email address, resource ids and the archer's name); then `func start` lists the timer trigger with the schedule from `BookingSchedule`
  - Notes: Options classes live in `PicktimeAutomation.Models`, which plan section 3 names as the home for configuration. One `IValidateOptions<T>` per class sits in `PicktimeAutomation.Services`, next to `AddPicktimeServices`. Three points to record:
    1. `AddPicktimeServices` takes `IConfiguration` as well as `IServiceCollection`. Plan section 3 names the method, not its parameter list. The configuration is needed to bind the sections and to check `BookingSchedule`, which plan section 2 requires to stop start-up and which is not bound to an options class.
    2. `Picktime:BrowserId` and `Picktime:Referer` are not added. Plan section 2 marks both "only if required", which spec section 5.4 settles in T12. Adding them now would mean settings that nothing reads.
    3. `func start` lists the timer trigger but does not print the NCRONTAB expression. That the trigger is indexed at all proves `%BookingSchedule%` resolved. It was confirmed by removing `BookingSchedule` and starting again: start-up stopped with "BookingSchedule must not be empty", and no function was listed. T9 adds the log line that states the schedule's last and next occurrence on every run.

## Phase 2 — Core booking logic and safety

Goal: the booking rules in spec section 6 are implemented, covered by their tests, and safe against duplicate bookings before any real booking is made.

- [x] **T4 — Update the models**
  - Refs: plan 3.1, spec 6.5
  - Depends on: T3
  - Done when:
    - `BookingOutcome`, `BookingResult` and the `BookingRequest` record exist as in plan section 3.1.
    - `BookingOutcome` includes `Unconfirmed`, and `BookingResult` has the three statuses `Succeeded`, `Rejected` and `Unknown`.
    - `BookingResult` carries `EmailConfirmationSent`.
    - `BookingAttempt` and `BookingSummary` can express per-hour outcomes and counts.
    - `BookingSummary` holds the booking date, `FailedReads`, and a `RunVerdict` with every value in plan section 3.1.
    - The solution builds.
  - Verify: `dotnet build PicktimeAutomation/PicktimeAutomation.AzureFunctions.slnx`
  - Notes: Moved before the API client work, because T6 returns `BookingResult`. Until T8, `PicktimeBookingService` keeps its old all-or-nothing rule, now written as the `Success` or `Failure` verdict. A booked hour also records its target name and booking id. `BookingAttempt` moved to its own file. Nothing uses `BookingResult` until T6.

- [x] **T5 — Read availability**
  - Refs: spec 4.1 defect 2, spec 5.1, spec 6.4, plan 3, plan 4.2, plan 7.6, tests 19–21, 25
  - Depends on: T3
  - Done when:
    - `IPicktimeApiService.GetAvailableSlotsAsync` exists, with the slots response model, as in plan section 3.
    - It takes a `CancellationToken`.
    - The request carries every query parameter in spec section 5.1.
    - A failed read throws `PicktimeReadException`, as in plan section 3. An empty list means only "fully booked".
    - Tests 19–21 and 25 pass.
  - Verify: the standard test command
  - Notes: A body with `"status": false` is a failed read and throws `PicktimeReadException`. Plan section 3 did not list it. Approved on 2026-10-02, and plan section 3 was updated to match. Any non-success HTTP status, including a 4xx, is also a failed read. Plan section 3 listed only HTTP 5xx. Approved on 2026-10-02, and plan section 3 was updated to match. T6 splits HTTP 401 and 403 into `PicktimeAuthenticationException`. A cancelled `CancellationToken` from the caller is not a failed read: `OperationCanceledException` passes through. `HttpClient`'s own timeout is a failed read. `TimeProvider.System` is registered in `AddPicktimeServices`, for the `_` cache-buster. The response model is `SlotsResponse`, in `PicktimeAutomation.Models`. It leaves out `metadata`, which spec section 5.1 says is not read. `PicktimeReadException` is in `PicktimeAutomation.Services`, in the `Exceptions` folder. The same pull request groups the Services files into `Validators`, `Interfaces` and `Exceptions` folders, each with a matching namespace. Requested during review on 2026-10-02. It changes no behaviour. The test project now references `Microsoft.Extensions.TimeProvider.Testing`, as plan section 7.2 rule 1 names. Besides tests 19–21 and 25, the tests cover the other failed reads in plan section 3 (HTTP status, network error, timeout) and the caller's cancellation. No logging (T15) and no retries (T10).

- [x] **T6 — Fix the booking request and parse its response**
  - Refs: spec 4.1 defect 1, spec 5.2, spec 5.3, plan 2, plan 3, plan 3.1, plan 4.1, plan 4.2, plan 7.4, plan 7.6, tests 22–24, 26, 30, 32
  - Depends on: T4, T5
  - Done when:
    - Before `start_date_time` is fixed: `local.settings.json` sets `AzureWebJobs.TargetBookingFunction.Disabled` to `true`, as in plan section 8.2. Fixing defect 1 makes booking requests valid, so from this task on a timer run would make real bookings.
    - `start_date_time` comes from `DateTimeOfBooking`.
    - The wire payload matches spec section 5.2 exactly. `alt_number_Ext` uses an explicit JSON property name. `booking_addnl_fields` is a constant, with no setting.
    - `CreateBookingAsync` returns a `BookingResult`, and takes a `CancellationToken`.
    - The result is `Succeeded`, `Rejected` or `Unknown`, classified as in plan section 4.1.
    - An HTTP 401 or 403 from either API call throws `PicktimeAuthenticationException`.
    - Response parsing has moved out of `PicktimeBookingService`. `BookingSuccessfulResponse` is kept.
    - Tests 22–24, 26, 30 and 32 pass. Test 22 is the regression test for defect 1. Test 26 covers both API calls.
  - Verify: the standard test command
  - Notes: The wire payload is `BookingPayload`, an internal record in `PicktimeAutomation.Services` with an explicit JSON name on every field. Test 22 was shown to fail when the old 12-field payload was put back. `PicktimeAuthenticationException` is in the `Exceptions` folder. Points to record:
    1. `BookingSuccessfulResponse.Status` is now `[JsonRequired]`. Without it, a body with no `status` would read as `false`, which is `Rejected` and falls through to the next target. Now it cannot be parsed, so it is `Unknown`.
    2. `status: true` with no `data.id` is `Succeeded` with no booking id, because spec section 5.2 makes `status` the authority.
    3. An HTTP 4xx keeps Picktime's `message`, read with `BookingUnsuccessfulResponse`. With no readable message, the message names the status code.
    4. Only HTTP 401 and 403 raise `PicktimeAuthenticationException`. Plan section 4.2 also names "a token rejection message", but its shape is unknown until T13 captures one. T13 must add it if the token rejection is not a 401 or 403.
    5. The caller's own cancellation throws `OperationCanceledException`, even after the POST was sent. Approved by the owner on 2026-10-02.
    6. Review points left for later tasks: the full success model is deserialised strictly, so an unexpected type in `data` makes a real success `Unknown`. This is safe, because it can never cause a second booking. The raw body is not logged yet (T15). `PicktimeBookingService` still formats `start_date_time` with the current culture. T7 replaces that date code.
    7. The shared test settings moved to `TestSettings`, with the base URL `https://picktime.invalid/`. That domain never resolves, so a test built from the real registration cannot reach Picktime.

- [x] **T7 — Booking date and season gate**
  - Refs: spec 6.2, spec 6.3, spec 6.5, plan 3, plan 3.1, plan 3.3, plan 7.2 rules 1, 3 and 4, plan 7.3, plan 7.5, tests 7, 8, 15–18
  - Depends on: T3, T4
  - Done when:
    - `BookArcheryIndoorTargetAsync` takes an optional `bookingDate` and a `CancellationToken`, as in plan section 3.
    - `LondonClock` exists in `PicktimeAutomation.Services`, as in plan section 3, built on the injected `TimeProvider`.
    - With no date, the booking date is `LondonClock.Today()` plus `DaysAhead`, not `DateTime.Today`.
    - The season gate is a pure function of the booking date.
    - A booking date outside the season returns a `BookingSummary` with the `Skipped` verdict, and makes no API calls.
    - The timer trigger calls the new entry point with no date, so the solution still builds.
    - Tests 7, 8 and 15–18 pass.
  - Verify: the standard test command
  - Notes: `LondonClock`, `SeasonGate` and `PicktimeTimestamp` are in the `Dates` folder of `PicktimeAutomation.Services`, with the namespace `PicktimeAutomation.Services.Dates`, which matches the `Validators`, `Interfaces` and `Exceptions` folders. The owner asked for the folder during review on 2026-10-05. `SeasonGate.IsInSeason(date, seasonStart, seasonEnd)` is a public static pure function. It takes the two settings as arguments, so it reads no configuration. All four worked examples in spec section 6.3 agree with the rule, and each is a test that runs the service at 00:05 London time. `LondonClock` is registered as a singleton. It looks up `Europe/London` in its constructor, not in a static field, after review. The Picktime timestamp is built in one internal helper, `PicktimeTimestamp`, with the invariant culture. `PicktimeApiService` now uses it for the slots request too, which changes no behaviour. A test shows that a Thai Buddhist culture still sends a Gregorian timestamp. The tests give `FakeTimeProvider` the UTC instant, with a zero offset, as `TimeProvider.System` does. With a `+01:00` start time, `GetLocalNow()` returns the time unchanged, and tests 15 and 17 passed against a clock that used the host time zone. With the fix, a host-clock bug fails tests 15, 17 and 18 and the two BST worked examples. Review points left for later tasks: (1) the timer trigger passes no `CancellationToken` yet. T9 adds it when it finishes the triggers. (2) `FakePicktimeApiService` returns no free slots, because the interim loop does not read availability. T8 must make the free slots configurable, or the date tests fail for reasons that have nothing to do with the date. (3) A skipped run does not yet log the booking date or the reason, which spec sections 6.2 and 6.3 require. T15 adds it. (4) `SeasonGate` repeats the `MM-dd` parse in `BookingOptionsValidator`. This was left, because the validator checks the format at start-up.

- [x] **T8 — Rewrite the booking service**
  - Refs: spec 4.1 defects 2 and 3, spec 5.3, spec 6.4, spec 6.5, spec 6.6, plan 3, plan 3.1, plan 3.4, plan 4.2, plan 7.3, tests 1–6, 9–11, 31
  - Depends on: T4, T5, T6, T7
  - Done when:
    - `PicktimeBookingService` follows the algorithm in spec section 6.4, using the configured target chain.
    - Availability reads run concurrently. Booking POSTs run one at a time, in hour order.
    - A `PicktimeReadException` treats that target as full, logs a warning naming the target, and adds it to `FailedReads`.
    - A `PicktimeAuthenticationException` from any call stops the run, as in plan section 4.2, with the `AuthenticationFailed` verdict.
    - An `Unknown` booking result records `Unconfirmed`, logs a warning, and tries no other target for that hour. This is the safe interim rule. T11 adds the re-read and the second attempt.
    - Each hour ends in exactly one outcome from spec section 6.5.
    - A failure on one hour never stops the other hours, except a rejected token.
    - Tests 1–6, 9–11 and 31 pass.
  - Verify: the standard test command
  - Notes: Tests 1–6, 9–11 and 31 are in `BookingAlgorithmTests`, with extra tests for the T8 interim rule for `Unknown`, a token rejected by a booking after an hour was booked, a token rejected alongside a failed read, the caller's cancellation, and the `Failure` verdict. Points to record:
    1. Any exception from one availability read, except a rejected token and the caller's cancellation, is a failed read. Plan section 3 named only `PicktimeReadException`, but spec section 6.4 says "if an availability read fails", and one unexpected error must not stop the other target being used. Found in review. Approved on 2026-10-05, and plan section 3 was updated to match.
    2. Every read is left to finish, so `FailedReads` is complete even when another read rejected the token. A rejected read token stops the run before any booking.
    3. An unexpected exception on an hour records `Failed` and tries no other target for that hour, because the POST may already have reached Picktime.
    4. `FakePicktimeApiService` now sets free hours, booking results and exceptions per target and hour. It can also hold the reads until both have started, which proves they run concurrently: the test fails when the reads are made sequential. The T7 tests give 2b every hour free. No assertion was removed, and the token test now also checks the reads.
    5. The test project references `Microsoft.Extensions.Diagnostics.Testing`, for `FakeLogger`, to check the two warnings.
    6. Review points left for T15: an authentication failure is not logged at error level (plan section 4.2), and an unexpected exception on an hour is recorded only as its message, with no log of the exception. T15 must log the exception in that catch block in `PicktimeBookingService`, because the summary does not hold it. When every free target rejects an hour, only the last rejection message is kept.

- [x] **T9 — Add the HTTP trigger and finish both triggers**
  - Refs: spec 6.1, spec 6.5, spec 6.7, plan 3, plan 3.1, plan 4.2, plan 5.3, plan 6, plan 7.7, tests 27–29, 34
  - Depends on: T8
  - Done when:
    - `POST /api/book` exists, protected by a function key, using ASP.NET Core integration (the package was added in T2).
    - It reads an optional `bookingDate` from the query string only, and returns the run summary as JSON with HTTP 200.
    - The past-date check uses `LondonClock`.
    - An invalid or past `bookingDate` returns HTTP 400 with the reason, and the booking service is not called.
    - It calls the same `BookArcheryIndoorTargetAsync` as the timer.
    - Unexpected exceptions are handled by the `ExceptionHandlingMiddleware` in plan section 3, registered in `Program.cs`. It logs the exception and writes the summary event with the `Error` verdict, and for the HTTP trigger returns HTTP 500 with no internal detail. The triggers contain no try/catch.
    - The timer trigger logs `IsPastDue` and the schedule's last and next occurrence on every run.
    - The timer trigger skips a run with `IsPastDue` set: it logs a warning, writes the summary event with the `Missed` verdict, and does not call the booking service.
    - Tests 27–29 and 34 pass.
  - Verify: the standard test command
  - Notes: Points to record:
    1. The HTTP trigger is its own function, `ManualBookingFunction`, in its own class. The timer keeps the name `TargetBookingFunction`, so the `AzureWebJobs.TargetBookingFunction.Disabled` setting still disables only the timer.
    2. Both triggers return ASP.NET Core `IResult` values: `Results.Json` for the summary, with enum values as names, and `Results.Problem` for HTTP 400 and 500. So every error is Problem Details. The owner approved Problem Details on 2026-10-05. The 400 detail names the reason, and for a past date it gives London today.
    3. A `bookingDate` that is present but empty, or given twice, is invalid, not ignored. A typo must never fall back to the default date.
    4. Test 27 uses a small fake `FunctionContext`, as planned. `GetHttpContext()` reads `Items["HttpRequestContext"]`, a key checked in `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` 2.1.1. If a package upgrade changes the key, test 27 fails, which is the right signal. The fallback (a separate handler class) was not needed.
    5. The middleware never handles the caller's cancellation: an `OperationCanceledException` while the function's token is cancelled is rethrown and records no `Error` run. A timeout with the token not cancelled is an `Error` run. Extra tests cover both. Plan section 3 was updated to record this. A run cancelled by the host is therefore not in the run record.
    6. The middleware is registered after `ConfigureFunctionsWebApplication()`, so it runs inside the ASP.NET Core proxy middleware and the HTTP context is available. It writes the 500 response directly, because the function's result is empty after an exception. For a function that is not triggered by HTTP, it rethrows after logging, so the host still records the invocation as failed and the Invocations view (plan section 5.2) is not green. Found in review, and plan section 3 was updated to match.
    7. Both triggers still write the summary event, as the timer did before. Plan section 5.3 says the booking service writes it for a normal run. T15 reshapes the event and decides where it is written.
    8. Tests 28 and 29 have extra rows: no date passes no date to the service, and today is accepted in GMT as well as in BST. An extra timer test checks that a run on time passes the host's token and no date. It was added in review.
    9. Review points left for later tasks: (1) on the Consumption plan, a cold start just after 00:05 may make an on-time run `IsPastDue`, which plan section 3 would skip as `Missed`. The owner chose a late-run window, added as T9a. T18 and T21 check `IsPastDue` on real scheduled runs. (2) `ScheduleStatus.Last` and `Next` are logged with no offset, so the log alone cannot show London or UTC. T15 should log the host time zone or an offset, for the check in plan section 8.1. (3) A future date with the wrong year passes the past-date check and the season gate. The spec rejects only past dates.

- [x] **T9a — Book a late run inside the late-run window**
  - Refs: spec 6.1, spec 6.6, spec 9, plan 2, plan 3, plan 3.1, plan 4.2, plan 7.5, plan 7.7, plan 7.8, tests 34–36
  - Depends on: T9
  - Done when:
    - `LateRunWindow` exists in the `Dates` folder of `PicktimeAutomation.Services`, as in plan section 3, and is registered in `AddPicktimeServices`.
    - `LondonClock` has `Now()`, built on the injected `TimeProvider`.
    - `BookingSchedule` is read with `NCrontab.Signed`, at its current stable version. An expression that does not parse stops start-up with a message naming the setting.
    - A timer run with `IsPastDue` set calls the booking service with no date when `LateRunWindow` allows it. Otherwise it records a `Missed` run, as before.
    - For a late run, the timer logs whether `LateRunWindow` allowed it.
    - Tests 34, 35 and 36 pass.
  - Verify: the standard test command
  - Notes: Added on 2026-10-05, after the T9 review found that a cold start on the Consumption plan may make an on-time run late. The spec and plan changes were approved by the owner on 2026-10-05, after `/review-plan`. The owner accepted the risk that a run started again after a restart books the fallback target (spec section 9, plan section 4.2), with the rule that no deployment is made between 00:05 and 01:00 on a run day. It is numbered T9a so that later task numbers do not change. Do it before the first scheduled run in Azure (T18).
    Points to record:
    1. `NCrontab.Signed` 3.4.0 is the current stable version (checked on NuGet on 2026-10-05).
    2. Every spec 6.1 worked example is a row of test 36, in BST and again in GMT. With a UTC clock instead of London time, 8 tests fail, so the tests catch a host-clock bug. An extra row shows that a late run before today's run time is missed.
    3. `LateRunWindow.IsValidSchedule` is the one parse rule, used by the start-up check and the constructor, so both read the schedule the same way. The occurrence search stops at now.
    4. Extra tests: `LateRunWindow` resolves from the real `AddPicktimeServices` registration, and the timer's run on time does not depend on the window.
    5. `/code-review` found 7 points. Points 2 to 6 are fixed. Point 7 (the schedule string repeated in two tests) is left, because the tests pin the schedule from plan section 2. Point 1: after an outage of more than one day, a late run that reaches the worker just after a run day's run time books, and that day's own run books the same date again. The owner accepted it on 2026-10-05 as the third exception in spec section 9, recorded in plan section 4.2.

- [x] **T10 — Retry the availability read only**
  - Refs: spec 9, plan 3, plan 4.1, plan 4.2, plan 7.2 rule 7, plan 7.6, test 33
  - Depends on: T8
  - Done when:
    - The availability `GET` retries up to 3 times on network error, timeout or HTTP 5xx, using `Microsoft.Extensions.Http.Resilience`. It never retries HTTP 401 or 403.
    - The booking `POST` has no automatic retry, and a 20-second timeout. Both clients are registered in `AddPicktimeServices`.
    - `host.json` sets `functionTimeout` to 10 minutes, as in plan section 4.1.
    - Test 33 passes, built from the real `AddPicktimeServices` registration.
  - Verify: the standard test command
  - Notes: Before any real booking (T14), because it sets the timeout that decides when a booking result is `Unknown`.
    1. `AddPicktimeServices` registers two named clients, as plan section 4.2 allows. `PicktimeRead` has the standard resilience handler with its defaults. `PicktimeBooking` has no handler and a 20-second timeout. Both get the base URL and the `scantoken` header from one method. `PicktimeApiService` takes `readClient` and `bookingClient`, so the GET and the POST cannot share a client.
    2. The standard handler's defaults also retry HTTP 408 and 429 on the GET. The owner approved this on 2026-10-05. It is recorded in plan section 4.1. HTTP 401 and 403 are never retried.
    3. When the handler stops a GET, it throws a Polly `ExecutionRejectedException`: `TimeoutRejectedException` for a timeout, or the circuit breaker's or rate limiter's exception. `GetAvailableSlotsAsync` turns each into `PicktimeReadException`, as plan section 3 requires. The caller's cancellation still passes through, and is not retried.
    4. Test 33 is in `PicktimeApiRegistrationTests`. Its HTTP 401 case is the existing test 32 read test, which uses the same registration and asserts one request. Extra tests: a GET timeout in the handler, the caller's cancellation through the handler, a booking network error sent once, a POST through the read client sent once, and the booking client's 20-second timeout. The tests set the retry backoff to zero with `ConfigureAll<HttpStandardResilienceOptions>`, so they do not depend on the handler's internal options name.
    5. A check by mutation: with the two clients swapped in the registration, test 33 fails in both directions. Without `DisableForUnsafeHttpMethods`, the read-client POST test fails.
    6. `/code-review` found 8 points and no blocker. Points 2 to 6 and 8 are fixed: the read client never retries a POST (`DisableForUnsafeHttpMethods`), every handler rejection becomes `PicktimeReadException`, the comments say "up to 3 retries", and a booking network error is tested through the real registration. Point 7 (build the service with `ActivatorUtilities`) is not done: it matches the two `HttpClient` arguments by order alone, and the named arguments are clearer. Point 1 is open for the owner: the read client keeps `HttpClient`'s default 100-second timeout, which still applies while the response body downloads, after the handler has returned. A body that stalls can make one read take up to 100 seconds, not the 30 in plan section 4.1. Plan section 4.1 says nothing is configured for the GET, so a fix, such as a 30-second `Timeout` on the read client, is a plan change.

- [x] **T11 — Handle an unknown booking result**
  - Refs: spec 6.4, spec 6.5, spec 9, plan 4.1, plan 7.4, tests 12–14
  - Depends on: T10
  - Done when:
    - An `Unknown` result follows the four steps in spec section 6.4, with the design details in plan section 4.1. It replaces T8's interim rule.
    - The request is never resent without first re-reading availability.
    - A failed re-read records `Unconfirmed`.
    - After an `Unknown` result, no other target is tried for that hour.
    - Tests 12–14 pass.
  - Verify: the standard test command
  - Notes: These tests prevent a duplicate booking. They are the most important tests in the suite. Before any real booking (T14).
    Points to record:
    1. The owner approved two rules on 2026-10-05 that the spec and plan did not cover. After an `Unknown` result an hour ends only as `Booked` or `Unconfirmed`. (a) A second attempt that is `Rejected` records `Unconfirmed`, because Picktime may now show the first request's booking. (b) A rejected token on the re-read records `Unconfirmed` for the hour, then stops the run with `AuthenticationFailed`. Both are in spec section 6.4 and plan section 4.1.
    2. The same principle is applied to the second attempt: a rejected token on it is handled as rule b, and an unexpected exception from it records `Unconfirmed`, not `Failed`. Plan section 4.2 points to section 4.1 for both.
    3. Any exception from the re-read, except a rejected token and the caller's cancellation, records `Unconfirmed`, as plan section 4.1 now says. A failed re-read is not added to `FailedReads`.
    4. A private `TokenRejectedAfterUnknownResultException` carries the hour's `Unconfirmed` outcome to the run, which then stops as for any rejected token.
    5. `FakePicktimeApiService` gained `WithFreeHoursOnReRead`, `WithReReadException` and `AvailabilityReads`. A booking exception is now thrown once the queued results for that target and hour are used, so a test can make the second attempt throw. No existing test set both.
    6. Two T8 tests changed, as expected. The interim-rule test is replaced by test 12. The `Unconfirmed` row of test 11 now makes the re-read show the hour as taken: with the hour still free, the second attempt books it.
    7. Test 14 and rule a are one data-driven test over the second result. Extra tests: rule b, a rejected token on the second attempt, an unexpected exception on the second attempt, a failed re-read (`PicktimeReadException` and an unexpected error), an unknown result on the fallback target 3a, and the caller's cancellation during the re-read.
    8. A check by mutation: resending without checking the re-read fails test 12 and test 11. Falling through to 3a on `Unknown` fails 11 tests. Re-reading the first free target, not the one that gave `Unknown`, fails the 3a test.
    9. `/code-review` found 9 points and no blocker. Points 1, 2, 5, 6, 7, 8 and 9 are fixed: every `Unconfirmed` reason keeps the first result's message, a test covers an unknown result on 3a, both stop-the-run paths share `StopForRejectedToken`, test 14 and rule a are one data-driven test, plan section 4.1 no longer says the request is never resent, the cancellation test cancels during the re-read, and the slot and the `Booked` attempt are not built twice. Point 4 (the private exception is control flow) is not done: a rejected token already stops the run through an exception, the wrapper keeps it as the inner exception, and a result type would change three method signatures and still need the existing catch. Point 3 is left for T15: a rejected token after an unknown result is logged only at Warning level, as for the other paths in T8 note 6.

## Phase 3 — Prove it against the real API

Goal: one real booking made from a local run, with the duplicate-booking protection already in place.

- [ ] **T12 — Find the minimum header set (manual)**
  - Refs: spec 5.4, spec 7.1, plan 2
  - Depends on: T6
  - Done when:
    - The Postman steps in spec section 7.1 are done, and the result is recorded in spec section 5.4.
    - Plan section 2 lists any header setting that turned out to be required.
    - The code sends the `scantoken` header plus only the required headers.
  - Verify: the standard test command, and the Postman result recorded in spec section 5.4
  - Notes: Do the Postman part early, before T5 and T6 if possible, so the API client is written against the real header set. The code change needs T6.

- [ ] **T13 — Capture rejected requests (manual)**
  - Refs: spec 5.2, spec 5.3, spec 7.2, plan 4.2, tests 24, 32
  - Depends on: T6
  - Done when:
    - A real slot-taken rejection is captured: HTTP status code, `status` and `message`. It is recorded in spec section 5.2.
    - A real token rejection is captured, from a request sent with a deliberately invalid `scantoken`. It is recorded in spec section 5.3.
    - The table in plan section 4.2 and the fixtures for tests 24 and 32 match them, and the "Provisional" note is removed.
  - Verify: the standard test command
  - Notes: Do the Postman part early, before T5 and T6 if possible, so the fixtures need no rework.

- [ ] **T14 — Make one real booking from a local run (manual)**
  - Refs: spec 6.7, spec 9, plan 5.1
  - Depends on: T9, T10, T11, T12, T13
  - Done when:
    - A booking fired through the local HTTP trigger returns `Booked` with a booking id.
    - The booking shows on the Picktime site.
    - The Picktime confirmation email arrives.
  - Verify: `func start`, then `POST http://localhost:7071/api/book?bookingDate=<yyyy-MM-dd>`
  - Notes: The trigger books every configured hour. For this test, set only `Booking:Hours:0` in `local.settings.json`, so it makes one booking, and restore the other hours afterwards. Choose a date within the season and the 7-day release window that you have not already booked by hand. Cancel the booking by hand if it is not wanted.

## Phase 4 — Logging

Goal: every run can be understood from its logs.

- [ ] **T15 — Logging and observability**
  - Refs: spec 3 goal 5, spec 4.1 defect 9, spec 5.2, spec 5.3, spec 6.2, spec 6.3, spec 6.5, plan 4.2, plan 5.1, plan 5.2, plan 5.3
  - Depends on: T8, T9
  - Done when:
    - The worker sends logs to Application Insights through OpenTelemetry, set up as in plan section 5.2: the two packages, `Program.cs`, `host.json` and `appsettings.json`.
    - The exporter is registered only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, and `func start` works without it.
    - Host sampling is off in `host.json`, and no sampling is configured in the worker.
    - Each run logs the start line from spec section 6.2, including the season gate result. A skipped run logs why.
    - Each run logs availability per target, and each attempt with its outcome, booking id and API message.
    - `booking_email_confirmation` is logged per booking, from `BookingResult.EmailConfirmationSent`.
    - Logging is structured, with named placeholders.
    - Every run writes one structured summary event, as in plan section 5.3, including `FailedReadCount`. That covers normal, skipped, `Missed` and `Error` runs. `BookingLoggingExtensions` is the only place it is written.
    - A malformed response body is logged at Warning level, cut to its first 1 KB.
    - HTTP 401 or 403 logs an error that names authentication as the cause.
    - The `scantoken` is never logged. Checked by review.
  - Verify: the standard test command, then one local run through the HTTP trigger with the log output checked
  - Notes:

## Phase 5 — Infrastructure and deployment

Goal: the Function runs in Azure on the correct schedule, deployed by CI.

- [ ] **T16 — Create the Azure resources (manual)**
  - Refs: spec 8 assumption 8, plan 2, plan 3.3, plan 6, plan 8.1, plan 8.3
  - Depends on: T12
  - Done when:
    - The resources in plan section 8.1 exist, on a Windows Consumption plan in UK South, in a pay-as-you-go subscription.
    - Every application setting from plan sections 2 and 8.1 is set, including `WEBSITE_TIME_ZONE` and `BookingSchedule`. Lists use the flattened keys in plan section 2.
    - `AzureWebJobs.TargetBookingFunction.Disabled` is **not** set in Azure. It is a local-only setting (plan section 2).
    - A £1 monthly budget on the resource group emails an alert when actual cost reaches £1.
    - Application Insights is workspace-based, and its Log Analytics workspace has a daily cap of 0.1 GB.
    - The user-assigned managed identity exists, with a federated credential for this repository's `main` branch and the Website Contributor role on the Function App.
    - SCM basic authentication is off, and HTTPS Only is on, on the Function App.
  - Verify: check the settings list in the portal (Function App → Environment variables), the budget (Cost Management → Budgets), and the identity's federated credential and role
  - Notes: Azure CLI is not installed. The portal works. The Function App's Deployment Center can create the managed identity and its federated credential; choose "User-assigned identity", not "Basic authentication".

- [ ] **T17 — Add the GitHub Actions workflow**
  - Refs: spec 4.1 defect 8, spec 9, plan 6, plan 8.1, plan 8.4
  - Depends on: T16
  - Done when:
    - One workflow builds and tests on push to `main` and on pull request, on `windows-latest` with .NET `10.0.x`.
    - It deploys only on `main`, and only when the tests pass.
    - It signs in with OpenID Connect, as in plan section 8.4. No deployment secret is stored in GitHub.
    - The client id, tenant id and subscription id are GitHub repository variables.
  - Verify: a pull request shows a green build and the deploy job is skipped; a push to `main` deploys; SCM basic authentication is still off
  - Notes:

- [ ] **T18 — Deploy and check the schedule**
  - Refs: spec 6.1, spec 6.2, plan 3, plan 3.3, plan 8.1
  - Depends on: T9a, T15, T17
  - Done when:
    - The Function is deployed from `main`.
    - The logs show the next scheduled run at 00:05 London time, not 00:05 UTC. This is the check for the trigger time in spec section 6.2.
    - The first scheduled run after deployment logs `IsPastDue`, and, if it is late, whether the late-run window allowed it.
  - Verify: Function App → Log stream after deployment
  - Notes:

## Phase 6 — Verify in the season

Goal: the automation is proved in production and left running.

- [ ] **T19 — Book end to end in Azure (manual)**
  - Refs: spec 6.7, spec 9, plan 5.1
  - Depends on: T18
  - Done when: a booking fired through the Azure HTTP trigger shows on the Picktime site, and its confirmation email arrives.
  - Verify: `POST https://<function-app>.azurewebsites.net/api/book?bookingDate=<yyyy-MM-dd>`, with the function key in the `x-functions-key` header
  - Notes: As in T14, set only `Booking:Hours:0` in the Azure app settings for this test, so it makes one booking, then restore the other hours. Choose a date you have not already booked. Cancel the booking by hand if it is not wanted.

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
  - Refs: spec 3 goals 1–3, spec 6.1, spec 6.5, plan 3, plan 5
  - Depends on: T19
  - Done when:
    - After one scheduled run, the logs, the Picktime emails and the Picktime site all agree.
    - The run's log shows `IsPastDue` was false, or that it was true and the late-run window allowed the run, so a normal wake-up is never recorded as `Missed`.
  - Verify: the plan section 5.4 query, the run's log lines, the inbox and the Picktime site
  - Notes:

- [ ] **T22 — Rewrite the README**
  - Refs: spec 9
  - Depends on: T18
  - Done when:
    - `README.md` says what the project does, how to run it locally, and which settings it needs.
    - It states the operating rules in spec section 9: the manual trigger is not used between 00:05 and 01:00 on a run day, or for a booking date already booked, and no deployment is made between 00:05 and 01:00 on a run day.
  - Verify: follow the README from a fresh clone
  - Notes:

- [ ] **T23 — Close out**
  - Refs: spec 9
  - Depends on: T19–T22
  - Done when:
    - Every item in the spec's definition of done (spec section 9) is true.
    - Remaining items are recorded, or closed.
  - Verify: go through spec section 9 item by item
  - Notes: `TODO.md` was deleted on 2026-10-01. It held only an unfilled template, so nothing was lost. Progress lives in this file.

---

## Blocked

<!-- Tasks that cannot start, and what they wait for. Remove when unblocked. -->

## Completed log

<!-- One line per completed task: date, task, pull request or commit. -->

* 2026-10-01 — T1 — Fix the self-ignoring `.gitignore` — branch `task/t1-fix-gitignore`
* 2026-10-01 — T2 — Upgrade to .NET 10 — branch `task/t2-upgrade-dotnet-10`
* 2026-10-01 — T3 — Move configuration out of source — branch `task/t3-move-config-out-of-source`
* 2026-10-02 — T4 — Update the models — branch `task/t4-update-models`
* 2026-10-02 — T5 — Read availability — branch `task/t5-read-availability`
* 2026-10-02 — T6 — Fix the booking request and parse its response — branch `task/t6-fix-booking-request`
* 2026-10-03 — T7 — Booking date and season gate — branch `task/t7-booking-date-season-gate`
* 2026-10-05 — T8 — Rewrite the booking service — branch `task/t8-rewrite-booking-service`
* 2026-10-05 — T9 — Add the HTTP trigger and finish both triggers — branch `task/t9-triggers`
* 2026-10-05 — T9a — Book a late run inside the late-run window — branch `task/t9a-late-run-window`
* 2026-10-05 — T10 — Retry the availability read only — branch `task/t10-retry-availability-read`
* 2026-10-05 — T11 — Handle an unknown booking result — branch `task/t11-unknown-booking-result`
