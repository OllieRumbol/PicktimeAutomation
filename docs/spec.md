# Picktime Automation — Specification and Delivery Plan

Status: agreed scope, ready to implement
Last updated: 2026-09-28

This document is the record of design decisions for this project. It is kept as one specification rather than split into separate decision records, because the design is small and coherent enough to read in one sitting. Keep it updated as decisions change.

---

## 1. Purpose

Book an indoor archery target automatically, so that no manual booking is needed during the indoor season.

The club uses Picktime for indoor target booking. Bookings open exactly 7 days in advance, at local midnight, and are first come first served. The automation runs a few minutes after the window opens and takes the wanted slots before other archers do.

---

## 2. Background

* The indoor season runs from 1 October to 31 March.
* The hall has 4 targets. Each target is split into two halves, so an archer books one of 8 faces: 1a, 1b, 2a, 2b, 3a, 3b, 4a, 4b.
* Each booking is a 1-hour slot. Slots run hourly from 07:00 to 22:00.
* Booking is free of charge. Payment, if any, is handled at the club.
* Only one archer is booked for: the repository owner.

---

## 3. Goals and non-goals

### Goals

1. Book target 2b at 17:00, 18:00 and 19:00 on Tuesday, Thursday and Friday.
2. Fall back to target 3a for any hour where 2b is not free.
3. Run unattended on a free-tier Azure Function for a whole season.
4. Make the target list, hours and days configurable, so no code change is needed to adjust them.
5. Give a clear log record of what each run booked and what it failed to book.

### Non-goals

* Cancelling or changing existing bookings.
* Booking for any other archer.
* Handling payment.
* A user interface. The only human-facing surfaces are the logs and a manual test trigger.
* Booking outdoor sessions.

---

## 4. Current state of the code

The repository contains a working skeleton. It compiles and has the right shape. The Picktime API is proven to accept a booking, but this code has never made one, because of defect 1 below.

| Project | Purpose | State |
| --- | --- | --- |
| `PicktimeAutomation.AzureFunctions` | Timer-triggered entry point | Skeleton, starts and triggers |
| `PicktimeAutomation.Services` | API client and booking rules | Skeleton, has defects |
| `PicktimeAutomation.Models` | Request and response contracts | Booking response is well modelled |
| `PicktimeAutomation.ServicesTests` | Unit tests | Placeholder `Test1.cs` only |
| `PicktimeAutomation.AzureFunctionsTests` | Unit tests | Placeholder `Test1.cs` only |

### 4.1 Defects to fix

1. **Wrong value sent as the booking time.**
   `PicktimeAutomation.Services/PicktimeApiService.cs:28` sets `start_date_time` to `createBookingRequest.ResourceId`. That sends the target GUID where a timestamp belongs. `BookingRequest.DateTimeOfBooking` is never used, so every booking request is malformed.

2. **No availability check.**
   `PicktimeBookingService` posts a booking for all three hours without asking which slots are free. It cannot implement the fallback rule, because it does not know whether 2b was actually taken.

3. **Fallback target is unused.**
   `PicktimeAutomation.Services/PicktimeBookingService.cs:11` declares `Target3aBookedResourceId` but nothing reads it.

4. **All configuration is hardcoded.**
   Account id, location id, target ids, first name, last name, email and the authentication token are literals in source. The token is in `PicktimeAutomation.AzureFunctions/Program.cs:12`.

5. **Time zone is not handled.**
   The timer expression `0 5 0 * * TUE,THU,FRI` is interpreted in UTC, and `DateTime.Today` on the Functions host is UTC. Both are wrong for a London booking window during British Summer Time. See section 6.2.

6. **`.gitignore` excludes itself.**
   Line 60 of the root `.gitignore` matches `.gitignore`, so no ignore file is tracked. A fresh clone has no ignore rules, which puts `bin/`, `obj/` and `local.settings.json` at risk of being committed.

7. **Runtime is out of support.**
   All projects target .NET 6, which reached end of support in November 2024. Functions worker packages are correspondingly old (`Microsoft.Azure.Functions.Worker` 1.6.0).

8. **No CI/CD.**
   `.github/` exists but is empty.

9. **Application Insights sampling is enabled.**
   `PicktimeAutomation.AzureFunctions/host.json` sets `samplingSettings.isEnabled` to `true`. Sampling discards telemetry to control volume, which is the opposite of what is wanted at three runs a week — it can drop the one log line that explains a failed run. See section 10.2.

---

## 5. External API

Base address: `https://www.picktime.com/`

Picktime timestamps are integers in the form `yyyyMMddHHmm`. For example `202609291800` means 18:00 on 29 September 2026. This spec calls that a *Picktime timestamp*.

### 5.1 Read availability for one target and one day

```
GET /endpoint/1.0.0/ia/slots
```

| Parameter | Value | Notes |
| --- | --- | --- |
| `schedulerId` | target resource id | The target, for example 2b |
| `dateAndTime` | `yyyyMMdd0000` | Midnight at the start of the day wanted |
| `endDate` | `yyyyMMdd0000` | Midnight at the start of the next day |
| `locationId` | location id | Fixed per club |
| `accountId` | account id | Fixed per club |
| `duration` | `60` | Slot length in minutes |
| `slot` | `60` | Slot length in minutes |
| `eventType` | `resource` | Fixed |
| `offBooking` | `false` | Fixed |
| `isSpecificLink` | `false` | Fixed |
| `serviceClassId` | empty | Fixed |
| `timezone` | `Europe/London` | Fixed |
| `v3` | `true` | Fixed |
| `withFullDays` | `true` | Fixed |
| `_` | epoch milliseconds | Cache-buster. Include it to match the browser. |

