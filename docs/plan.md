# Picktime Automation — Plan

> **What this document is for:** It answers *how will we build it?* It turns the requirements in `spec.md` into a technical design, and it is agreed before any code is written.

Status: Approved
Last updated: 2026-10-09

This document is the record of design decisions for this project. It says how the requirements in spec.md are met, and why that way. It does not restate requirements: it refers to them by section, such as "spec section 6.2". It was split out of spec.md on 2026-09-29. Keep it updated as decisions change.

---

## 1. Approach

The automation is an Azure Function on a Consumption plan, within the free grant. A timer trigger and an HTTP trigger both call one booking service. The booking service applies the rules in spec section 6, and reaches Picktime only through an API client. All settings come from configuration.

The booking rules have no HTTP or Azure Functions dependency. That keeps them covered by ordinary unit tests, and keeps both triggers thin adapters with no logic of their own.

---

## 2. Configuration

No secret or personal detail stays in source. Local development uses `local.settings.json`. Azure uses Function App application settings.

| Setting | Example | Notes |
| --- | --- | --- |
| `Picktime:BaseUrl` | `https://www.picktime.com/` | |
| `Picktime:ScanToken` | *(secret)* | The `scantoken` header value |
| `Picktime:AccountId` | `4fcc15b7-…` | |
| `Picktime:LocationId` | `dd0a2b7e-…` | |
| `Archer:FirstName` | `Oliver` | |
| `Archer:LastName` | `Bourne` | |
| `Archer:Email` | *(personal)* | |
| `Booking:DaysAhead` | `7` | |
| `Booking:Hours` | `[17, 18, 19]` | |
| `Booking:Targets` | `[{ "Name": "2b", "ResourceId": "…" }, { "Name": "3a", "ResourceId": "…" }]` | In preference order |
| `Booking:SeasonStart` | `10-01` | Month and day |
| `Booking:SeasonEnd` | `03-31` | Month and day |
| `BookingSchedule` | `0 5 0 * * TUE,THU,FRI` | The timer's NCRONTAB expression: 00:05 on Tuesday, Thursday and Friday. See below. |
| `AzureWebJobs.TargetBookingFunction.Disabled` | `true` | **Local only.** Stops the timer firing during local runs (section 8.2). Never set it in Azure. |

Bind these with the options pattern and validate them at start-up, so a missing token fails immediately and loudly rather than at 00:05. `BookingSchedule` is the one exception. See below.

**Validation rules.** Start-up stops with a message that names the setting if any rule fails. Test 35 covers each rule.

| Setting | Rule |
| --- | --- |
| `Picktime:BaseUrl` | An absolute `https` URL |
| `Picktime:ScanToken`, `Picktime:AccountId`, `Picktime:LocationId` | Not empty |
| `Archer:FirstName`, `Archer:LastName` | Not empty |
| `Archer:Email` | Not empty, and contains `@` |
| `Booking:DaysAhead` | A whole number from 1 to 14 |
| `Booking:Hours` | At least one hour. Each is a whole number from 0 to 23, with no duplicates. |
| `Booking:Targets` | At least one target. Each has a non-empty `Name` and `ResourceId`, and names are unique. |
| `Booking:SeasonStart`, `Booking:SeasonEnd` | Valid `MM-dd` dates. The season may wrap the year end, as 1 October to 31 March does. |
| `BookingSchedule` | Not empty, and a valid NCRONTAB expression with a seconds field (below) |

The example values for days ahead, hours, targets and season come from spec sections 6.1 and 6.3. `DaysAhead` and the season dates are settings even though spec section 3, goal 4 does not require it: they cost nothing, and Picktime's release window or the club's season could change.

There is deliberately no time zone setting. The club is in London, so `Europe/London` is one constant in `LondonClock` (section 3), not something to configure.

**Lists are written as flattened keys.** `local.settings.json` and the Azure app settings hold only flat text values, so `Booking:Hours` and `Booking:Targets` cannot be written as lists. Write one key per item, with its position as a number. The options binding reads them back as lists.

| Key | Value |
| --- | --- |
| `Booking:Hours:0` | `17` |
| `Booking:Hours:1` | `18` |
| `Booking:Hours:2` | `19` |
| `Booking:Targets:0:Name` | `2b` |
| `Booking:Targets:0:ResourceId` | `<2b resource id>` |
| `Booking:Targets:1:Name` | `3a` |
| `Booking:Targets:1:ResourceId` | `<3a resource id>` |

The `:` separator works on Windows, which this project uses (section 8.1). A further target is added as `Booking:Targets:2:Name` and `Booking:Targets:2:ResourceId`, which is still a configuration change, as spec section 3, goal 4 requires. A single text value such as `"17,18,19"` was rejected: it needs custom parsing, and it cannot hold the name and id pairs.

The API client sends only the `scantoken` header, on both calls. The booking body is sent as `application/json; charset=utf-8`, with no cache-buster on the URL. This is the minimum set that spec section 5.4 records, so no header setting is required. There is deliberately no `Picktime:BrowserId` or `Picktime:Referer` setting: spec section 5.4 shows neither is needed, and a setting that nothing reads is only one more thing to keep correct.

There is deliberately no setting for `booking_addnl_fields`. It is a constant, because it never varies (spec section 8, assumption 3).

**The schedule is a setting, not code.** The timer trigger reads it as `[TimerTrigger("%BookingSchedule%")]`, so the run days and time change without a code change, as spec section 3, goal 4 requires. Today the expression is written directly in `TargetBookingFunction.cs`, which does not meet that goal.

* `BookingSchedule` is the single source of truth for when the automation runs. There is deliberately no separate `RunDays` setting, because a second value describing the same schedule would be a second place to change and a second place to get wrong.
* The name is flat, with no `Booking:` prefix, because the Functions host reads it directly. It is not bound to the options classes.
* Without a check, a missing `BookingSchedule` would only put the timer function in an error state, while the HTTP trigger kept running, which is easy to miss. So the start-up validation in `AddPicktimeServices` also checks that `BookingSchedule` is set, and stops start-up if it is not. It also parses the expression, because the late-run window in section 3 reads it, so an expression that does not parse stops start-up. It requires the six-field form, with seconds. The host also accepts five fields, but this setting has always had six, and one accepted form keeps the window's parse and the host's parse the same. A valid expression with the wrong time or days is caught by the trigger time check after deployment, in section 8.1.

---

## 3. Architecture

Keep the existing five-project layout. It is a sensible separation and there is no reason to churn it.

```
PicktimeAutomation.AzureFunctions   Timer trigger, HTTP test trigger, DI wiring, logging
PicktimeAutomation.Services         Picktime API client, booking rules
PicktimeAutomation.Models           Requests, responses, configuration, results
PicktimeAutomation.ServicesTests    Unit tests for the two services
PicktimeAutomation.AzureFunctionsTests  Unit tests for the functions
```

**Target framework.** Upgrade all five projects from .NET 6 to .NET 10. Upgrade the Azure Functions worker packages (`Microsoft.Azure.Functions.Worker` and its extensions) to their current major version. This fixes defect 7 in spec section 4.1. The isolated worker model supports .NET 10 until 14 November 2028, which covers this season and the next two. Checked on 2026-09-30.

Interface changes:

```csharp
public interface IPicktimeApiService
{
    Task<IReadOnlyList<long>> GetAvailableSlotsAsync(string resourceId, DateOnly date, CancellationToken ct);
    Task<BookingResult> CreateBookingAsync(BookingRequest request, CancellationToken ct);
}
```

Two changes of note against the current code:

