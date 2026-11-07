namespace AtelieMarylu.Domain.Shipping;

public record ShippingOption(string Code, string Name, decimal Amount, int? DeliveryDays);