Response:

```json
{
  "status": true,
  "message": "Success",
  "data": [202609291700, 202609291800, 202609291900],
  "metadata": {
    "availabilityIndicators": false,
    "calEndDate": 202610052359,
    "calStartDate": 202609282215,
    "availabledays": ["20260928", "20260929", "20260930"],
    "selectedDate": 202609290000
  },
  "version": "1.0.0"
}
```

* `data` holds the free start times for that target on that day, as Picktime timestamps. An hour that is absent is already taken.
* `metadata.availabledays` lists the dates the site offers in its calendar. It is informational only. The automation does not read it, because an empty `data` array already tells us there is nothing to book on that date.
* In the captured sample, `data` held all 16 hourly slots from 07:00 to 22:00. The example above is shortened to the three hours this automation cares about.

### 5.2 Create a booking

```
POST /endpoint/1.0.0/ia/save/event
```

An optional `?_=<epoch milliseconds>` cache-buster may be appended, as the site does.

Body, as captured from a **verified successful booking**:

```json
{
  "account_id": "<account id>",
  "send_sms": false,
  "location": "<location id>",
  "start_date_time": 202609291700,
  "duration": 60,
  "cost": 0,
  "type": "resource",
  "resources": ["<target resource id>"],
  "fname": "<first name>",
  "lname": "<last name>",
  "email": "<email>",
  "mobile_number": "",
  "mobile_number_ext": null,
  "alt_mobile_number": "",
  "alt_number_Ext": null,
  "address": null,
  "city": null,
  "state": null,
  "zip": null,
  "notes": "",
  "birth_month_date": "month-selectDate",
  "birth_year": "",
  "booking_addnl_fields": "{\"ADDITIONAL ARCHER\":\"\"}",
  "payment_required": false,
  "timezone": "Europe/London"
}
```

Notes on the fields:

* The current code sends only the first twelve. The remaining fields come from the club's booking form. Send the payload exactly as captured, since a booking is known to succeed with it and the cost of sending the extra fields is nothing.
* `alt_number_Ext` has that unusual capitalisation in the real payload. Keep it. A JSON property name attribute is needed, because it will not match a C# property name by convention.
* `birth_month_date` holds the literal string `month-selectDate`, which is the unset state of a form control. It is not a date. Send it verbatim.
* `booking_addnl_fields` is a JSON **string** containing nested JSON, not an object. `ADDITIONAL ARCHER` is a custom field the club has added to its form.

Successful response:

```json
{
  "status": true,
  "message": "Appointment fixed",
  "data": {
    "id": "<booking id>",
    "account_id": "<account id>",
    "status": true,
    "type": "resource",
    "timezone": "Europe/London",
    "duration": 60
  }
}
```

* `status: true` and `message: "Appointment fixed"` indicate success. Treat `status` as the authority and log the message.
* `data.id` is the booking id. Log it, so a booking can be traced back to a run.
* `PicktimeAutomation.Models/BookingSuccessfulResponse.cs` already models this response correctly.

A failure has `status: false` with an explanatory `message`.

### 5.3 Authentication

A `scantoken` header carries a JSON Web Token. Verified properties:

* The payload contains `iss`, `accountId`, `userId` and `iat`. There is no `exp` claim.
* `userId` is `null`, so it is an anonymous token issued to any visitor of the public booking page.
* **Reuse is proven.** A token issued on 25 March 2026 was used on 28 September 2026 to create a real booking, which returned `200 OK` and `status: true`. The token is therefore long-lived across at least six months, and is not tied to a browser session.

Consequences for the design:

1. Store the token as configuration, not source, so it can be replaced without a code change.
2. If the API starts rejecting the token, log an error that clearly names authentication as the cause. Silence is the failure mode to avoid.

### 5.4 Headers

A booking has been created successfully from Postman, outside any browser session, using a six-month-old token. So no browser, no live session and no fresh token is required. That removes the main risk.

One detail is still open. The captured availability request sends `browserid`, `x-requested-with: XMLHttpRequest`, a `referer`, and cookies including `pt_csrf` and `pt_slot_hold_check`, whose value equals the `browserid`. The successful Postman booking was sent with 25 headers, so it is not yet known which of those the API actually requires.

This is a verification task rather than a blocker. See section 14.

---

## 6. Functional specification

### 6.1 Schedule

| Item | Value |
| --- | --- |
| Run days | Tuesday, Thursday, Friday |
| Run time | 00:05, Europe/London |
| Booking date | Run date plus 7 days |
| Hours wanted | 17:00, 18:00, 19:00 |
| Preferred target | 2b |
| Fallback target | 3a |
| Fallback granularity | Per hour, independently |

Because the offset is exactly 7 days, the weekday is preserved. A Tuesday run books the following Tuesday. A full week is 9 bookings across 3 runs.

### 6.2 Time zone rules

This is the most likely source of a silent, wrong-day booking, so it is specified explicitly.

The booking window opens at **London** midnight. London is not UTC for part of the season:

* British Summer Time ends on the last Sunday of October. In the 2026/27 season that is 25 October 2026.
* British Summer Time starts again on the last Sunday of March. In 2027 that is 28 March 2027.

So roughly the first three and a half weeks of each season run in BST, where London midnight is 23:00 UTC on the previous day.