1. `CreateBookingAsync` returns a parsed result rather than a raw `string`. Response parsing belongs with the client that knows the wire format, not in the booking rules. This moves `ParseBookingApiResponse` out of `PicktimeBookingService`.
2. Both methods take a `CancellationToken`, which the Functions host supplies.

**How `GetAvailableSlotsAsync` reports a failed read.** An empty list means the target is fully booked. A read that fails, after its retries, throws `PicktimeReadException`. That covers a network error, a timeout, any non-success HTTP status except 401 and 403, a body that cannot be parsed, or a body with `"status": false`. HTTP 401 and 403 throw `PicktimeAuthenticationException` instead (section 4.2). In practice the read never returns them, because it does not check the token (spec section 5.3). The handling stays as a safeguard, in case Picktime starts to check it. So a failed read can never be mistaken for a fully booked day, as spec section 6.4 requires. The booking service catches the exception, logs a warning naming the target, treats the target as having no free hours, and adds the target's name to `BookingSummary.FailedReads` (section 3.1). This matches how an authentication failure is reported, with `PicktimeAuthenticationException`.

The booking service treats any other exception from one read the same way, as a failed read. The only exceptions are `PicktimeAuthenticationException`, which stops the run (section 4.2), and the caller's cancellation, which is never caught. An unexpected error leaves the free slots just as unknown, and spec section 6.4 requires that one target that cannot be read does not stop the others from being used. Every read is left to finish before a rejected token stops the run, so `FailedReads` is complete.

`IPicktimeBookingService` keeps one entry point. The booking date is optional:

```csharp
Task<BookingSummary> BookArcheryIndoorTargetAsync(
    DateOnly? bookingDate = null,
    CancellationToken ct = default);
```

When no date is supplied, the service computes it from the injected `TimeProvider` and the configured `DaysAhead`. The timer trigger supplies nothing; the HTTP trigger supplies a date only when one was requested.

That placement is deliberate. If the trigger computed the date, the date rule — the thing most likely to be wrong, per spec section 6.2 — would live in an Azure Functions entry point, which is awkward to unit test. Keeping it inside the service means both triggers are thin adapters with no logic of their own, and the rule is covered by ordinary unit tests.

**Service registration** lives in one extension method, `AddPicktimeServices(IServiceCollection, IConfiguration)`, in the `PicktimeAutomation.Services` project, which `Program.cs` calls. `LondonClock` is in the same project, so `PicktimeAutomation.ServicesTests` can test both. This includes the options, the services and both HTTP clients with their retry policies. Tests build the real registrations from the same method, so test 33 checks the configuration that actually runs (section 7.2, rule 7).

**Host setup and HTTP model.** `Program.cs` uses `FunctionsApplication.CreateBuilder(args)` with `ConfigureFunctionsWebApplication()`, which is ASP.NET Core integration, with the package `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`. It replaces the current `new HostBuilder()` with `ConfigureFunctionsWorkerDefaults()`, for two reasons:

1. `CreateBuilder` loads `appsettings.json` automatically. With `new HostBuilder()` it is not loaded, so the worker log levels in section 5.2 would be silently ignored.
2. The HTTP trigger takes an ASP.NET Core `HttpRequest`, which tests 27–29 build from a plain `DefaultHttpContext`. The built-in `HttpRequestData` model needs a mocked `FunctionContext` instead.

**The timer trigger** logs `TimerInfo.IsPastDue` and the schedule's last and next occurrence on every run, so a wrong value is visible from the first scheduled run. If `IsPastDue` is set, the run has started late. It still books when it is inside the late-run window that spec section 6.1 requires. Otherwise the trigger does not call the booking service: it logs a warning, and writes the summary event with the `Missed` verdict (section 3.1). A late run inside the window books exactly like a run on time, with no date, so the service computes London today plus `DaysAhead`. For a late run, the trigger logs whether the window allowed it, so a late run that booked is visible in the logs.

**`LateRunWindow`** is one small injected class in the `Dates` folder of `PicktimeAutomation.Services`, with `bool AllowsLateRun()`. Its constructor takes `LondonClock` and the raw `BookingSchedule` string, and parses the string once. It returns `true` only when both are true:

1. London time now is before 01:00, a constant, because spec section 6.1 sets it.
2. The `BookingSchedule` expression has an occurrence on London today, at or before London time now. That makes London today a run day, which gives the worked examples in spec section 6.1, including the outage case.

`LateRunWindow` gets the time from `LondonClock`, which gains a `Now()` method next to `Today()`, so it is testable with `FakeTimeProvider`. `AddPicktimeServices` registers it as a singleton.

The 01:00 cut-off assumes a run time just after midnight. If `BookingSchedule` moves the run time to 01:00 or later, every late run is `Missed`. That failure is safe, because it books nothing, and the warning shows it.

The expression is read with `NCrontab.Signed`, the library the Functions host uses to read the timer schedule. So the window check and the host read the schedule the same way. The host reads it in London time, because `WEBSITE_TIME_ZONE` is set (section 8.1), and the window check reads it in London time too. The package is free and open source, and adds no cost.

Two alternatives were rejected:

* Reading the missed occurrence from `TimerInfo.ScheduleStatus`. Microsoft's documentation does not say what it holds on a late run, and its times carry no offset (checked on 2026-10-05). A rule built on it could not be tested without a real late run.
* A separate setting for the run days. Section 2 makes `BookingSchedule` the single source of truth for when the automation runs.

**The HTTP trigger** reads `bookingDate` from the query string only, as spec section 6.7 requires. It checks the date before calling the booking service. It returns the `BookingSummary` as JSON with HTTP 200, and HTTP 400 for an invalid or past date. An unexpected error is handled by the exception middleware below.

**Unexpected errors are handled in one place:** an `ExceptionHandlingMiddleware`, registered in `Program.cs` with `UseMiddleware`, wraps every function. When a function throws, it:

1. Logs the exception at error level.
2. Writes the summary event with the `Error` verdict (section 3.1), through `BookingLoggingExtensions`. The booking date is left empty, because the middleware does not know it.
3. For an HTTP function, returns HTTP 500 with the short message in section 4.2, and no internal detail. For any other function, such as the timer, it rethrows the exception, so the host still records the invocation as failed.

It never handles the caller's cancellation: an `OperationCanceledException` while the function's cancellation token is cancelled is rethrown, and writes no summary event. Approved by the owner on 2026-10-05. A timeout, which also throws `OperationCanceledException` while the token is not cancelled, is an unexpected error and is handled as above.

The triggers contain no try/catch, so they stay thin adapters, and a function added later is covered with no extra code. Note that in the isolated worker model an unhandled exception fails one run, not the host. The middleware exists for the run record and the safe HTTP response, not to keep the host alive.

A try/catch in each trigger was rejected: it repeats the same handling in two places, and the owner's C# standard says to handle unexpected exceptions in one place, which for Azure Functions is a worker middleware.

**`LondonClock`** is one small injected class with `DateOnly Today()`, and `DateTime Now()` for the late-run window (section 3). `Now()` returns London local time as a `DateTime`, not a `DateTimeOffset`, because `NCrontab.Signed` works on `DateTime` values in the schedule's own time zone. It is built on the injected `TimeProvider` and the constant `Europe/London`, resolved with `TimeZoneInfo.FindSystemTimeZoneById`. The booking service uses it for the default booking date, and the HTTP trigger uses it for the past-date check. So the date rule stays in one place (section 7.2, rule 3), and both uses are testable with `FakeTimeProvider`.

### 3.1 Model changes

**`BookingRequest` stays minimal.** It holds only what varies per booking:

