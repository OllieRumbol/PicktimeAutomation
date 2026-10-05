# Picktime Automation — Specification

> **What this document is for:** It answers *what are we building, and why?* It is the source of truth for requirements.

Status: Approved
Last updated: 2026-10-05

This document is the record of requirements for this project. It says what must be true, not how it is achieved. Each fact is stated once. Keep it updated as requirements change.

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
   The three application projects target .NET 6, which reached end of support in November 2024. The two test projects already target .NET 10. Functions worker packages are correspondingly old (`Microsoft.Azure.Functions.Worker` 1.6.0).

8. **No CI/CD.**
   `.github/` exists but is empty.

9. **Application Insights sampling is enabled.**
   `PicktimeAutomation.AzureFunctions/host.json` sets `samplingSettings.isEnabled` to `true`. Sampling can discard the one log line that explains a failed run.

---

## 5. External API

Base address: `https://www.picktime.com/`

Picktime timestamps are integers in the form `yyyyMMddHHmm`. For example `202609291800` means 18:00 on 29 September 2026. This spec calls that a *Picktime timestamp*. Picktime timestamps are in London local time, matching the `timezone` value of `Europe/London` sent with every request.

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

* The current code sends only twelve of these fields. The remaining fields come from the club's booking form. Send the payload exactly as captured, since a booking is known to succeed with it and the cost of sending the extra fields is nothing.
* `alt_number_Ext` has that unusual capitalisation in the real payload. Keep it exactly.
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
A failure has `status: false` with an explanatory `message`.

### 5.3 Authentication

A `scantoken` header carries a JSON Web Token. Verified properties:

* The payload contains `iss`, `accountId`, `userId` and `iat`. There is no `exp` claim.
* `userId` is `null`, so it is an anonymous token issued to any visitor of the public booking page.
* **Reuse is proven.** A token issued on 25 March 2026 was used on 28 September 2026 to create a real booking, which returned `200 OK` and `status: true`. The token is therefore long-lived across at least six months, and is not tied to a browser session.

Requirements that follow:

1. The token can be replaced without a code change.
2. If the API rejects the token, the run reports authentication as the cause. Silence is the failure mode to avoid.

### 5.4 Headers

A booking has been created successfully from Postman, outside any browser session, using a six-month-old token. So no browser, no live session and no fresh token is required. That removes the main risk.

One detail is still open. The captured availability request sends `browserid`, `x-requested-with: XMLHttpRequest`, a `referer`, and cookies including `pt_csrf` and `pt_slot_hold_check`, whose value equals the `browserid`. The successful Postman booking was sent with 25 headers, so it is not yet known which of those the API actually requires.

This is a verification task rather than a blocker. See section 7.

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

**A late run books only inside the late-run window.** A scheduled run can start late, for example while the host starts up. A late run still books when it starts on a run day, in London time, before 01:00. The booking date is then correct, because it is London today plus 7 days, exactly as for a run on time.

A late run that starts at 01:00 or later, or on a day that is not a run day, books nothing, because the booking date might not be one a scheduled run would book. It logs a warning that the run was missed. A missed run is caught up with the manual trigger (section 6.7).

After an outage of more than one day, only the latest run day is booked, if the late run starts on it before 01:00. An earlier run day missed in the outage is not reported separately. The absence of its confirmation emails shows it.

Worked examples, for the run scheduled at 00:05 London on Tuesday 13 October 2026:

| Late run starts (London) | Result |
| --- | --- |
| Tuesday 13 October, 00:05:20 | Books Tuesday 20 October |
| Tuesday 13 October, 00:59:59 | Books Tuesday 20 October |
| Tuesday 13 October, 01:00:00 | Missed. Books nothing. |
| Wednesday 14 October, 00:30 | Missed. Books nothing. Wednesday is not a run day. |
| Thursday 15 October, 00:30, after an outage since Tuesday | Books Thursday 22 October, as Thursday's own run would. Tuesday's missed run is not reported separately. |

### 6.2 Time zone rules

This is the most likely source of a silent, wrong-day booking, so it is specified explicitly.

The booking window opens at **London** midnight. London is not UTC for part of the season:

* British Summer Time ends on the last Sunday of October. In the 2026/27 season that is 25 October 2026.
* British Summer Time starts again on the last Sunday of March. In 2027 that is 28 March 2027.

So roughly the first three and a half weeks of each season run in BST, where London midnight is 23:00 UTC on the previous day.

Two independent things must both use London time:

1. **The trigger time.** The run starts at 00:05 London time, in both GMT and BST.
2. **The booking date.** The date is computed from the current London time, not from the host clock, which is UTC.

Fixing only one of the two gives a booking that is a day out. Both must be in place. The booking date is covered by tests. The trigger time depends on how the hosting is configured, so it is checked after deployment.

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
            else if the result is unknown:
                follow "Unknown booking results" below, then move to the next hour
            else:
                record the failure, try the next target
    if no target was booked for this hour:
        record "no target available"

log a summary of all three hours
```

Notes:

* Availability is read once per target per run, not once per hour. That is 2 reads and up to 3 writes, so about 5 HTTP calls per run. The only extra read is after an unknown booking result, below.
* Availability is read before any booking is made. A slot that Picktime reports as free can still be taken by another archer in the seconds between the read and the write, so a booking failure is expected behaviour and must fall through to the next target rather than abort the run.
* The target list is configuration. Adding the other six target ids later extends the fallback chain with no code change.

**If an availability read fails**, treat that target as having no free hours, log a warning naming the target, and carry on. One unreachable target must not stop the other from being used. If every availability read fails, every hour records `NoAvailability` and the run summary says so.

**If a read or booking fails because the token is rejected**, the run reports authentication as the cause (section 5.3), not as no availability or a failed booking. The run stops there, because every further request would be rejected too. Hours not yet finished record `Failed`, with authentication as the reason.

**Unknown booking results.** Sometimes Picktime gives no clear answer to a booking request, for example no response at all. The booking may or may not exist. Then:

1. Read availability again for that target and hour.
2. If the hour has gone, record `Unconfirmed`. Do not try another target.
3. If the hour is still free, the booking did not take. Try the same target once more.
4. If that attempt is also unknown, record `Unconfirmed`. Make no further attempts on any target.

After an unknown result, the automation never books a second target for that hour. Losing an hour is accepted in preference to a possible duplicate.

### 6.5 Outcome per hour

Each hour ends in exactly one of these states, and each is logged:

| Outcome | Meaning |
| --- | --- |
| `Booked` | A target was booked. The target name and booking id are logged. |
| `NoAvailability` | No target in the chain had that hour free. |
| `Failed` | A target was free but every booking attempt was rejected. |
| `Unconfirmed` | A booking may exist, but Picktime did not confirm it (section 6.4). It is logged as a warning. The Picktime confirmation email shows whether it was booked. |

Each hour is independent. A failure on one hour must never stop the other hours from being attempted. The one exception is a rejected token, which stops the run (section 6.4).

A run is a success only when all configured hours reach `Booked`. Partial success is reported as partial, not as failure. An `Unconfirmed` hour makes a run partial.

A run outside the season is reported as skipped, and appears in the run record like any other run. So does a late run that books nothing (section 6.1), and a run that fails with an unexpected error.

### 6.6 Double booking

No separate state store is needed. A slot already booked, whether by this automation or by hand, is absent from the availability response. The availability read is therefore the guard against double booking on one target.

It does not stop a second run for the same booking date from booking the fallback target for an hour already held on the preferred target. So two runs for the same booking date must not both book. Section 9 records the two cases where this is accepted, and the rules that keep them rare.

### 6.7 Manual trigger

An HTTP-triggered function exists alongside the timer, for testing and for catching up a missed run.

| Item | Value |
| --- | --- |
| Method and route | `POST /api/book` |
| Authorisation | Protected, so the URL alone is not enough to fire a booking |
| Query string | Optional `bookingDate` as `yyyy-MM-dd`, for example `?bookingDate=2026-10-13` |
| Default | When no date is given, use the same rule as the timer: London today plus 7 days |
| Season gate | Applies, exactly as the timer does |
| Invalid date | A `bookingDate` that is not a valid `yyyy-MM-dd` date is rejected with a clear error. Nothing is booked. |
| Past date | A `bookingDate` before today, in London time, is rejected with a clear error. Nothing is booked. Today is allowed. |
| Unexpected error | Reported as a failed run, with no internal details. The full detail goes to the logs. |
| Response | The run summary as JSON, so the outcome is visible without opening the logs |

It uses the same code path as the timer, so manual and scheduled runs cannot behave differently.

---

## 7. Remaining unknowns

Both endpoints are now captured and a booking has been proven end to end, so nothing blocks implementation. Two small things are still to be pinned down, both cheaply, before the first real booking.

### 7.1 The minimum header set

The successful Postman booking carried 25 headers. It is not known which are required. Find out by removing headers from that same Postman request and re-sending against a free slot, in this order:

1. Remove all cookies. Re-send.
2. Remove `browserid`. Re-send.
3. Remove `x-requested-with` and `referer`. Re-send.

Stop at the first step that fails, and keep whatever the last successful attempt sent. The goal is to send as little as possible, because every extra header is one more thing that can change under us. Record the answer in section 5.4.

### 7.2 What a rejection looks like

No rejected booking has been captured. Get one by posting a booking for a slot that is already taken — any slot that is already taken can serve as the test case, for example one booked by hand. Record the exact `status`, `message` and HTTP status code in section 5.2.

---

## 8. Assumptions

Recorded so they can be corrected rather than discovered later.

1. There is no club limit on how many slots one archer may hold. The automation will attempt 9 bookings per week.
2. Indoor booking is free, so `cost` stays `0` and `payment_required` stays `false`.
3. Bookings are for one archer only. Confirmed: no additional archers, ever. `booking_addnl_fields` is therefore a fixed constant, `{"ADDITIONAL ARCHER":""}`, sent exactly as the verified booking sent it. It never varies.
4. Cancellation is out of scope. Unwanted bookings are cancelled by hand.
5. The season is a fixed 1 October to 31 March, the same every year.
6. Slots are always exactly one hour, and 17:00 to 20:00 is always within the hall's opening hours.
7. The `scantoken` remains valid for the whole season. Reuse is proven over six months, so this is now evidenced rather than assumed. If it ever stops working, the fix is to capture a new one and update one application setting.
8. A pay-as-you-go Azure subscription is available. The resource group and Function App names will be settled when the Azure resources are created.
9. The 7-day window is a release rule, not an API restriction. The verified booking was made 1 day ahead, so the endpoint accepts any date whose slots have been released. The automation still uses 7 days, because that is when the slots appear.

One observation on timing: bookings are made by hand until the automation is deployed. The first automated booking comes from the first scheduled run after deployment, for the date 7 days later.

---

## 9. Definition of done

* A scheduled run on Tuesday, Thursday and Friday at 00:05 London books 17:00, 18:00 and 19:00 seven days ahead.
* Target 2b is preferred and 3a is used per hour when 2b is taken.
* Runs outside the season do nothing, and say so in the logs.
* No secret is in the repository's current files, and no personal detail is in the source code.
* The automation never holds two bookings for the same hour, on the same target or different targets, even when a booking request times out. Three exceptions are accepted, because each needs an unusual event and only one person runs the automation:
  1. A manual run for the same booking date as a scheduled run. The manual trigger is not used between 00:05 and 01:00 on a run day, because a late scheduled run can start at any time in that window (section 6.1). It is not used for a booking date that a run has already booked.
  2. A scheduled run that is run again after the host stops in the middle of it, and restarts before 01:00 (section 6.1). The second run can book the fallback target for hours the first run booked. No deployment is made between 00:05 and 01:00 on a run day.
  3. A late run for a run day missed in an outage of more than one day, when the host starts a few seconds before the run time on a later run day. The late run starts just after the run time, so it books (section 6.1), and that day's own run then books the same booking date again. The second run can book the fallback target for hours the first run booked.
* All tests pass.
* Deployment is automated, and a failing test stops a deployment.
* One real booking has been confirmed on the Picktime site from a run in Azure, and its confirmation email arrived.
* No log line from a run is ever discarded, except when a daily cap on log volume is reached, which only a runaway logging fault can cause.
* Every run in the last 90 days can be reviewed in one place.
* The logs make it clear, without reading the code, what any given run did.
* `README.md` explains what the project does, how to run it locally, and which settings it needs.