Two independent things must both be correct:

1. **The trigger time.** Set the Function App setting `WEBSITE_TIME_ZONE` to `GMT Standard Time`, so the NCRONTAB expression is evaluated in London time. That Windows id covers British Summer Time as well, despite its name. A Windows plan is used for this reason — see section 13.1.
2. **The date arithmetic.** Compute the booking date from the current London time, not from `DateTime.Today`, which is UTC on the host. Use `TimeZoneInfo.FindSystemTimeZoneById("Europe/London")`, which .NET resolves on both Linux and Windows.

Fixing only one of the two gives a booking that is a day out. Both must be in place, and both must be covered by tests.

Every run must log the current UTC time, the current London time and the computed booking date, so a mistake is visible in the first log line.

### 6.3 Season rule

Do nothing unless the **booking date** falls between 1 October and 31 March inclusive.

The gate is on the booking date rather than the run date. That way the run on Thursday 24 September correctly books Thursday 1 October, the first day of the season.

Worked examples for the 2026/27 season:

| Run date | Booking date | Action |
| --- | --- | --- |
| Thu 24 Sep 2026 | Thu 1 Oct 2026 | Book — first day of the season |
| Tue 29 Sep 2026 | Tue 6 Oct 2026 | Book |
| Tue 23 Mar 2027 | Tue 30 Mar 2027 | Book — last booking of the season |
| Thu 25 Mar 2027 | Thu 1 Apr 2027 | Skip — out of season |

A skipped run logs that it skipped, and why.

### 6.4 Booking algorithm

```
booking date = London today + 7 days

if booking date is outside the season:
    log skipped, exit

for each target in preference order (2b, then 3a):
    free[target] = availability for target on the booking date

for each hour in (17, 18, 19):
    for each target in preference order (2b, then 3a):
        if hour is free for target:
            book it
            if the booking succeeded:
                record success, move to the next hour
            else:
                record the failure, try the next target
    if no target was booked for this hour:
        record "no target available"

log a summary of all three hours
```

Notes on the design:

* Availability is read once per target per run, not once per hour. That is 2 reads and up to 3 writes, so about 5 HTTP calls per run.
* Availability is read before any booking is made. A slot that Picktime reports as free can still be taken by another archer in the seconds between the read and the write, so a booking failure is expected behaviour and must fall through to the next target rather than abort the run.
* The target list is configuration. Adding the other six target ids later extends the fallback chain with no code change.

**If an availability read fails** after its retries, treat that target as having no free hours, log a warning naming the target, and carry on. One unreachable target must not stop the other from being used. If every availability read fails, every hour records `NoAvailability` and the run summary says so.

**Ordering and speed.** The measured booking call took 3.64 seconds, and a Consumption-plan cold start adds several more. A fully sequential run is therefore roughly 20 seconds from trigger to last booking.

* The two availability reads are independent, so run them concurrently.
* The three booking POSTs run **sequentially**, in hour order. They are not parallelised. Sending them together would save a few seconds, but it also means three in-flight non-idempotent writes with no way to reason about what happened if the run faults midway. Twenty seconds after midnight is still far ahead of any human competing for the same slot, so the simplicity is worth more than the seconds.

### 6.5 Outcome per hour

Each hour ends in exactly one of these states, and each is logged:

| Outcome | Meaning |
| --- | --- |
| `Booked` | A target was booked. The target name and booking id are logged. |
| `NoAvailability` | No target in the chain had that hour free. |
| `Failed` | A target was free but every booking attempt was rejected. |

A run is a success only when all three hours reach `Booked`. Partial success is reported as partial, not as failure.

### 6.6 Double booking

No separate state store is needed. A slot already booked, whether by this automation or by hand, is absent from the availability response. The availability read is therefore the guard against double booking.

### 6.7 Manual trigger

An HTTP-triggered function exists alongside the timer, for testing and for catching up a missed run.

| Item | Value |
| --- | --- |
| Method and route | `POST /api/book` |
| Authorisation | Function key, so the URL alone is not enough |
| Body or query | Optional `bookingDate` as `yyyy-MM-dd` |
| Default | When no date is given, use the same rule as the timer: London today plus 7 days |
| Season gate | Applies, exactly as the timer does |
| Response | The run summary as JSON, so the outcome is visible without opening the logs |

It calls the same `IPicktimeBookingService.BookArcheryIndoorTargetAsync`, so there is one code path and no risk of the manual and scheduled routes behaving differently.

---

## 7. Configuration

No secret or personal detail stays in source. Local development uses `local.settings.json`. Azure uses Function App application settings.

| Setting | Example | Notes |
| --- | --- | --- |
| `Picktime:BaseUrl` | `https://www.picktime.com/` | |
| `Picktime:ScanToken` | *(secret)* | The `scantoken` header value |
| `Picktime:AccountId` | `4fcc15b7-…` | |
| `Picktime:LocationId` | `dd0a2b7e-…` | |
| `Picktime:BrowserId` | *(to confirm)* | Only if section 5.4 shows it is required |
| `Picktime:Referer` | `https://www.picktime.com/thwac` | Only if required |
| `Archer:FirstName` | `Oliver` | |
| `Archer:LastName` | `Bourne` | |
| `Archer:Email` | *(personal)* | |
| `Booking:DaysAhead` | `7` | |
| `Booking:Hours` | `[17, 18, 19]` | |
| `Booking:Targets` | `[{ "Name": "2b", "ResourceId": "…" }, { "Name": "3a", "ResourceId": "…" }]` | In preference order |
| `Booking:SeasonStart` | `10-01` | Month and day |
| `Booking:SeasonEnd` | `03-31` | Month and day |
| `Booking:TimeZone` | `Europe/London` | |