```csharp
public sealed record BookingRequest(long DateTimeOfBooking, string ResourceId);
```

The other 23 payload fields from spec section 5.2 do not belong on this model. They are either fixed constants or come from configuration, so `PicktimeApiService` composes the wire payload from `BookingRequest` plus its injected options. Putting form fields such as `birth_month_date` on a domain model would spread Picktime's form shape across the whole solution.

The wire payload serialises `alt_number_Ext` with an explicit JSON property name attribute, because the name does not match C# naming convention.

**`BookingSummary` and `BookingAttempt` need to grow.** They currently carry only `Success` and `ErrorMessage`, which cannot express the outcomes in spec section 6.5:

```csharp
public enum BookingOutcome { Booked, NoAvailability, Failed, Unconfirmed }

public sealed class BookingAttempt
{
    public int Hour { get; init; }
    public BookingOutcome Outcome { get; init; }
    public string? TargetName { get; init; }   // set when Booked or Unconfirmed
    public string? BookingId { get; init; }    // set when Booked
    public string? ErrorMessage { get; init; } // set when Failed or Unconfirmed
}
```

`BookingSummary` holds the booking date, its list of attempts, counts per outcome, `FailedReads` (the names of targets whose availability read failed), and a verdict. It replaces the single `Success` boolean, so a partial result reads as partial, as spec section 6.5 requires.

```csharp
public enum RunVerdict { Success, Partial, Failure, Skipped, AuthenticationFailed, Missed, Error }
```

| Verdict | When |
| --- | --- |
| `AuthenticationFailed` | Any call raised `PicktimeAuthenticationException` (section 4.2). This overrides every other verdict, as spec section 6.4 requires. The run stops. Hours already finished keep their outcome. Hours not yet finished record `Failed`, with authentication as the reason. |
| `Success` | All configured hours are `Booked`. |
| `Partial` | At least one hour is `Booked` or `Unconfirmed`, but not all configured hours are `Booked`. |
| `Failure` | No hour is `Booked` or `Unconfirmed`. |
| `Skipped` | The booking date is outside the season. No hours are attempted. |
| `Missed` | A late timer run outside the late-run window (section 3). The booking service is not called, and the booking date is left empty. |
| `Error` | The exception middleware (section 3) caught an unexpected exception. The booking date is left empty. |

A skipped run still returns a `BookingSummary`, with zero counts, and still writes the summary event in section 5.3. So it appears in the section 5.4 query like any other run, as spec section 6.5 requires. The HTTP trigger returns it with HTTP 200. `Missed` and `Error` runs also write the summary event, so every run appears in the query, whatever its outcome.

**`BookingResult`** is the new return type of `CreateBookingAsync`. It holds a status, the booking id from `data.id`, the API's `message`, and `EmailConfirmationSent`, read from `booking_email_confirmation`. The status has three values, defined in section 4.1:

```csharp
public enum BookingResultStatus { Succeeded, Rejected, Unknown }
```

The booking service uses the status to decide what happens next. `Rejected` falls through to the next target. `Unknown` follows section 4.1 and never falls through.

**`BookingSuccessfulResponse`** in `PicktimeAutomation.Models` already models the success response in spec section 5.2 correctly. Keep it.

### 3.2 Designed to extend

The likely extensions, and what each would cost. Anything in the first group is configuration only.

| Extension | Cost |
| --- | --- |
| Use more of the 8 targets | Add their ids to `Booking:Targets` in preference order. No code change. |
| Different hours | Change `Booking:Hours`. No code change. |
| Different days | Change the `BookingSchedule` setting. No code change. |
| A different booking lead time | Change `Booking:DaysAhead`. No code change. |
| An alert when a run books nothing | An Azure Monitor alert on the section 5.4 query. No code change. |
| Email or push notification from the run itself | A notification service injected into the booking service. Small, and section 5.5 anticipates it. |
| Booking for a second archer | The archer details are single-valued in configuration. Would need a collection and a loop. Moderate. |
| Cancelling a booking | Needs its Picktime endpoint captured first, the same way the two current endpoints were. |

The two design choices that keep this open are the configurable target chain and keeping the booking rules free of any HTTP or Azure Functions dependency. The rules are an ordinary class that takes a date and returns a summary, so it can be driven from a timer, an HTTP request, a test, or something not yet thought of.

### 3.3 Time zone handling

Spec section 6.2 requires both the trigger time and the booking date to use London time. They are handled separately, and both are needed:

1. **The trigger time.** Set the Function App setting `WEBSITE_TIME_ZONE` to `GMT Standard Time`, so the `BookingSchedule` expression is evaluated in London time. Despite its name, that Windows id means UK time including British Summer Time. It is not fixed to GMT. A Windows plan is used for this reason — see section 8.1.
2. **The date arithmetic.** Compute the booking date from the current London time, not from `DateTime.Today`, which is UTC on the host. `LondonClock` (section 3) does this with `TimeZoneInfo.FindSystemTimeZoneById("Europe/London")`, which .NET resolves on both Linux and Windows.

The date arithmetic is covered by the tests in section 7.5. The trigger time depends on the `WEBSITE_TIME_ZONE` setting, so it cannot be unit tested. It is checked after deployment instead (section 8.1).

### 3.4 Execution order

The measured booking call took 3.64 seconds, and a Consumption-plan cold start adds several more. A fully sequential run is therefore roughly 20 seconds from trigger to last booking.

* The two availability reads are independent, so run them concurrently.
* The three booking POSTs run **sequentially**, in hour order. They are not parallelised. Sending them together would save a few seconds, but it also means three in-flight non-idempotent writes with no way to reason about what happened if the run faults midway. Twenty seconds after midnight is still far ahead of any human competing for the same slot, so the simplicity is worth more than the seconds.

---

## 4. Error handling and retries

### 4.1 The retry rule that matters

**The two endpoints must not share a retry policy.**

Reading availability is a `GET`. It is safe to repeat, so retry it freely.

Creating a booking is a `POST`, and it is **not idempotent**. If the request times out, the booking may already have been created on Picktime's side. A blind retry then books the same slot twice, and there is no cancellation path in this automation to undo it. A generic "retry on timeout" policy applied to both endpoints would silently cause exactly that.

So:

| Endpoint | Retry policy |
| --- | --- |
| `GET /ia/slots` | Retry up to 3 times on network error, timeout or HTTP 5xx, with the standard resilience handler's exponential backoff, which starts at 2 seconds. The handler's defaults also retry HTTP 408 and 429, which the owner approved on 2026-10-05. HTTP 401 and 403 are never retried. |
| `POST /ia/save/event` | **No automatic retry.** An unknown result is handled as below. |

**What makes a booking result `Unknown`.** `CreateBookingAsync` returns `Unknown` for any of these, because the booking may exist:

* A timeout, or a dropped connection.
* Any HTTP 5xx, with or without a body.
* An HTTP 2xx whose body cannot be parsed.

It returns `Succeeded` for a parsed `status: true`, and `Rejected` for a parsed `status: false` or an HTTP 4xx. HTTP 401 and 403 are also authentication failures (section 4.2).

**Handling an `Unknown` result** follows the four steps in spec section 6.4. The request is never resent without a fresh read, and is resent at most once, to the same target. Design details:

