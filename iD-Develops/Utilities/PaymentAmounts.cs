using System.Globalization;

namespace iD_Develops.Utilities;

public static class PaymentAmounts
{
    // Stripe charge units, including its ISK/UGX compatibility exceptions:
    // https://docs.stripe.com/currencies#zero-decimal
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
        { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "VND", "VUV", "XAF", "XOF", "XPF" };

    public static string Format(long minorUnits, string currency, IFormatProvider? culture = null)
    {
        var zero = ZeroDecimal.Contains(currency);
        var integerDisplay = zero || currency.Equals("ISK", StringComparison.OrdinalIgnoreCase) || currency.Equals("UGX", StringComparison.OrdinalIgnoreCase);
        return currency.ToUpperInvariant() + " " + (minorUnits / (zero ? 1m : 100m))
            .ToString(integerDisplay ? "N0" : "N2", culture ?? CultureInfo.CurrentCulture);
    }
}