Bind these with the options pattern and validate them at start-up, so a missing token fails immediately and loudly rather than at 00:05.

There is deliberately no `RunDays` setting. The NCRONTAB expression on the timer is the single source of truth for when the automation runs. A configuration value that merely documents the cron would be a second place to change and a second place to get wrong.

---

## 8. Architecture

Keep the existing five-project layout. It is a sensible separation and there is no reason to churn it.

```
PicktimeAutomation.AzureFunctions   Timer trigger, HTTP test trigger, DI wiring, logging
PicktimeAutomation.Services         Picktime API client, booking rules
PicktimeAutomation.Models           Requests, responses, configuration, results
PicktimeAutomation.ServicesTests    Unit tests for the two services
PicktimeAutomation.AzureFunctionsTests  Unit tests for the functions
```

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

`IPicktimeBookingService` keeps one entry point. The booking date is optional:

```csharp
Task<BookingSummary> BookArcheryIndoorTargetAsync(
    DateOnly? bookingDate = null,
    CancellationToken ct = default);
```

When no date is supplied, the service computes it from the injected `TimeProvider` and the configured `DaysAhead`. The timer trigger supplies nothing; the HTTP trigger supplies a date only when one was requested.

That placement is deliberate. If the trigger computed the date, the date rule — the thing most likely to be wrong, per section 6.2 — would live in an Azure Functions entry point, which is awkward to unit test. Keeping it inside the service means both triggers are thin adapters with no logic of their own, and the rule is covered by ordinary unit tests.

### 8.1 Model changes

**`BookingRequest` stays minimal.** It holds only what varies per booking:

```csharp
public sealed record BookingRequest(long DateTimeOfBooking, string ResourceId);
```

The other 23 payload fields from section 5.2 do not belong on this model. They are either fixed constants or come from configuration, so `PicktimeApiService` composes the wire payload from `BookingRequest` plus its injected options. Putting form fields such as `birth_month_date` on a domain model would spread Picktime's form shape across the whole solution.

**`BookingSummary` and `BookingAttempt` need to grow.** They currently carry only `Success` and `ErrorMessage`, which cannot express the three outcomes in section 6.5:

```csharp
public enum BookingOutcome { Booked, NoAvailability, Failed }

public sealed class BookingAttempt
{
    public int Hour { get; init; }
    public BookingOutcome Outcome { get; init; }
    public string? TargetName { get; init; }   // set when Booked
    public string? BookingId { get; init; }    // set when Booked
    public string? ErrorMessage { get; init; } // set when Failed
}
```

`BookingSummary` keeps its list of attempts, and replaces the single `Success` boolean with counts per outcome plus an overall verdict, so a partial result reads as partial rather than as failure.

**`BookingResult`** is the new return type of `CreateBookingAsync`: whether it succeeded, the booking id from `data.id`, the API's `message`, and whether the failure was transient or definitive. The booking service needs that last flag to decide between retrying and falling through to the next target.

### 8.2 Designed to extend

The likely extensions, and what each would cost. Anything in the first group is configuration only.

| Extension | Cost |
| --- | --- |
| Use more of the 8 targets | Add their ids to `Booking:Targets` in preference order. No code change. |
| Different hours | Change `Booking:Hours`. No code change. |
| Different days | Change the NCRONTAB expression. No code change. |
| A different booking lead time | Change `Booking:DaysAhead`. No code change. |
| An alert when a run books nothing | An Azure Monitor alert on the section 10.4 query. No code change. |
| Email or push notification from the run itself | A notification service injected into the booking service. Small, and section 10.5 anticipates it. |
| Booking for a second archer | The archer details are single-valued in configuration. Would need a collection and a loop. Moderate. |
| Cancelling a booking | Needs its Picktime endpoint captured first, the same way the two current endpoints were. |

The two design choices that keep this open are the configurable target chain and keeping the booking rules free of any HTTP or Azure Functions dependency. The rules are an ordinary class that takes a date and returns a summary, so it can be driven from a timer, an HTTP request, a test, or something not yet thought of.

---

## 9. Error handling and retries

### 9.1 The retry rule that matters

**The two endpoints must not share a retry policy.**

Reading availability is a `GET`. It is safe to repeat, so retry it freely.

Creating a booking is a `POST`, and it is **not idempotent**. If the request times out, the booking may already have been created on Picktime's side. A blind retry then books the same slot twice, and there is no cancellation path in this automation to undo it. A generic "retry on timeout" policy applied to both endpoints would silently cause exactly that.

So:

| Endpoint | Retry policy |
| --- | --- |
| `GET /ia/slots` | Retry up to 3 times with a short backoff, on network error, timeout or HTTP 5xx |
| `POST /ia/save/event` | **No automatic retry.** Treat an ambiguous outcome as ambiguous. |

When a booking POST fails ambiguously — a timeout, a dropped connection, or a 5xx with no body — do not resend it. Instead, re-read availability for that target and hour:

* The hour is **gone** from availability, so the booking almost certainly succeeded. Record `Booked` with a warning that the booking id is unknown.
* The hour is **still free**, so the booking did not take. It is now safe to attempt it once more.

That turns an unsafe retry into a safe one, using the availability endpoint we already have.

### 9.2 Everything else

