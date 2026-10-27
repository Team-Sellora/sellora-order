namespace Sellora.OrderService.Domain.VanReturns;

public enum VanReturnFailure
{
    /// <summary>Bad lines or quantities — a 400.</summary>
    InvalidRequest,

    /// <summary>Another agency's return; shown as not found.</summary>
    NotVisible,

    /// <summary>Already accepted with different counts — a 409.</summary>
    AlreadyAccepted
}

/// <summary>A van return broke a rule; the message is shown to the caller as-is.</summary>
public sealed class VanReturnRuleException : Exception
{
    public VanReturnRuleException(VanReturnFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public VanReturnFailure Failure { get; }
}
