using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services;
using PicktimeAutomation.Services.Dates;
using PicktimeAutomation.Services.Exceptions;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Tests 1 to 6, 9 to 11 and 31 (plan section 7.3), and tests 12 to 14 for unknown booking results
/// (plan section 7.4), for the booking algorithm in spec section 6.4 and the verdict table in plan section 3.1.
/// The booking date is passed in, so the clock plays no part.
/// </summary>
[TestClass]
public sealed class BookingAlgorithmTests
{
    private const string Target2b = "fake-resource-2b";
    private const string Target3a = "fake-resource-3a";
    private const long Slot17 = 202610131700;
    private const long Slot18 = 202610131800;

    // Tuesday 13 October 2026, inside the season.
    private static readonly DateOnly BookingDate = new(2026, 10, 13);

    private readonly FakeLogger<PicktimeBookingService> _logger = new();

    // Test 1
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_EveryHourFreeOn2b_BooksAllThreeOn2b()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.Success, summary.Verdict);
        Assert.AreEqual(3, summary.BookedCount);
        Assert.IsTrue(summary.Attempts.All(attempt => attempt.TargetName == "2b" && attempt.BookingId == FakePicktimeApiService.BookingId));
        CollectionAssert.AreEqual(new[] { 17, 18, 19 }, summary.Attempts.Select(attempt => attempt.Hour).ToArray());
        CollectionAssert.AreEqual(
            new[] { (Target2b, 202610131700L), (Target2b, 202610131800L), (Target2b, 202610131900L) },
            BookingCalls(api));
        Assert.AreEqual(2, api.AvailabilityReadCount);
    }

    // Test 2
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_OneHourTakenOn2b_BooksThatHourOn3aAndTheOthersOn2b()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 19)
            .WithFreeHours(Target3a, 17, 18, 19);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.Success, summary.Verdict);
        CollectionAssert.AreEqual(new[] { "2b", "3a", "2b" }, summary.Attempts.Select(attempt => attempt.TargetName).ToArray());
        CollectionAssert.AreEqual(
            new[] { (Target2b, 202610131700L), (Target3a, 202610131800L), (Target2b, 202610131900L) },
            BookingCalls(api));
    }

    // Test 3
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_HourTakenOnBothTargets_RecordsNoAvailabilityAndBooksTheOtherHours()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 19)
            .WithFreeHours(Target3a, 17, 19);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(BookingOutcome.NoAvailability, OutcomeAt(summary, 18));
        Assert.AreEqual(BookingOutcome.Booked, OutcomeAt(summary, 17));
        Assert.AreEqual(BookingOutcome.Booked, OutcomeAt(summary, 19));
        Assert.IsFalse(api.BookingRequests.Any(request => request.DateTimeOfBooking == 202610131800));
        Assert.AreEqual(RunVerdict.Partial, summary.Verdict);
    }

    // Test 4
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_FreeHourRejectedOn2b_BooksItOn3a()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Rejected());

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        var hour18 = AttemptAt(summary, 18);
        Assert.AreEqual(BookingOutcome.Booked, hour18.Outcome);
        Assert.AreEqual("3a", hour18.TargetName);
        CollectionAssert.AreEqual(
            new[] { (Target2b, 202610131800L), (Target3a, 202610131800L) },
            BookingCalls(api).Where(call => call.Slot == 202610131800).ToArray());
        Assert.AreEqual(RunVerdict.Success, summary.Verdict);
    }

    // Test 5
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_FreeHourRejectedOnBothTargets_RecordsFailed()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Rejected())
            .WithBookingResults(Target3a, 18, FakePicktimeApiService.Rejected());

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        var hour18 = AttemptAt(summary, 18);
        Assert.AreEqual(BookingOutcome.Failed, hour18.Outcome);
        Assert.AreEqual("Slot not available", hour18.ErrorMessage);
        Assert.AreEqual(1, summary.FailedCount);
        Assert.AreEqual(0, summary.NoAvailabilityCount);
        Assert.AreEqual(2, summary.BookedCount);
    }

    // Test 6
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_UnexpectedExceptionOnOneHour_BooksTheOtherHours()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingException(Target2b, 18, new InvalidOperationException("Something unexpected broke."));

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        var hour18 = AttemptAt(summary, 18);
        Assert.AreEqual(BookingOutcome.Failed, hour18.Outcome);
        Assert.AreEqual("Something unexpected broke.", hour18.ErrorMessage);
        Assert.AreEqual(BookingOutcome.Booked, OutcomeAt(summary, 17));
        Assert.AreEqual(BookingOutcome.Booked, OutcomeAt(summary, 19));
        Assert.AreEqual(RunVerdict.Partial, summary.Verdict);

        // The POST may have reached Picktime before the error, so 3a is not tried for that hour.
        CollectionAssert.AreEqual(
            new[] { (Target2b, 202610131800L) },
            BookingCalls(api).Where(call => call.Slot == 202610131800).ToArray());
    }

    // Test 9
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_ReadFailsFor2b_TreatsItAsFullAndBooksOn3a()
    {
        var api = new FakePicktimeApiService()
            .WithReadException(Target2b, new PicktimeReadException("The availability read timed out."))
            .WithFreeHours(Target3a, 17, 18, 19);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.Success, summary.Verdict);
        Assert.IsTrue(summary.Attempts.All(attempt => attempt.TargetName == "3a"));
        Assert.IsTrue(api.BookingRequests.All(request => request.ResourceId == Target3a));
        CollectionAssert.AreEqual(new[] { "2b" }, summary.FailedReads.ToArray());
        CollectionAssert.AreEqual(new[] { "2b" }, WarningTargetNames());
    }

    // Test 10
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_BothReadsFail_RecordsNoAvailabilityAndBooksNothing()
    {
        var api = new FakePicktimeApiService()
            .WithReadException(Target2b, new PicktimeReadException("The availability read returned HTTP 500."))
            .WithReadException(Target3a, new PicktimeReadException("The availability read returned HTTP 500."));

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(3, summary.NoAvailabilityCount);
        Assert.IsEmpty(api.BookingRequests);
        CollectionAssert.AreEqual(new[] { "2b", "3a" }, summary.FailedReads.ToArray());
        CollectionAssert.AreEquivalent(new[] { "2b", "3a" }, WarningTargetNames());
        Assert.AreEqual(RunVerdict.Failure, summary.Verdict);
    }

    // Test 11: two hours booked and one not is partial, whichever way the third hour ended.
    [TestMethod]
    [DataRow(BookingOutcome.NoAvailability)]
    [DataRow(BookingOutcome.Failed)]
    [DataRow(BookingOutcome.Unconfirmed)]
    public async Task BookArcheryIndoorTargetAsync_TwoHoursBookedAndOneNot_ReportsPartial(BookingOutcome hour18Outcome)
    {
        var api = new FakePicktimeApiService().WithFreeHours(Target2b, 17, 18, 19);
        switch (hour18Outcome)
        {
            case BookingOutcome.NoAvailability:
                api.WithFreeHours(Target2b, 17, 19);
                break;
            case BookingOutcome.Failed:
                api.WithBookingResults(Target2b, 18, FakePicktimeApiService.Rejected());
                break;
            case BookingOutcome.Unconfirmed:
                api.WithBookingResults(Target2b, 18, FakePicktimeApiService.Unknown())
                    .WithFreeHoursOnReRead(Target2b, 19);
                break;
        }

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(hour18Outcome, OutcomeAt(summary, 18));
        Assert.AreEqual(2, summary.BookedCount);
        Assert.AreEqual(RunVerdict.Partial, summary.Verdict);
    }

    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_EveryHourRejected_ReportsFailure()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithBookingResults(Target2b, 17, FakePicktimeApiService.Rejected())
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Rejected())
            .WithBookingResults(Target2b, 19, FakePicktimeApiService.Rejected());

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(3, summary.FailedCount);
        Assert.AreEqual(RunVerdict.Failure, summary.Verdict);
    }

    // Test 12
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_UnknownResultAndHourGoneOnReRead_RecordsUnconfirmedAndBooksNothingMore()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Unknown())
            .WithFreeHoursOnReRead(Target2b, 19);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        AssertUnconfirmedOn2b(summary, 18);
        AssertReReadOf2b(api);
        CollectionAssert.AreEqual(new[] { (Target2b, Slot18) }, BookingCallsAt(api, Slot18));
        CollectionAssert.AreEqual(new[] { "2b" }, WarningTargetNames());
        Assert.AreEqual(RunVerdict.Partial, summary.Verdict);
    }

    // Test 13
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_UnknownResultAndHourStillFree_BooksOnTheSameTargetWithOneMoreAttempt()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Unknown(), FakePicktimeApiService.Succeeded());

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        var hour18 = AttemptAt(summary, 18);
        Assert.AreEqual(BookingOutcome.Booked, hour18.Outcome);
        Assert.AreEqual("2b", hour18.TargetName);
        Assert.AreEqual(FakePicktimeApiService.BookingId, hour18.BookingId);
        AssertReReadOf2b(api);
        CollectionAssert.AreEqual(new[] { (Target2b, Slot18), (Target2b, Slot18) }, BookingCallsAt(api, Slot18));
        Assert.AreEqual(RunVerdict.Success, summary.Verdict);
    }

    // Test 14 (Unknown), and rule a in spec section 6.4 (Rejected): Picktime may now show the first
    // request's booking, so a rejection proves nothing.
    [TestMethod]
    [DataRow(BookingResultStatus.Unknown)]
    [DataRow(BookingResultStatus.Rejected)]
    public async Task BookArcheryIndoorTargetAsync_UnknownResultThenNotBooked_RecordsUnconfirmedWithNoThirdAttempt(BookingResultStatus secondStatus)
    {
        var secondResult = secondStatus == BookingResultStatus.Unknown
            ? FakePicktimeApiService.Unknown()
            : FakePicktimeApiService.Rejected();
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Unknown(), secondResult);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        AssertUnconfirmedOn2b(summary, 18);
        CollectionAssert.AreEqual(new[] { (Target2b, Slot18), (Target2b, Slot18) }, BookingCallsAt(api, Slot18));
        CollectionAssert.AreEqual(new[] { "2b" }, WarningTargetNames());
        StringAssert.Contains(AttemptAt(summary, 18).ErrorMessage, "The first booking result was unknown.");
    }

    // The re-read and the second attempt go to the target that gave the unknown result, here the fallback.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_UnknownResultOn3a_ReReadsAndBooks3aOnly()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Rejected())
            .WithBookingResults(Target3a, 18, FakePicktimeApiService.Unknown(), FakePicktimeApiService.Succeeded());

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        var hour18 = AttemptAt(summary, 18);
        Assert.AreEqual(BookingOutcome.Booked, hour18.Outcome);
        Assert.AreEqual("3a", hour18.TargetName);
        Assert.HasCount(3, api.AvailabilityReads);
        Assert.AreEqual(Target3a, api.AvailabilityReads[2]);
        CollectionAssert.AreEqual(
            new[] { (Target2b, Slot18), (Target3a, Slot18), (Target3a, Slot18) },
            BookingCallsAt(api, Slot18));
    }

    // Rule b in spec section 6.4: the hour stays unconfirmed, and the rejected token stops the run.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_ReReadRejectsTheToken_RecordsUnconfirmedAndStops()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Unknown())
            .WithReReadException(Target2b, new PicktimeAuthenticationException("The availability read returned HTTP 401."));

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.AuthenticationFailed, summary.Verdict);
        Assert.AreEqual(BookingOutcome.Booked, OutcomeAt(summary, 17));
        AssertUnconfirmedOn2b(summary, 18);
        AssertFailedForAuthentication(summary, 19);
        CollectionAssert.AreEqual(new[] { (Target2b, Slot17), (Target2b, Slot18) }, BookingCalls(api));
        CollectionAssert.AreEqual(new[] { "2b" }, WarningTargetNames());
    }

    // A second booking request rejected for the token is handled the same way as rule b.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_SecondAttemptRejectsTheToken_RecordsUnconfirmedAndStops()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Unknown())
            .WithBookingException(Target2b, 18, new PicktimeAuthenticationException("The booking request returned HTTP 401."));

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.AuthenticationFailed, summary.Verdict);
        AssertUnconfirmedOn2b(summary, 18);
        AssertFailedForAuthentication(summary, 19);
        CollectionAssert.AreEqual(new[] { (Target2b, Slot17), (Target2b, Slot18), (Target2b, Slot18) }, BookingCalls(api));
    }

    // Plan section 4.1: a failed re-read leaves the booking as uncertain as before, whatever the error.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BookArcheryIndoorTargetAsync_ReReadFails_RecordsUnconfirmedAndBooksTheOtherHours(bool isUnexpectedError)
    {
        Exception reReadException = isUnexpectedError
            ? new InvalidOperationException("Something unexpected broke.")
            : new PicktimeReadException("The availability read returned HTTP 500.");
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Unknown())
            .WithReReadException(Target2b, reReadException);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        AssertUnconfirmedOn2b(summary, 18);
        AssertReReadOf2b(api);
        CollectionAssert.AreEqual(new[] { (Target2b, Slot18) }, BookingCallsAt(api, Slot18));
        Assert.AreEqual(BookingOutcome.Booked, OutcomeAt(summary, 19));
        Assert.AreEqual(RunVerdict.Partial, summary.Verdict);
        CollectionAssert.AreEqual(new[] { "2b" }, WarningTargetNames());
    }

    // An unexpected error on the second attempt is unconfirmed, not failed: the first request may have booked the hour.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_SecondAttemptThrows_RecordsUnconfirmedAndDoesNotTry3a()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingResults(Target2b, 18, FakePicktimeApiService.Unknown())
            .WithBookingException(Target2b, 18, new InvalidOperationException("Something unexpected broke."));

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        AssertUnconfirmedOn2b(summary, 18);
        CollectionAssert.AreEqual(new[] { (Target2b, Slot18), (Target2b, Slot18) }, BookingCallsAt(api, Slot18));
    }

    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_CallerCancelsDuringReRead_ThrowsAndBooksNothingMore()
    {
        // The token is cancelled only during the re-read, as when the host stops the run at that moment.
        using var cancellation = new CancellationTokenSource();
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithBookingResults(Target2b, 17, FakePicktimeApiService.Unknown())
            .WithReReadException(Target2b, () =>
            {
                cancellation.Cancel();
                return new OperationCanceledException(cancellation.Token);
            });
        var service = CreateService(api);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => service.BookArcheryIndoorTargetAsync(BookingDate, cancellation.Token));

        AssertReReadOf2b(api);
        Assert.HasCount(1, api.BookingRequests);
    }

    // Test 31
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_ReadRejectsTheToken_StopsWithAuthenticationFailed()
    {
        var api = new FakePicktimeApiService()
            .WithReadException(Target2b, new PicktimeAuthenticationException("The availability read returned HTTP 401."))
            .WithFreeHours(Target3a, 17, 18, 19);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.AuthenticationFailed, summary.Verdict);
        Assert.IsEmpty(api.BookingRequests);
        AssertFailedForAuthentication(summary, 17, 18, 19);
    }

    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_OneReadFailsAndTheOtherRejectsTheToken_ReportsAuthenticationFailedAndTheFailedRead()
    {
        var api = new FakePicktimeApiService()
            .WithReadException(Target2b, new PicktimeReadException("The availability read returned HTTP 500."))
            .WithReadException(Target3a, new PicktimeAuthenticationException("The availability read returned HTTP 403."));

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.AuthenticationFailed, summary.Verdict);
        AssertFailedForAuthentication(summary, 17, 18, 19);
        CollectionAssert.AreEqual(new[] { "2b" }, summary.FailedReads.ToArray());
    }

    // Spec section 6.4: one unreachable target must not stop the other from being used, whatever the error.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_UnexpectedErrorReading2b_TreatsItAsFullAndBooksOn3a()
    {
        var api = new FakePicktimeApiService()
            .WithReadException(Target2b, new InvalidOperationException("Something unexpected broke."))
            .WithFreeHours(Target3a, 17, 18, 19);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.Success, summary.Verdict);
        Assert.IsTrue(summary.Attempts.All(attempt => attempt.TargetName == "3a"));
        CollectionAssert.AreEqual(new[] { "2b" }, summary.FailedReads.ToArray());
    }

    // Plan section 3.4: the reads are independent, so they run at the same time.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_TwoTargets_ReadsBothConcurrently()
    {
        // Each read waits until both have started. Sequential reads time out, so both targets read as failed.
        var api = new FakePicktimeApiService()
            .WithReadsHeldUntilStarted(2)
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19);

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.IsEmpty(summary.FailedReads);
        Assert.AreEqual(RunVerdict.Success, summary.Verdict);
    }

    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_BookingRejectsTheToken_KeepsFinishedHoursAndStops()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithFreeHours(Target3a, 17, 18, 19)
            .WithBookingException(Target2b, 18, new PicktimeAuthenticationException("The booking request returned HTTP 401."));

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.AuthenticationFailed, summary.Verdict);
        Assert.AreEqual(BookingOutcome.Booked, OutcomeAt(summary, 17));
        AssertFailedForAuthentication(summary, 18, 19);
        CollectionAssert.AreEqual(
            new[] { (Target2b, 202610131700L), (Target2b, 202610131800L) },
            BookingCalls(api));
    }

    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_CallerCancels_ThrowsAndBooksNothingMore()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithBookingException(Target2b, 17, new OperationCanceledException(cancellation.Token));
        var service = CreateService(api);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => service.BookArcheryIndoorTargetAsync(BookingDate, cancellation.Token));

        Assert.HasCount(1, api.BookingRequests);
    }

    private PicktimeBookingService CreateService(FakePicktimeApiService api)
    {
        var bookingOptions = new BookingOptions
        {
            DaysAhead = 7,
            Hours = [17, 18, 19],
            Targets =
            [
                new BookingTargetOptions { Name = "2b", ResourceId = Target2b },
                new BookingTargetOptions { Name = "3a", ResourceId = Target3a },
            ],
            SeasonStart = "10-01",
            SeasonEnd = "03-31",
        };

        return new PicktimeBookingService(api, Options.Create(bookingOptions), new LondonClock(new FakeTimeProvider()), _logger);
    }

    private static BookingAttempt AttemptAt(BookingSummary summary, int hour)
    {
        return summary.Attempts.Single(attempt => attempt.Hour == hour);
    }

    private static BookingOutcome OutcomeAt(BookingSummary summary, int hour)
    {
        return AttemptAt(summary, hour).Outcome;
    }

    private static (string ResourceId, long Slot)[] BookingCalls(FakePicktimeApiService api)
    {
        return api.BookingRequests.Select(request => (request.ResourceId, request.DateTimeOfBooking)).ToArray();
    }

    private static (string ResourceId, long Slot)[] BookingCallsAt(FakePicktimeApiService api, long slot)
    {
        return BookingCalls(api).Where(call => call.Slot == slot).ToArray();
    }

    /// <summary>One read of each target, then the re-read of 2b after its unknown result.</summary>
    private static void AssertReReadOf2b(FakePicktimeApiService api)
    {
        Assert.HasCount(3, api.AvailabilityReads);
        Assert.AreEqual(Target2b, api.AvailabilityReads[2]);
    }

    private static void AssertUnconfirmedOn2b(BookingSummary summary, int hour)
    {
        var attempt = AttemptAt(summary, hour);
        Assert.AreEqual(BookingOutcome.Unconfirmed, attempt.Outcome);
        Assert.AreEqual("2b", attempt.TargetName);
    }

    private string?[] WarningTargetNames()
    {
        return _logger.Collector.GetSnapshot()
            .Where(record => record.Level == LogLevel.Warning)
            .Select(record => record.GetStructuredStateValue("TargetName"))
            .ToArray();
    }

    private static void AssertFailedForAuthentication(BookingSummary summary, params int[] hours)
    {
        foreach (var hour in hours)
        {
            var attempt = AttemptAt(summary, hour);
            Assert.AreEqual(BookingOutcome.Failed, attempt.Outcome);
            Assert.StartsWith("Authentication failed.", attempt.ErrorMessage);
        }
    }
}