| Situation | Behaviour |
| --- | --- |
| HTTP 401 or 403, or a token rejection message | No retry. Log an error naming authentication as the cause. |
| `status: false` — slot taken | No retry. Fall through to the next target in the chain. |
| Malformed or empty response body | No retry. Record `Failed` for that hour, with the raw body logged at debug level. |
| An availability read fails after retries | Treat the target as having no free hours. Log a warning. Carry on. |
| An hour throws unexpectedly | Catch, record `Failed`, continue to the next hour. |

A failure on one hour must never abandon the other two hours. Each hour is independent.

Retries on the `GET` use the standard `Microsoft.Extensions.Http.Resilience` handler. Because the policy differs per endpoint, either register two named clients, or register the handler only for the availability path. Whichever is chosen, it must be impossible to accidentally pick up an automatic retry on the booking POST.

---

## 10. Knowing whether it worked

There are two channels, and they answer different questions.

### 10.1 Picktime's confirmation email — the quick check

Picktime sends a confirmation email for every booking it accepts. This is free, needs no work, and already exists. It is the primary day-to-day check.

A healthy run produces **three emails**, one per hour booked.

The booking response even reports this. `CreateBookingData.booking_email_confirmation` says whether Picktime intended to send one, so the automation logs that flag per booking. If emails stop arriving but the logs say `booking_email_confirmation: true`, the problem is at Picktime's end, not ours.

The limitation is that this channel only reports success. Picktime sends nothing when the automation books nothing, so a failed run is signalled by the **absence** of email — and an absence is easy not to notice. That is what the logs are for.

### 10.2 Azure logs — free, and quick to reach

Application Insights on the free tier: 5 GB per month, against a workload of roughly 78 runs per season producing a few dozen log lines each. Cost is not a consideration at this volume.

**Sampling must be turned off.** The existing `host.json` enables it (defect 9 in section 4.1). Sampling exists to discard data at high volume, and at three runs a week it can only do harm: the line it drops may be the one explaining why nothing booked. Set `samplingSettings.isEnabled` to `false`.

Three ways to reach the logs, in increasing order of effort:

| Route | Where | Use it for |
| --- | --- | --- |
| **Invocations** | Function App → Functions → the function → Invocations | The normal check. A row per run, click through for that run's log lines. |
| **Log stream** | Function App → Log stream | Watching a manual run live. Nothing is retained. |
| **Logs (KQL)** | Application Insights → Logs | History, and the pinned dashboard query below. |

### 10.3 What each run logs

1. **Start** — UTC time, London time, the computed booking date, and whether the season gate passed.
2. **Availability** — the free hours found for each target.
3. **Each attempt** — hour, target name, outcome, the booking id on success, and `booking_email_confirmation`.
4. **Summary** — one event, described below.

Use structured logging with named placeholders throughout, so values land in `customDimensions` and are queryable. Keep `BookingLoggingExtensions` as the single place summaries are written.

**The summary is one event with named properties**, not a sentence of interpolated text. That is what makes the dashboard query below possible:

```csharp
logger.LogInformation(
    "Booking run finished. Date={BookingDate} Booked={BookedCount} " +
    "NoAvailability={NoAvailabilityCount} Failed={FailedCount} Verdict={Verdict}",
    bookingDate, booked, noAvailability, failed, verdict);
```

Never log the `scantoken`.

### 10.4 A season at a glance

Paste this into Application Insights → Logs, then **Pin to dashboard**. After that, every run of the season is one click away:

```kusto
traces
| where timestamp > ago(180d)
| where message startswith "Booking run finished"
| extend BookingDate = tostring(customDimensions.BookingDate),
         Booked      = toint(customDimensions.BookedCount),
         Unavailable = toint(customDimensions.NoAvailabilityCount),
         Failed      = toint(customDimensions.FailedCount),
         Verdict     = tostring(customDimensions.Verdict)
| project timestamp, BookingDate, Booked, Unavailable, Failed, Verdict
| order by timestamp desc
```

A season of runs, three columns wide, with any row that did not book three hours obvious at a glance. Add `| where Booked < 3` to see only the runs worth investigating.

### 10.5 Residual risk

With email plus logs, the remaining gap is narrow but real: a run that books nothing sends no email and writes a log nobody reads.

Three things reduce it, and it is accepted rather than solved:

1. Three missing emails on a shooting night is a noticeable signal in itself.
2. An authentication failure logs at error level and names the cause, so the reason is immediate once anyone looks.
3. The HTTP trigger checks the automation on demand, returning the summary as JSON without waiting for a scheduled run.

Adding a push or email alert later is a small change. An Azure Monitor alert on the query in section 10.4 would do it with no code at all, and is the natural first extension.

---

## 11. Security

| Item | Decision |
| --- | --- |
| `scantoken` in git history | Low severity, and accepted. It is an anonymous token issued freely to any visitor of the public booking page, so it grants nothing the public does not already have. It has no `exp` claim and no revocation route, so rotation would achieve nothing. |
| `scantoken` going forward | Move to configuration, and to a Function App application setting in Azure. Not in source. |
| Email address | Move to configuration. The repository is public, so it should not be a literal in source. |
| `.gitignore` | Remove the self-ignoring line, then commit both ignore files. This matters because the token will live in `local.settings.json`. |
| `local.settings.json` | Stays ignored. Never committed. |
| GitHub Actions secrets | The Azure publish profile is held as a repository secret. Secrets are not exposed to pull requests from forks. |
| HTTP test trigger | Protected by a function key, so the URL alone is not enough to fire a booking. |
| Logging the token | Check at code review that the `scantoken` is never logged. Not covered by a test — see section 12.6 for why. |

