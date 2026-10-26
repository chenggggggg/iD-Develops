using iD_Develops.Enums;
using iD_Develops.Models;

namespace iD_Develops.Utilities;

public static class CatalogPricing
{
    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal DiscountedUnitPrice(CatalogProduct product, decimal original)
    {
        original = Money(Math.Max(0, original));
        var discount = product.DiscountType switch
        {
            CatalogDiscountType.Percentage => Money(original * Math.Clamp(product.DiscountValue ?? 0, 0, 100) / 100),
            CatalogDiscountType.FixedAmount => Money(Math.Max(0, product.DiscountValue ?? 0)),
            _ => 0
        };
        return Math.Max(0, original - discount);
    }

    public static CatalogPriceBreakdown Calculate(CatalogProduct product, int quantity = 1, decimal? fallbackPrice = null)
    {
        var original = Money(Math.Max(0, product.BasePrice ?? fallbackPrice ?? 0));
        var unitPrice = DiscountedUnitPrice(product, original);
        var count = Math.Max(1, quantity);
        var fees = new[] { product.RegistrationFee, product.TransactionFee, product.ServiceFee }
            .Select(fee => Money(Math.Max(0, fee ?? 0))).ToArray();
        var rate = Math.Clamp(product.VatPercentage ?? 0, 0, 100) / 100;
        var feeTotal = fees.Sum();
        var feeVat = fees.Sum(fee => Money(fee * rate));
        var subtotal = unitPrice * count + feeTotal;
        var vat = Money(unitPrice * count * rate) + feeVat;
        var originalTotal = original * count + feeTotal + Money(original * count * rate) + feeVat;
        return new(original, unitPrice, subtotal, feeTotal, vat, subtotal + vat, originalTotal);
    }

    public static void ValidateDiscount(CatalogProduct product)
    {
        if (!Enum.IsDefined(product.DiscountType))
            throw new ArgumentException("Select a valid discount type.");
        if (product.DiscountType == CatalogDiscountType.None) return;
        if (product.DiscountValue is not > 0 || Money(product.DiscountValue.Value) != product.DiscountValue)
            throw new ArgumentException("Enter a positive discount with at most two decimal places.");
        if (product.DiscountType == CatalogDiscountType.Percentage && product.DiscountValue > 100)
            throw new ArgumentException("A percentage discount cannot exceed 100%.");
        var price = product.BasePrice ?? product.Variants.Where(variant => variant.IsActive)
            .Select(variant => (decimal?)variant.Price).Min();
        if (!price.HasValue)
            throw new ArgumentException("Set a product price before adding a discount.");
        if (product.DiscountType == CatalogDiscountType.FixedAmount && product.DiscountValue > price)
            throw new ArgumentException("A fixed discount cannot exceed the product price.");
    }
}

public sealed record CatalogPriceBreakdown(decimal OriginalUnitPrice, decimal UnitPrice, decimal Subtotal,
    decimal Fees, decimal Vat, decimal Total, decimal OriginalTotal);