* The re-read is a call to `GetAvailableSlotsAsync` for that target and the booking date, checked for that hour.
* Every `Unconfirmed` case logs a warning, with the hour, booking date, target name and reason as named placeholders.
* If the re-read itself fails, with `PicktimeReadException` or any other exception, the hour records `Unconfirmed`. An unexpected exception from the second attempt also records `Unconfirmed`, not `Failed` as in section 4.2. The booking may exist, so no other target is tried. A failed re-read is not added to `FailedReads`, which lists only the reads made once per run.
* A second attempt that returns `Rejected` records `Unconfirmed`, as spec section 6.4 rule a requires.
* A `PicktimeAuthenticationException` from the re-read or the second attempt records `Unconfirmed` for this hour, then stops the run with the `AuthenticationFailed` verdict, as section 4.2 says. This meets spec section 6.4 rule b. Hours not yet finished record `Failed`. A private exception in `PicktimeBookingService` carries the hour's `Unconfirmed` outcome to the run, so the run keeps it.
* The caller's cancellation is never caught.

That turns an unsafe retry into a safe one, using the availability endpoint we already have. It never holds two bookings for the same hour, as spec section 9 requires. The cost is that, rarely, an hour that 3a could have filled is lost.

**Timeouts.** Each is chosen, not left to a default, because the booking timeout decides when a result becomes `Unknown`.

| Timeout | Value | Reason |
| --- | --- | --- |
| Booking `POST` | 20 seconds | About 5 times the measured 3.64 seconds, so a slow booking that succeeds is not wrongly marked `Unknown`. Short enough that a hung request does not stall the run. `HttpClient`'s default of 100 seconds is not used. Confirmed on 2026-10-07 by the first real booking from the code. The whole manual run, one availability read plus the booking, took 7.58 seconds, so the booking took at most that. The Postman bookings in spec section 5.4 took 6.28 and 6.69 seconds. 20 seconds is still more than 2.5 times the slowest of these. |
| Availability `GET` | The standard resilience handler defaults: 10 seconds per attempt, 30 seconds in total. Also a 35-second `Timeout` on the read client. | Reads are quick and safe to repeat, and the handler defaults suit them. The handler returns when the response headers arrive, so it does not limit the body download. The handler also sets the client's own `Timeout` to infinite, so without this setting a stalled body can hold a read until the function timeout. The setting is applied after the handler is added, so the handler does not overwrite it. The client timeout counts from the start of the request, retries included. At 35 seconds, just above the handler's 30, the handler still stops a normal timeout first, and the client timeout stops only a stalled body. Decided by the owner on 2026-10-06. |
| Whole run | `functionTimeout` of 10 minutes in `host.json` | The Consumption plan defaults to 5 minutes, with a maximum of 10 (checked on 2026-09-30). Worst case, with every call timing out, is 35 seconds of reads, which run concurrently, plus, for each of 3 hours, two attempts and one re-read (75 seconds): about 4.5 minutes. 10 minutes leaves room for a cold start. |

An HTTP-triggered function must respond within 230 seconds, whatever `functionTimeout` says. In the worst case above, a manual run loses its HTTP response, but the run itself continues to the end and logs as normal.

### 4.2 Everything else

The first two rows match the rejections captured on 2026-10-06. A rejected token returns HTTP 401 on a booking (spec section 5.3). A taken slot returns HTTP 200 with `status: false` (spec section 5.2). Tests 24 and 32 use the captured bodies. The availability read does not check the token, so in practice it never returns HTTP 401 or 403. Its handling stays as a safeguard. No HTTP 403 has been seen, and it is handled the same way as a safeguard.

| Situation | Behaviour |
| --- | --- |
| HTTP 401 or 403 | The API client throws `PicktimeAuthenticationException`, from either call, with no retry. The booking service catches it, stops the run, and sets the `AuthenticationFailed` verdict. Hours not yet finished record `Failed`, with authentication as the reason (section 3.1). An hour with an unknown booking result records `Unconfirmed` instead (section 4.1). Log at error level, naming authentication as the cause. This meets spec sections 5.3 and 6.4. |
| `status: false` — slot taken | No retry. Fall through to the next target, as spec section 6.4 requires. |
| Malformed or empty body from the booking `POST` | The result is `Unknown`. Handle as section 4.1. Log the raw body at Warning level, cut to its first 1 KB. |
| Malformed or empty body from the availability `GET` | The read has failed: throw `PicktimeReadException` (section 3). Log the raw body at Warning level, cut to its first 1 KB. |
| An availability read fails after retries | The API client throws `PicktimeReadException`. The booking service handles it as in section 3, which meets spec section 6.4. |
| An hour throws unexpectedly | Catch, record `Failed`, and continue to the next hour. This meets spec section 6.5. After an unknown booking result, record `Unconfirmed` instead (section 4.1). |
| Invalid or past `bookingDate` on the HTTP trigger | Return HTTP 400 with the reason, and do not call the booking service. This meets spec section 6.7. |
| Unexpected error in any function | Handled by the exception middleware (section 3): it logs the exception and writes the `Error` summary event. For the HTTP trigger it returns HTTP 500 with a short message: "The booking run failed. See the logs." No stack trace or internal detail is returned. This meets spec section 6.7. |

**Accepted risk: a manual run at the same time as a scheduled run.** Two runs in parallel could both see an hour as free and both book it. Guarding against it would need a lock shared between runs. It is accepted instead, because only one person uses the manual trigger.

**Accepted risk: a scheduled run run again after a restart.** The host saves the timer's schedule status only after the function finishes. If the host stops during a run and restarts before 01:00, the same run starts again, late but inside the late-run window (section 3), and can book the fallback target for hours the first run booked. Guarding against it would need a record of earlier bookings, which is a new store and new design. It is accepted instead, because the host must stop during a run that takes seconds.

**Accepted risk: a late run just before a run day's own run.** After an outage of more than one day, the host can start a few seconds before the run time on a run day. Its late run for the earlier missed day reaches the worker just after the run time, so the late-run window (section 3) allows it, and the day's own run then books the same date again. The window cannot tell the two runs apart without the timer's schedule status, which section 3 rejects. It is accepted, because it needs both a long outage and a host start within seconds of the run time.

Spec section 9 records the three exceptions, and the rules for when the manual trigger is used and when deployments are made.

**Raw response bodies in logs** may contain the archer's name or email. This is accepted, as recorded in section 6.

Retries on the `GET` use the standard `Microsoft.Extensions.Http.Resilience` handler. Because the policy differs per endpoint, either register two named clients, or register the handler only for the availability path. Whichever is chosen, it must be impossible to accidentally pick up an automatic retry on the booking POST. Two named clients are registered: a read client with the handler and the 35-second timeout, and a booking client with no handler and the 20-second timeout. The handler is added to the read client only, never through `ConfigureHttpClientDefaults`, which would add it to both. The handler is also set never to retry a `POST`, so a booking sent through the read client by mistake is still sent once. Test 33 fails if the two are swapped.

---

## 5. Knowing whether it worked

There are two channels, and they answer different questions.

### 5.1 Picktime's confirmation email — the quick check

Picktime sends a confirmation email for every booking it accepts. This is free, needs no work, and already exists. It is the primary day-to-day check.

A healthy run produces **three emails**, one per hour booked.

The booking response even reports this. `CreateBookingData.booking_email_confirmation` says whether Picktime intended to send one, so the automation logs that flag per booking. If emails stop arriving but the logs say `booking_email_confirmation: true`, the problem is at Picktime's end, not ours.

The limitation is that this channel only reports success. Picktime sends nothing when the automation books nothing, so a failed run is signalled by the **absence** of email — and an absence is easy not to notice. That is what the logs are for.

### 5.2 Azure logs — free, and quick to reach

Application Insights stays within its free allowance. See section 8.3.