---

## 12. Testing

Keep MSTest, which both test projects already use. Replace the `Test1.cs` placeholders.

### 12.1 Principles

**There is no coverage target, and coverage is not reported.** A percentage rewards testing trivia and says nothing about whether the logic is right.

Every test below exists because it catches a specific defect, and each is listed with the defect it catches. If a test cannot be justified that way, it does not get written. Consequences worth stating plainly:

* Thin adapters are not tested for being thin. The two Azure Function triggers hold no logic, so they get two tests between them, not a suite.
* No test asserts a property getter, a constructor, or that a mock was called in a particular order.
* If `PicktimeAutomation.AzureFunctionsTests` ends up with nothing worth asserting, delete the project rather than pad it.

### 12.2 Design for testability

Testability is a design constraint here, not something retrofitted. Six rules, each of which removes a reason a test would otherwise be hard to write:

1. **Use `TimeProvider`, never `DateTime.Now` or `DateTime.Today`.** .NET 10 ships `TimeProvider` and `FakeTimeProvider` (in `Microsoft.Extensions.TimeProvider.Testing`), so the BST and GMT cases in section 12.5 are ordinary unit tests with no custom clock abstraction to invent.
2. **All HTTP sits behind `IPicktimeApiService`.** `PicktimeBookingService` never touches `HttpClient`, so the booking rules are testable with a hand-written fake and no message-handler plumbing.
3. **The date rule lives in the service, not the trigger.** See section 8. Logic in an Azure Functions entry point is logic that is painful to reach from a test.
4. **Decisions are pure functions where they can be.** The season gate takes a date and returns a verdict. It reads no clock, no configuration and no ambient state, so its test is a table of dates.
5. **Configuration arrives as injected options objects,** not as `IConfiguration` lookups scattered through the code. A test constructs the options it needs.
6. **Methods return inspectable results.** `BookArcheryIndoorTargetAsync` returns a `BookingSummary` describing every hour, so a test asserts the returned outcome rather than reading log output.

Rule 6 is what makes most of section 12.3 possible at all. The current `BookingSummary` cannot express a per-hour outcome, which is why section 8.1 grows it.

### 12.3 Booking rules — `PicktimeAutomation.ServicesTests`

The highest-value group. These cover the logic that decides what gets booked, driven by a fake `IPicktimeApiService`.

1. All three hours free on 2b, so all three book on 2b.
2. 18:00 taken on 2b but free on 3a, so 17:00 and 19:00 book on 2b and 18:00 books on 3a.
3. An hour taken on both targets records `NoAvailability` and does not stop the other hours.
4. Availability says free, but the booking is rejected, so the next target is tried.
5. Both targets reject the booking, which records `Failed`.
6. A transient throw on one hour does not prevent the other two from booking.
7. A booking date outside the season performs no API calls at all.
8. Season boundaries: 1 October and 31 March are inside; 30 September and 1 April are outside.
9. The availability read for 2b fails, so 2b is treated as full and all three hours book on 3a.
10. Both availability reads fail, so every hour records `NoAvailability` and no booking is attempted.
11. A partial result — two hours booked, one not — is reported as partial, not as failure.

Each test names the defect it catches.

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
| 10 | Both availability reads fail, so every hour records `NoAvailability` and nothing is booked | A blind booking attempt after a failed read |
| 11 | Two hours booked and one not is reported as partial, not as failure | A mostly-successful run reads as a total failure in the logs |

### 12.4 Ambiguous booking outcomes — section 9.1

The tests that stop a duplicate booking. The most valuable three in the suite, because the defect they prevent cannot be undone once it happens. Drive them with a fake API service that reports an ambiguous failure, then a controlled availability response.

| # | Test | Defect it catches |
| --- | --- | --- |
| 12 | A booking times out and the hour has gone from availability, so the outcome is `Booked` and **no second booking is attempted** | A duplicate booking for the same slot |
| 13 | A booking times out and the hour is still free, so exactly one further attempt is made | Giving up on a slot that was never actually booked |
| 14 | A booking times out twice, so the outcome is `Failed` and no third attempt is made | An unbounded retry loop against a failing API |

### 12.5 The date rule and time zones

Section 6.2 calls this the most likely source of a silent, wrong-day booking, so it gets its own group. All four use `FakeTimeProvider` with an explicit time zone, so they are deterministic and do not depend on the machine running them.

| # | Test | Defect it catches |
| --- | --- | --- |
| 15 | 00:05 London on Tuesday 6 October 2026 books Tuesday 13 October 2026, even though the host clock reads 23:05 UTC on 5 October | The BST off-by-one-day booking |
| 16 | A run during GMT computes the booking date correctly | A fix for BST that breaks the rest of the season |
| 17 | A run on a clock-change weekend computes the booking date correctly | The edge case nobody tests by hand |
| 18 | With no date supplied, the service computes London today plus the configured `DaysAhead` | The default path diverging from the explicit one |

### 12.6 API client — `PicktimeAutomation.ServicesTests`

Use a stub `HttpMessageHandler`. These are worth writing despite looking like wire-format trivia: every one of them is a failure that is invisible until a booking silently does not happen.

