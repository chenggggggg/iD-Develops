using Stripe;

namespace iD_Develops.Utilities;

public static class ExternalDependencyAvailability
{
    public static bool IsStripeUnavailable(Exception exception)
        => TryGetStripeErrorCode(exception, out _);

    public static string GetStripeErrorCode(Exception exception)
        => TryGetStripeErrorCode(exception, out var errorCode)
            ? errorCode
            : "stripe-check-failed";

    private static bool TryGetStripeErrorCode(Exception exception, out string errorCode)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is not StripeException stripeException)
            {
                continue;
            }

            var statusCode = (int?)stripeException.HttpStatusCode;
            errorCode = statusCode switch
            {
                StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "stripe-authentication-failed",
                StatusCodes.Status408RequestTimeout => "stripe-timeout",
                StatusCodes.Status429TooManyRequests => "stripe-rate-limited",
                >= 500 => "stripe-offline",
                _ => string.Empty
            };

            if (errorCode.Length == 0 && HasHttpFailure(stripeException))
            {
                errorCode = "stripe-offline";
            }

            return errorCode.Length > 0;
        }

        errorCode = string.Empty;
        return false;
    }

    private static bool HasHttpFailure(Exception exception)
    {
        for (var current = exception.InnerException; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException)
            {
                return true;
            }
        }

        return false;
    }
}