**How the logs get there.** The Functions host and the .NET worker log separately. Settings in `host.json` do not affect logs from the worker, and every booking log line comes from the worker. So the worker sends its logs directly to Application Insights through OpenTelemetry, as Microsoft's isolated worker guide recommends. Relaying through the host, the default, is not used: it gives no guarantee that named placeholders arrive as `customDimensions`, which the query in section 5.4 depends on.

| Where | Setting |
| --- | --- |
| Packages | `Microsoft.Azure.Functions.Worker.OpenTelemetry` and `Azure.Monitor.OpenTelemetry.Exporter` |
| `Program.cs` | `AddOpenTelemetry().UseFunctionsWorkerDefaults().UseAzureMonitorExporter()`, in one extension method that `Program.cs` calls, so a test can read the options. Register all three only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, so local runs work without Azure. `UseFunctionsWorkerDefaults` tells the host to stop relaying the worker's logs, so without the exporter it would leave the logs nowhere. With no connection string, the worker relays its logs through the host, which prints them in the `func start` console (found in the first T15 Verify run, 2026-10-08). Give the exporter a fixed 100% sampling rate (`SamplingRatio = 1.0f` and `TracesPerSecond = null`), and turn off its trace-based log sampler (`EnableTraceBasedLogsSampler = false`), so no log depends on a trace sampling decision. |
| `host.json` | `"telemetryMode": "OpenTelemetry"`. In this mode the `logging.applicationInsights` section, including `samplingSettings`, does not apply (Microsoft Learn, "Use OpenTelemetry with Azure Functions", checked on 2026-10-06). So that setting no longer fixes defect 9 in spec section 4.1. Still set `samplingSettings.isEnabled` to `false`, so sampling stays off if the mode is ever removed. `functionTimeout` is set here too (section 4.1). |
| `appsettings.json` | Worker log levels: `Default` at `Information`, `Microsoft` at `Warning`. Worker log levels are set here, not in `host.json`. |

