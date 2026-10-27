using Microsoft.AspNetCore.Mvc;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Api.Controllers;

/// <summary>US-E4-5: one mapping from decision outcomes to HTTP responses.</summary>
internal static class OrderDecisionProblems
{
    public static IActionResult ToActionResult(this ControllerBase controller, OrderDecisionResult result)
    {
        if (result.Outcome == OrderDecisionOutcome.Succeeded)
        {
            return controller.Ok(result.Order);
        }

        if (result.Outcome == OrderDecisionOutcome.ReasonRequired)
        {
            // A field-level validation error, so a form can put it under the box.
            var validation = new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["reason"] = new[] { result.Message ?? "A reason is required." }
            })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Reason required",
                Detail = result.Message
            };

            return controller.BadRequest(validation);
        }

        var (status, title) = result.Outcome switch
        {
            OrderDecisionOutcome.InvalidRequest => (StatusCodes.Status400BadRequest, "Invalid request"),
            OrderDecisionOutcome.TenantNotAvailable => (StatusCodes.Status401Unauthorized, "Tenant not available"),
            OrderDecisionOutcome.CallerNotPermitted => (StatusCodes.Status403Forbidden, "Caller scope missing"),
            OrderDecisionOutcome.OrderNotFound => (StatusCodes.Status404NotFound, "Order not found"),
            OrderDecisionOutcome.CancellationWindowClosed => (StatusCodes.Status409Conflict, "Cancellation window closed"),
            OrderDecisionOutcome.Conflict => (StatusCodes.Status409Conflict, "Order cannot change"),
            _ => (StatusCodes.Status500InternalServerError, "Decision failed")
        };

        var problem = new ProblemDetails { Status = status, Title = title, Detail = result.Message };

        if (result.WindowClosed is { } window)
        {
            // The elapsed time the story asks for, machine-readable as well
            // as in the message.
            problem.Extensions["confirmedAt"] = window.ConfirmedAt;
            problem.Extensions["windowClosedAt"] = window.WindowClosedAt;
            problem.Extensions["windowMinutes"] = window.WindowLength is { } length ? (int?)(int)length.TotalMinutes : null;
            problem.Extensions["elapsedSinceConfirmationMinutes"] = Minutes(window.ElapsedSinceConfirmation);
            problem.Extensions["closedMinutesAgo"] = Minutes(window.ClosedAgo);
            problem.Extensions["closedAgo"] = window.ClosedAgo is { } ago ? DurationText.Describe(ago) : null;
            problem.Extensions["checkedAt"] = window.CheckedAt;
        }

        return controller.StatusCode(status, problem);
    }

    private static int? Minutes(TimeSpan? value) => value is { } span ? (int)Math.Floor(span.TotalMinutes) : null;
}
