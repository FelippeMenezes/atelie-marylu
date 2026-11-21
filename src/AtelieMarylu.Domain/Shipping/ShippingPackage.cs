namespace AtelieMarylu.Domain.Shipping;

// UnitPrice é o valor declarado de cada unidade: as transportadoras usam no seguro (e devolvem esse valor se o pacote sumir)
public record ShippingPackage(int WeightGrams, int HeightCm, int WidthCm, int LengthCm, int Quantity, decimal UnitPrice = 0);