Defect 9 is fixed because nothing is sampled out, in the host or in the worker. Sampling is designed for high volume, and at three runs a week there is nothing to gain from it. The exporter samples by default: from version 1.6.0 it keeps at most 5 traces per second, and from 1.5.0 a log can be dropped with its trace (the exporter's changelog, checked on 2026-10-07). So the worker replaces that default with a fixed 100% rate, as the changelog describes, and turns off the log sampler, so logs are kept even if a sampler is set later through the `OTEL_TRACES_SAMPLER` settings. A test checks the settings. The first run in Azure is still checked: the section 5.4 query must return it with every column filled.

The libraries are free and open source. The logs go to the same Application Insights resource, within the same free allowance (section 8.3), so this choice adds no cost.

Two ways to reach the logs, in increasing order of effort:

| Route | Where | Use it for |
| --- | --- | --- |
| **Invocations** | Function App → Functions → the function → Invocations | The normal check. A row per run, click through for that run's log lines. |
| **Logs (KQL)** | Application Insights → Logs | History, and the pinned dashboard query below. |

Log stream in the portal is not available. When the host uses OpenTelemetry, the Azure portal does not support log streaming (Microsoft Learn, "Use OpenTelemetry with Azure Functions", checked on 2026-10-06). A manual run returns its summary in the HTTP response, and its log lines are in Logs (KQL) after it finishes.

After the first run in Azure, confirm that the Invocations view shows data with OpenTelemetry enabled. If it does not, Logs (KQL) becomes the normal check.

### 5.3 What each run logs

1. **Start** — the times and booking date required by spec section 6.2, and the season gate result required by spec section 6.3. A `Missed` run never reaches the booking service, so the timer trigger logs the same times and booking date itself. The timer trigger also logs the schedule's last and next times with their UTC offset, so the log shows whether they are London time or UTC.
2. **Availability** — the free hours found for each target.
3. **Each attempt** — hour, target name, outcome, the booking id on success, and `booking_email_confirmation`. An hour that throws unexpectedly (section 4.2) logs the exception itself, not only its message, because the summary does not hold it.
4. **Summary** — one event, described below. It is written once for every run, whatever the outcome: by the trigger that started the run, after the booking service returns, for a normal or skipped run; by the timer trigger for a `Missed` run; and by the exception middleware for an `Error` run (section 3). So every run appears in the run record, as spec section 6.5 requires.

Use structured logging with named placeholders throughout, so values land in `customDimensions` and are queryable. Keep `BookingLoggingExtensions` as the single place summaries are written. Both triggers and the exception middleware call it.

**Why the triggers write the summary, not the booking service.** The booking service returns the `BookingSummary`, and the HTTP trigger also returns it as JSON. `BookingLoggingExtensions` is in the Functions project, which the services project does not reference. The `Missed` and `Error` summaries must be written in the Functions project anyway, because the booking service never runs for them. So every summary is written in one layer. Moving the normal and skipped summaries into the booking service was rejected on 2026-10-06: it splits the writers across two projects, and a trigger call left in place during the move would count every run twice. Test 37 checks that each run writes exactly one summary.

**The summary is one event with named properties**, not a sentence of interpolated text. That is what makes the dashboard query below possible:

```csharp
logger.LogInformation(
    "Booking run finished. Date={BookingDate} Booked={BookedCount} " +
    "NoAvailability={NoAvailabilityCount} Failed={FailedCount} " +
    "Unconfirmed={UnconfirmedCount} FailedReads={FailedReadCount} Verdict={Verdict}",
    bookingDate, booked, noAvailability, failed, unconfirmed, failedReads, verdict);
```

Never log the `scantoken`.

### 5.4 The last 90 days at a glance

Paste this into Application Insights → Logs, then **Pin to dashboard**. After that, every run in the last 90 days is one click away:

```kusto
traces
| where timestamp > ago(90d)
| where message startswith "Booking run finished"
| extend BookingDate = tostring(customDimensions.BookingDate),
         Booked      = toint(customDimensions.BookedCount),
         Unavailable = toint(customDimensions.NoAvailabilityCount),
         Failed      = toint(customDimensions.FailedCount),
         Unconfirmed = toint(customDimensions.UnconfirmedCount),
         FailedReads = toint(customDimensions.FailedReadCount),
         Verdict     = tostring(customDimensions.Verdict)
| project timestamp, BookingDate, Booked, Unavailable, Failed, Unconfirmed, FailedReads, Verdict
| order by timestamp desc
```

Up to 90 days of runs, with any row that did not book every configured hour obvious at a glance. The window matches the free retention period in section 8.3. Older runs are not kept. Add `| where Verdict != "Success"` to see only the runs worth investigating. A `FailedReads` value above zero means Picktime could not be read, not that the day was full. For any row with `Unconfirmed` above zero, check the Picktime confirmation emails for that date.

### 5.5 Residual risk

With email plus logs, the remaining gap is narrow but real: a run that books nothing sends no email and writes a log nobody reads.

Three things reduce it, and it is accepted rather than solved:

1. Three missing emails on a shooting night is a noticeable signal in itself.
2. An authentication failure logs at error level and names the cause, so the reason is immediate once anyone looks.
3. The HTTP trigger checks the automation on demand, returning the summary as JSON without waiting for a scheduled run.

One more gap is accepted: a bad token can go unnoticed on a run where no hour is free. The availability read does not check the token (spec section 5.3), so the token is checked only when a booking is tried. When every target is full, no booking is tried, and the run reports a normal result. The bad token shows on the next run that tries a booking.

Adding a push or email alert later is a small change. An Azure Monitor alert on the query in section 5.4 would do it with no code at all, and is the natural first extension.

---

## 6. Security

| Item | Decision |
| --- | --- |
| `scantoken` in git history | Low severity, and accepted. Spec section 5.3 shows it is anonymous and does not expire, so it grants nothing the public does not already have, and rotation would achieve nothing. |
| `scantoken` going forward | Move to configuration, and to a Function App application setting in Azure. Not in source. |
| Email address | Move to configuration. The repository is public, so it should not be a literal in source. |
| Name and email in git history | Accepted on 2026-09-30, by the owner's choice. The archer's name and email were literals in committed code, so they stay in git history after they move to configuration. The name also appears as example values in section 2. Rewriting history was rejected: it is destructive, and GitHub keeps the old commits in merged pull requests anyway. |
| Name and email in logs | Accepted on 2026-10-06, by the owner's choice, as an exception to the owner's C# standard, which says not to log personal data. A malformed response body is logged at Warning level, cut to its first 1 KB (section 4.2), and a booking response can hold the archer's name and email. The raw text is what makes a malformed body possible to diagnose. The data is the owner's own, it is already public in git history (row above), and the logs stay in the owner's private Application Insights resource for up to 90 days (section 8.3). Only these two fields are accepted. The `scantoken` is never logged. |
| `.gitignore` | Remove the self-ignoring line, then commit both ignore files. This matters because the token will live in `local.settings.json`. |
| `local.settings.json` | Stays ignored. Never committed. |
| Deployment credentials | GitHub Actions signs in to Azure with OpenID Connect (section 8.4). No deployment secret is stored in GitHub, and SCM basic authentication stays off on the Function App. A publish profile was rejected: Microsoft marks it "not recommended", and it needs basic authentication switched on, which Microsoft says makes the app less secure (checked on 2026-09-30). |
| HTTP test trigger | A function key gives the protection required by spec section 6.7. The key is sent in the `x-functions-key` header, not the URL, so it does not appear in browser history or logs of URLs. |
| HTTPS | HTTPS Only is on for the Function App, so the function key is never sent unencrypted. |
| Logging the token | Check at code review that the `scantoken` is never logged. One narrow test covers the logs on the API path — see section 7.6. |

---

## 7. Testing

Keep MSTest, which both test projects already use. Replace the `Test1.cs` placeholders.

### 7.1 Principles

**There is no coverage target, and coverage is not reported.** A percentage rewards testing trivia and says nothing about whether the logic is right.

Every test below exists because it catches a specific defect, and each is listed with the defect it catches. If a test cannot be justified that way, it does not get written. Consequences worth stating plainly:

* Thin adapters are not tested for being thin. The two Azure Function triggers hold no logic beyond two small checks (the HTTP trigger's input check and the timer's late-run check), and the exception middleware has one job. Together they get four tests, not a suite.
* No test asserts a property getter, a constructor, or that a mock was called in a particular order.
* If `PicktimeAutomation.AzureFunctionsTests` ends up with nothing worth asserting, delete the project rather than pad it.

### 7.2 Design for testability

Testability is a design constraint here, not something retrofitted. Seven rules, each of which removes a reason a test would otherwise be hard to write:

1. **Use `TimeProvider`, never `DateTime.Now` or `DateTime.Today`.** .NET 10 ships `TimeProvider` and `FakeTimeProvider` (in `Microsoft.Extensions.TimeProvider.Testing`), so the BST and GMT cases in section 7.5 are ordinary unit tests with no custom clock abstraction to invent.
2. **All HTTP sits behind `IPicktimeApiService`.** `PicktimeBookingService` never touches `HttpClient`, so the booking rules are testable with a hand-written fake and no message-handler plumbing.
3. **The date rule lives in the service, not the trigger.** See section 3. Logic in an Azure Functions entry point is logic that is painful to reach from a test.
4. **Decisions are pure functions where they can be.** The season gate takes a date and returns a verdict. It reads no clock, no configuration and no ambient state, so its test is a table of dates.
5. **Configuration arrives as injected options objects,** not as `IConfiguration` lookups scattered through the code. A test constructs the options it needs.
6. **Methods return inspectable results.** `BookArcheryIndoorTargetAsync` returns a `BookingSummary` describing every hour, so a test asserts the returned outcome rather than reading log output.
7. **Registration is shared, not copied.** `AddPicktimeServices` is the only place services and HTTP clients are registered (section 3). A test that needs the real wiring calls it, rather than repeating the registration and drifting from it.

Rule 6 is what makes most of section 7.3 possible at all. The current `BookingSummary` cannot express a per-hour outcome, which is why section 3.1 grows it.

### 7.3 Booking rules — `PicktimeAutomation.ServicesTests`

The highest-value group. These cover the logic that decides what gets booked, driven by a fake `IPicktimeApiService`. Each test names the defect it catches.

| # | Test | Defect it catches |
| --- | --- | --- |
| 1 | All three hours free on 2b, so all three book on 2b | The happy path silently books the wrong target or the wrong number of hours |
| 2 | 18:00 taken on 2b but free on 3a, so 17:00 and 19:00 book on 2b and 18:00 books on 3a | Fallback applied per session instead of per hour — the exact rule that was ambiguous when we started |
| 3 | An hour taken on both targets records `NoAvailability` and does not stop the other hours | One unavailable hour aborts the run, losing the other two |
| 4 | Availability says free but the booking is rejected, so the next target is tried | A lost race is treated as fatal instead of falling through |
| 5 | Both targets reject the booking, so the hour records `Failed` | `Failed` and `NoAvailability` conflated, hiding why nothing was booked |
| 6 | A throw on one hour does not prevent the other two from booking | One exception costs all three slots |
| 7 | A booking date outside the season makes no API calls at all | Out-of-season runs hammer the API, or worse, book something |
| 8 | Season boundaries: 1 October and 31 March inside; 30 September and 1 April outside | An off-by-one that loses the first or last night of the season |
| 9 | The availability read for 2b fails, so 2b is treated as full and all three hours book on 3a | A single failed read costs the whole run |
| 10 | Both availability reads fail, so every hour records `NoAvailability`, nothing is booked, and `FailedReads` lists both targets | A blind booking attempt after a failed read, or a failed read reported as a fully booked day |
| 11 | Two hours booked and one not is reported as partial, not as failure | A mostly-successful run reads as a total failure in the logs |
| 31 | An availability read raises `PicktimeAuthenticationException`, so the run stops: no booking is attempted, every hour records `Failed` with authentication as the reason, and the verdict is `AuthenticationFailed` | A rejected token looks like a fully booked night |

### 7.4 Unknown booking results — section 4.1

The tests that stop a duplicate booking. The most valuable in the suite, because the defect they prevent cannot be undone once it happens. Drive tests 12–14 with a fake API service that returns `Unknown`, then a controlled availability response. Test 30 uses a stub `HttpMessageHandler`.

| # | Test | Defect it catches |
| --- | --- | --- |
| 12 | A booking returns `Unknown` and the hour has gone from availability, so the outcome is `Unconfirmed`, **no second booking is attempted, and 3a is not tried** | Two bookings for the same hour |
| 13 | A booking returns `Unknown` and the hour is still free, so exactly one further attempt is made on the same target | Giving up on a slot that was never actually booked |
| 14 | A booking returns `Unknown` twice, so the outcome is `Unconfirmed`, no third attempt is made, and 3a is not tried | Falling through to 3a when the first target may be booked, or an unbounded retry loop |
| 30 | `CreateBookingAsync` returns `Unknown`, not `Rejected`, for a timeout, an HTTP 5xx with and without a body, and an HTTP 2xx with an unreadable body | An uncertain result treated as a rejection, which falls through to 3a |

### 7.5 The date rule and time zones

Spec section 6.2 calls this the most likely source of a silent, wrong-day booking, so it gets its own group. All five use `FakeTimeProvider` with an explicit time zone, so they are deterministic and do not depend on the machine running them.

| # | Test | Defect it catches |
| --- | --- | --- |
| 15 | 00:05 London on Tuesday 6 October 2026 books Tuesday 13 October 2026, even though the host clock reads 23:05 UTC on 5 October | The BST off-by-one-day booking |
| 16 | A run during GMT computes the booking date correctly | A fix for BST that breaks the rest of the season |
| 17 | A run at 00:05 London on Friday 23 October 2026, in BST, books Friday 30 October 2026, in GMT, across the clock change on Sunday 25 October | The edge case nobody tests by hand |
| 18 | With no date supplied, the service computes London today plus the configured `DaysAhead` | The default path diverging from the explicit one |
| 36 | `LateRunWindow` gives the result in each worked example in spec section 6.1, with the scheduled run in BST, and once more in GMT | A run delayed by start-up books nothing, or a late run on an unscheduled day books the wrong date |

### 7.6 API client — `PicktimeAutomation.ServicesTests`

Use a stub `HttpMessageHandler`. These are worth writing despite looking like wire-format trivia: every one of them is a failure that is invisible until a booking silently does not happen.

| # | Test | Defect it catches |
| --- | --- | --- |
| 19 | `GetAvailableSlotsAsync` builds the URL with every query parameter from spec section 5.1, and the right `dateAndTime` and `endDate` | One wrong or missing parameter returns the wrong day's availability, or none |
| 20 | A slots response parses into the free hours | Availability misread, so everything downstream is wrong |
| 21 | An empty `data` array yields no free hours and is not an error | A fully booked day treated as a fault |
| 22 | `CreateBookingAsync` posts every field in the spec section 5.2 payload with the exact names — including `alt_number_Ext` and the nested-JSON string in `booking_addnl_fields` — and `start_date_time` taken from `DateTimeOfBooking` | **Regression test for defect 1**, plus the two field-name traps |
| 23 | A successful response parses as success and captures the booking id from `data.id` | A real booking recorded as a failure |
| 24 | A `status: false` response parses as `Rejected` and preserves the message | A rejection treated as `Unknown`, which blocks the fall-through to 3a |
| 25 | Malformed JSON in a slots response raises `PicktimeReadException`. It is not returned as an empty list, and no other exception escapes. | A failed read mistaken for a fully booked day, or an unparseable body crashing the run |
| 26 | The `scantoken` header is present on every request | Auth silently missing, so nothing books and the reason is unclear |
| 32 | An HTTP 401 or 403 from either call raises `PicktimeAuthenticationException`, and the request is not retried | A token rejection treated as a normal failure, or retried |
| 33 | Built from the real `AddPicktimeServices` registration, with a stub handler. With HTTP 503: the booking `POST` reaches the handler exactly once, and the availability `GET` reaches it 4 times (the first try plus 3 retries). With HTTP 401: the availability `GET` reaches it exactly once. The test sets the retry backoff to zero, so it runs quickly; it checks the number of calls, not the timing. | A retry policy added to the booking client, which can book the same slot twice, or one that retries a rejected token |

That the token is never written to a log is a code review point in section 6. Asserting the absence of a value across arbitrary log calls is brittle and proves little. One narrow test is kept: built from the real `AddPicktimeServices` registration, which sets the header, it runs a malformed read, a malformed booking body and a rejected token, and checks that no log record holds the token. Added on 2026-10-07, at the owner's request (T15).

### 7.7 Function triggers — `PicktimeAutomation.AzureFunctionsTests`

Five tests, deliberately. Both triggers are thin adapters. The only logic is the HTTP trigger's input check, the timer's late-run check, the exception middleware and the summary each trigger writes (section 5.3), so there is nothing else here worth asserting.

| # | Test | Defect it catches |
| --- | --- | --- |
| 27 | The exception middleware, given a function that throws, logs the exception and writes the summary event with the `Error` verdict. For an HTTP function it returns 500 with no internal detail. The test uses a fake `FunctionContext`. | A crashed run missing from the run record, a stack trace leaked to the caller, or a function left without error handling |
| 28 | The HTTP trigger passes a supplied date through unchanged, and returns the run summary as JSON | The manual trigger behaving differently from the scheduled one |
| 29 | A malformed or past `bookingDate` returns 400, and the booking service is not called. Today is accepted, including at 00:30 London in BST, when the UTC date is still the day before. | A typo in a manual run books the wrong day, or crashes, or a valid same-day run is rejected near midnight |
| 34 | A timer run with `IsPastDue` set, outside the late-run window, does not call the booking service, logs a warning, and writes the summary event with the `Missed` verdict. Inside the window, it calls the booking service with no date. | A late run books a day that was never scheduled, a missed night leaves no record, or a run delayed by start-up books nothing |
| 37 | A normal run and a skipped run, through each trigger, write exactly one summary event. It has every property that the section 5.4 query reads: `BookingDate`, `BookedCount`, `NoAvailabilityCount`, `FailedCount`, `UnconfirmedCount`, `FailedReadCount` and `Verdict`. | A second summary writer added by mistake, so the dashboard counts every run twice, or a missing property that leaves a dashboard column empty |

### 7.8 Configuration — `PicktimeAutomation.ServicesTests`

| # | Test | Defect it catches |
| --- | --- | --- |
| 35 | Each validation rule in section 2 stops start-up with a message naming the setting, including a missing `Picktime:ScanToken`, and a missing or unparseable `BookingSchedule`. A season that wraps the year end is accepted. | A bad setting that is only discovered at 00:05 |

---

## 8. Infrastructure and deployment

### 8.1 Azure resources

| Resource | Choice | Notes |
| --- | --- | --- |
| Region | UK South | Nearest region |
| Plan | Consumption (serverless) | Within the free grant |
| Storage account | Standard LRS | Required by the timer trigger |
| Application Insights | Workspace-based | Holds no data itself. It sends everything to the Log Analytics workspace below. |
| Log Analytics workspace | Pay-as-you-go (per GB), with a daily cap of 0.1 GB | Where logs are stored and charged. The first 5 GB a month per billing account is free, and Application Insights data is kept for 90 days at no charge (checked on 2026-09-30, and again on 2026-10-06). Expected usage: a few MB a month. See section 8.3. |
| Function App | .NET 10 isolated worker | |
| Operating system | **Windows** | Required for .NET 10 on the Consumption plan, and chosen for the time zone setting. See below. |
| Budget | £1 per month, on the resource group | Emails an alert when actual cost reaches £1. Budgets are free. See section 8.3. |
| Managed identity | User-assigned | Used by GitHub Actions to deploy (section 8.4). It has a federated credential that trusts this repository's `main` branch, and the Website Contributor role on this Function App only. Managed identities have no charge. |

Required application settings:

| Setting | Value |
| --- | --- |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `WEBSITE_TIME_ZONE` | `GMT Standard Time` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Set by Azure when Application Insights is linked to the Function App. The exporter in section 5.2 needs it. |

Plus every setting in section 2.

**Y1 quota.** A new subscription can start with no Consumption plan quota in a region. Creating the Function App then fails pre-flight validation with `SubscriptionIsOverQuotaForSku` and "Current Limit (Y1 VMs): 0". A free quota request fixes it: a Basic support request for "Function or Web App (Windows and Linux)", plan Y1, in the region. This subscription had the error in UK South on 2026-10-08. The request for a limit of 2 was closed automatically, and the next attempt succeeded.

**Why Windows.** There are two reasons, and either one is enough.

1. **.NET 10 is not available on the Linux Consumption plan.** Microsoft's documentation states that .NET 9 is the last .NET version supported there, and that the Linux Consumption plan retires on 30 September 2028. Apps on the Windows Consumption plan are not affected. Checked on 2026-09-30, and again on 2026-10-06: .NET 10 is generally available on Functions v4.
2. **The time zone setting.** `WEBSITE_TIME_ZONE` takes a Windows time zone id on a Windows plan (`GMT Standard Time`) and an IANA id on a Linux plan (`Europe/London`). The setting is long-established and well documented on Windows, and less reliably behaved on Linux. A wrong trigger time is the worst failure this project can have, so the better-trodden path wins.

**Flex Consumption**, Microsoft's recommended successor to the Linux Consumption plan, runs only on Linux. Reason 2 therefore applies to it too, so it is not used.

Microsoft labels the Consumption plan "legacy" and recommends Flex Consumption for new apps. On Windows the Consumption plan is still generally available, with no retirement date (checked on 2026-09-30). Check this again before each season.

The code is unaffected by this choice. See section 3.3. After deployment, check the trigger time:

* **While BST is in force**, until the last Sunday of October (spec section 6.2): in Logs (KQL), the first scheduled run's UTC timestamp must be 23:05 on the day before the run day. A run at 00:05 UTC means the setting is missing.
* **While GMT is in force:** London time equals UTC, so the logs cannot show the difference. Check instead that `WEBSITE_TIME_ZONE` is `GMT Standard Time` in the Function App's environment variables.

### 8.2 Local prerequisites

Checked on this machine:

| Tool | Status | Needed for |
| --- | --- | --- |
| .NET 10 SDK | Installed, 10.0.401 | Building and testing |
| Azure Functions Core Tools v4 | Installed, 4.14.0 | Running the Function locally |
| Azure CLI | Installed, 2.90.0 | Creating the Azure resources. The portal is an alternative. |
| Azurite (local storage emulator) | Bundled with Visual Studio 2026. Not installed as an npm package, and it cannot be: Node.js on this machine is v16.9.1, which is below the minimum for current Azurite. | Running the Function locally. The timer trigger needs storage, and `local.settings.json` points it at Azurite. |

**Azurite** must be running before the Function starts locally. Use the copy bundled with Visual Studio. From Visual Studio, nothing is needed, because Visual Studio starts it. From a terminal, start it in a second terminal before `func start`:

```
"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\Azure Storage Emulator\azurite.exe" --location "$env:LOCALAPPDATA\Azurite"
```

Do not install Azurite with `npm install -g azurite`. This machine's Node.js is v16.9.1, which current Azurite does not support. The bundled copy carries its own runtime, so it does not use the machine's Node.js. It is free, and nothing runs in Azure.

Checked on 2026-10-01: Azurite started this way, and `func start` then listed the timer trigger.

**The timer is disabled for local runs.** `local.settings.json` sets `AzureWebJobs.TargetBookingFunction.Disabled` to `true`. Without it, `func start` can make real calls to Picktime: the timer keeps a record of its runs in Azurite, and when it sees a missed scheduled run it fires at once. The late-run check (section 3) does not prevent this. A missed run that fires between 00:05 and 01:00 on a run day is inside the late-run window, and books. So the `Disabled` setting is the only protection.

This happened on 2026-10-02. A `func start` on a Friday morning fired the missed 00:05 run, which sent three real booking requests to Picktime. No booking was made only because defect 1 in spec section 4.1 made every request invalid. Once defect 1 is fixed, the same mistake would make real bookings.

With the setting, `func start` reports "Function TargetBookingFunction is disabled" and the timer never runs. The HTTP trigger is not affected, so deliberate test bookings through it still work. The setting must never be set in Azure, where the timer must run.

### 8.3 Cost

Effectively free, but not literally zero.

* **Functions free grant**, checked on 2026-09-30: 1 million executions and 400,000 GB-s of compute per month. It applies only to pay-as-you-go subscriptions, and it is shared by all function apps in the subscription. Microsoft Learn does not state these figures. It refers to the Azure Functions pricing page, so check them there again when the resources are created.
* **Expected usage:** about 13 runs a month. Each run takes about 20 seconds, so at up to 0.25 GB of memory that is under 100 GB-s a month. It is negligible against the grant, even with other function apps in the same subscription.
* Logs (Log Analytics workspace): the first 5 GB a month per billing account is free (checked on 2026-10-06). About 78 runs per season, each writing a few dozen log lines, is negligible against it.
* Daily cap: 0.1 GB a day on the workspace, which is free to set. Expected usage is hundreds of times smaller. It stops a logging bug from running up a cost before the £1 budget alert can report it. The trade-off: if the cap is ever reached, logs stop for the rest of that day, which can only happen during a runaway bug. Spec section 9 records this as the one accepted exception to "no log line from a run is ever discarded".
* Log retention: Application Insights tables keep data for 90 days at no charge (checked on 2026-10-06). Keep the default. Lowering it saves nothing, because the first 31 days are included in the ingestion price. Raising it adds a cost.
* Storage account: the only unavoidable charge. Azure Functions cannot run without a storage account, and the timer trigger keeps its schedule state there. Azure has no permanent free tier for storage. Estimated at well under £1 per month, from a tiny amount of stored data and the background transactions of the Functions host. This figure is an estimate, not checked against the storage pricing page.

**Approved on 2026-09-30:** the storage account cost, up to £1 per month. The £1 budget in section 8.1 sends an alert if the real cost is ever higher, so the estimate is checked by Azure rather than trusted.

### 8.4 CI/CD

A single GitHub Actions workflow in `.github/workflows/`. It runs on `windows-latest`, to match the Function App's operating system (section 8.1), and installs .NET `10.0.x` with `actions/setup-dotnet`.

1. Trigger on push to `main`, and on pull request.
2. Restore, build, and run all tests. NuGet Audit runs on restore, and its warnings for vulnerable packages (`NU1901` to `NU1904`) are errors in the workflow, as the owner's C# standard requires. So a package with a known vulnerability stops the build, and it never deploys.
3. On `main` only, and only when tests pass, publish and deploy to the Function App.
4. Sign in with `azure/login` using OpenID Connect, then deploy with `Azure/functions-action`. The workflow needs the `id-token: write` permission. This is Microsoft's recommended method (checked on 2026-09-30).
5. The managed identity's client id, tenant id and subscription id are GitHub repository variables. They identify the identity; they are not secrets.

**The sign-in trusts one subject.** The identity's federated credential trusts the classic subject for the `main` branch: `repo:OllieRumbol/PicktimeAutomation:ref:refs/heads/main`. GitHub sends this form because the repository's OIDC setting `use_immutable_subject` is `false`. Azure accepts a token only when its subject matches exactly. So:

* The deploy job must run on a push to `main`. A job on a pull request sends a different subject, so it cannot sign in. Step 3 already keeps it off pull requests.
* The deploy job must not use a GitHub environment. A job with `environment:` sends `repo:<owner>/<repo>:environment:<name>` in place of the branch, and the sign-in fails.

The portal now generates GitHub's immutable subject form (`repo:<owner>@<owner id>/<repo>@<repo id>:…`) and recommends it. Switching both the repository setting and the credential to that form is a possible later improvement. It is not done.

Tests gate the deployment. A red build does not reach Azure.

**Rollback.** Revert the bad commit on `main`. The workflow then tests and deploys the previous version. No other tooling is needed.
