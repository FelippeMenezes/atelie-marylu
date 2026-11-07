namespace AtelieMarylu.Domain.Shipping;

public record ShippingPackage(int WeightGrams, int HeightCm, int WidthCm, int LengthCm, int Quantity);
