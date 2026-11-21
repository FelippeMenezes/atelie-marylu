namespace AtelieMarylu.Domain.Shipping;

// Como o pacote sai do ateliê, a partir do código de frete gravado no pedido (Order.ShippingCode).
// Serve para o painel dizer à Marylu o que fazer: esperar a cliente, esperar a transportadora ou levar o pacote.
public enum ShippingHandover
{
    // A cliente busca no ateliê ("retirada")
    CustomerPickup,

    // A transportadora busca no ateliê (no Melhor Envio, só a Loggi Coleta tem coleta confirmada)
    CarrierPickup,

    // A Marylu leva o pacote etiquetado a uma agência ou a um ponto parceiro da transportadora
    DropOff,
}

public static class ShippingHandoverRules
{
    // Mesmos códigos de StudioPickupCalculator.Code e MelhorEnvioShippingCalculator (Infrastructure), repetidos aqui
    // porque o Domain não pode depender da Infrastructure. Os testes conferem que continuam iguais.
    public const string StudioPickupCode = "retirada";

    // "me-" + id da modalidade no Melhor Envio. 32 = Loggi Coleta (lista em GET /api/v2/me/shipment/services)
    private static readonly HashSet<string> CarrierPickupCodes = ["me-32"];

    // Na dúvida, DropOff: é o caso que exige ação da Marylu. Dizer "espere a coleta" quando ninguém vai vir
    // deixaria o pedido parado; dizer "leve à agência" quando haveria coleta só custa uma viagem.
    public static ShippingHandover For(string shippingCode) =>
        shippingCode == StudioPickupCode ? ShippingHandover.CustomerPickup
        : CarrierPickupCodes.Contains(shippingCode) ? ShippingHandover.CarrierPickup
        : ShippingHandover.DropOff;
}