| # | Test | Defect it catches |
| --- | --- | --- |
| 19 | `GetAvailableSlotsAsync` builds the URL with every query parameter from section 5.1, and the right `dateAndTime` and `endDate` | One wrong or missing parameter returns the wrong day's availability, or none |
| 20 | A slots response parses into the free hours | Availability misread, so everything downstream is wrong |
| 21 | An empty `data` array yields no free hours and is not an error | A fully booked day treated as a fault |
| 22 | `CreateBookingAsync` posts every field in the section 5.2 payload with the exact names — including `alt_number_Ext` and the nested-JSON string in `booking_addnl_fields` — and `start_date_time` taken from `DateTimeOfBooking` | **Regression test for defect 1**, plus the two field-name traps |
| 23 | A successful response parses as success and captures the booking id from `data.id` | A real booking recorded as a failure |
| 24 | A `status: false` response parses as a definitive failure and preserves the message | A rejection treated as transient, triggering a pointless retry |
| 25 | Malformed JSON is reported as a failure, not thrown | An unparseable body crashing the run |
| 26 | The `scantoken` header is present on every request | Auth silently missing, so nothing books and the reason is unclear |

Not tested: that the token is never written to a log. Asserting the absence of a value across arbitrary log calls is brittle and proves little. It is a code review point in section 11 instead.

### 12.7 Function triggers — `PicktimeAutomation.AzureFunctionsTests`

Two tests, deliberately. Both triggers are thin adapters with no logic, so there is nothing else here worth asserting.

| # | Test | Defect it catches |
| --- | --- | --- |
| 27 | An exception from the booking service is caught and logged, so the Function does not crash the host | A thrown exception taking down the host and losing the log record of why |
| 28 | The HTTP trigger passes a supplied date through unchanged, and returns the run summary as JSON | The manual trigger behaving differently from the scheduled one |

---

## 13. Infrastructure and deployment

### 13.1 Azure resources

| Resource | Choice | Notes |
| --- | --- | --- |
| Region | UK South | Nearest region |
| Plan | Consumption (serverless) | Within the free grant |
| Storage account | Standard LRS | Required by the timer trigger |
| Application Insights | Free tier | 5 GB per month, far above what this needs |
| Function App | .NET 10 isolated worker | |
| Operating system | **Windows** | Chosen for the time zone setting. See below. |

Required application settings:

| Setting | Value |
| --- | --- |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `WEBSITE_TIME_ZONE` | `GMT Standard Time` |

Plus every setting in section 7.

**Why Windows.** The operating system matters here for one reason only: `WEBSITE_TIME_ZONE` takes a Windows time zone id on a Windows plan (`GMT Standard Time`) and an IANA id on a Linux plan (`Europe/London`). The setting is long-established and well documented on Windows, and less reliably behaved on Linux. A wrong trigger time is the worst failure this project can have, so the better-trodden path wins.

Note that `GMT Standard Time` is the Windows id for **UK time including British Summer Time**, despite the name. It is not fixed to GMT.

The code is unaffected by this choice. It uses the IANA id `Europe/London` via `TimeZoneInfo`, which .NET resolves on both platforms. Step 30 of the delivery plan still verifies the next-run time in the logs.

### 13.2 Local prerequisites

Checked on this machine:

| Tool | Status | Needed for |
| --- | --- | --- |
| .NET 10 SDK | Installed, 10.0.401 | Phase 1 onwards |
| Azure Functions Core Tools v4 | **Not installed** | Running the Function locally, phase 3 |
| Azure CLI | **Not installed** | Creating the Azure resources, phase 6. The portal is an alternative. |

Core Tools must be installed before phase 3. Neither missing tool blocks phases 1 or 2.

### 13.3 Cost

Effectively free, but not literally zero.

* Executions: about 78 runs per season. The free grant is 1,000,000 per month.
* Application Insights: negligible against the 5 GB monthly free allowance.
* Storage account: a few pence per month. This is the only unavoidable charge, and it exists because a timer trigger needs storage for its schedule state.

### 13.4 CI/CD

A single GitHub Actions workflow in `.github/workflows/`:

1. Trigger on push to `main`, and on pull request.
2. Restore, build, and run all tests.
3. On `main` only, and only when tests pass, publish and deploy to the Function App.
4. Authenticate with an Azure publish profile held in a repository secret.

Tests gate the deployment. A red build does not reach Azure.

---

## 14. Remaining unknowns

Both endpoints are now captured and a booking has been proven end to end, so nothing blocks implementation. Two small things are still to be pinned down, both cheaply, during phase 3.

### 14.1 The minimum header set

The successful Postman booking carried 25 headers. It is not known which are required. Find out by removing headers from that same Postman request and re-sending against a free slot, in this order:

1. Remove all cookies. Re-send.
2. Remove `browserid`. Re-send.
3. Remove `x-requested-with` and `referer`. Re-send.

Stop at the first step that fails, and keep whatever the last successful attempt sent. The goal is to send as little as possible, because every extra header is one more thing that can change under us. Record the answer in section 7 as configuration if `browserid` turns out to be needed.

### 14.2 What a rejection looks like

No rejected booking has been captured. Get one by posting a booking for a slot that is already taken — the 17:00 slot on target 2b for 29 September 2026 is now taken, so it can serve as the test case. Record the exact `status`, `message` and HTTP status code, then make the error handling in section 9 match.

---

## 15. Assumptions

Recorded so they can be corrected rather than discovered later.

1. There is no club limit on how many slots one archer may hold. The automation will attempt 9 bookings per week.
2. Indoor booking is free, so `cost` stays `0` and `payment_required` stays `false`.
3. Bookings are for one archer only. Confirmed: no additional archers, ever. `booking_addnl_fields` is therefore a fixed constant, `{"ADDITIONAL ARCHER":""}`, sent exactly as the verified booking sent it. It needs no configuration setting, because it never varies.
4. Cancellation is out of scope. Unwanted bookings are cancelled by hand.
5. The season is a fixed 1 October to 31 March, the same every year.
6. Slots are always exactly one hour, and 17:00 to 20:00 is always within the hall's opening hours.
7. The `scantoken` remains valid for the whole season. Reuse is proven over six months, so this is now evidenced rather than assumed. If it ever stops working, the fix is to capture a new one and update one application setting.
8. An Azure subscription is available, and the resource group and Function App name will be settled during phase 6.
9. The 7-day window is a release rule, not an API restriction. The verified booking was made 1 day ahead, so the endpoint accepts any date whose slots have been released. The automation still uses 7 days, because that is when the slots appear.

One observation on timing: to book Thursday 1 October 2026, the run had to happen on Thursday 24 September, which has passed. The first booking this automation can take is Tuesday 6 October 2026, from the run on Tuesday 29 September.

---

## 16. Delivery plan

### Phase 1 — Foundations

1. Upgrade all five projects from .NET 6 to .NET 10, and update the Functions worker packages to their current major version.
2. Confirm the Function App still starts locally after the upgrade.
3. Remove the self-ignoring line from `.gitignore`, then commit both ignore files.
4. Introduce the configuration classes from section 7, bound with the options pattern and validated at start-up.
5. Move the token, account id, location id, target ids, name and email out of source and into configuration.
6. Delete the two `Test1.cs` placeholders.

### Phase 2 — Core booking logic

7. Add `GetAvailableSlotsAsync` to `IPicktimeApiService`, with the slots response model.
8. Fix defect 1, so `start_date_time` comes from `DateTimeOfBooking`.
9. Change `CreateBookingAsync` to return a parsed `BookingResult`, and move response parsing out of the booking service.
10. Add the London time zone handling from section 6.2, using `TimeProvider` so it is testable.
11. Add the season gate from section 6.3.
12. Update the models per section 8.1: the outcome enum, the grown `BookingAttempt` and `BookingSummary`, and the new `BookingResult`.
13. Rewrite `PicktimeBookingService` to the algorithm in section 6.4, with per-hour outcomes. This depends on step 12.
14. Add the HTTP trigger from section 6.7, so later phases have a way to fire a run on demand.

### Phase 3 — Prove it against the real API

15. Narrow the header set per section 14.1, and send only what is required.
16. Capture a rejection per section 14.2, and make the error handling match it.
17. Make one real booking from a local run, fired through the HTTP trigger, and confirm it appears on the Picktime site.

### Phase 4 — Resilience and logging

18. Add the resilience handler to the availability `GET` only, per section 9.1.
19. Implement the ambiguous-outcome handling for the booking `POST`: re-read availability rather than resending.
20. Turn off Application Insights sampling in `host.json`, per defect 9.
21. Extend `BookingLoggingExtensions` for the per-hour outcomes, and emit the single structured summary event from section 10.3.
22. Log `booking_email_confirmation` per booking, so the logs and the Picktime emails can be reconciled.
23. Add the explicit authentication-failure log path.

### Phase 5 — Tests

24. Write the booking rules tests, items 1 to 11.
25. Write the ambiguous-outcome tests, items 12 to 14. These are the ones that prevent a duplicate booking.
26. Write the date rule and time zone tests, items 15 to 18.
27. Write the API client tests, items 19 to 26.
28. Write the two trigger tests, items 27 and 28.

### Phase 6 — Infrastructure and deployment

29. Create the Azure resources from section 13.1, on a Windows Consumption plan.
30. Set every application setting, including `WEBSITE_TIME_ZONE`.
31. Add the GitHub Actions workflow from section 13.4.
32. Deploy, then confirm from the logs that the next scheduled run is 00:05 London and not 00:05 UTC.

### Phase 7 — Verify in the season

33. Fire the HTTP trigger against Azure and confirm a booking end to end, including the arrival of the Picktime email.
34. Pin the section 10.4 query to an Azure dashboard, so a season of runs is one click away.
35. Let one scheduled run happen unattended, then check the logs, the Picktime emails and the Picktime site all agree.
36. Replace the one-line `README.md` with what a reader needs: what this does, how to run it locally, and which settings it requires.
37. Record the result in `TODO.md` and close out any remaining items.

---

## 17. Definition of done

* A scheduled run on Tuesday, Thursday and Friday at 00:05 London books 17:00, 18:00 and 19:00 seven days ahead.
* Target 2b is preferred and 3a is used per hour when 2b is taken.
* Runs outside the season do nothing, and say so in the logs.
* No secret or personal detail is in source, and both `.gitignore` files are committed.
* A booking `POST` is never automatically resent, so the automation cannot create a duplicate booking.
* All tests pass, including the BST and GMT time zone cases and the ambiguous-outcome cases.
* The GitHub Actions workflow builds, tests and deploys, with tests gating deployment.
* One real booking has been confirmed on the Picktime site from a run in Azure, and its confirmation email arrived.
* Application Insights sampling is off, so no log line from a run is ever discarded.
* The season-at-a-glance query is pinned to an Azure dashboard.
* The logs make it clear, without reading the code, what any given run did.
* `README.md` explains what the project does, how to run it locally, and which settings it needs.
