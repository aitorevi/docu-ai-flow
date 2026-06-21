namespace InvoiceProcessor.Domain.Invoices;

using SharpMonads.Core;

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Result<Money, Error> Create(decimal amount, string currency) =>
        string.IsNullOrWhiteSpace(currency) || currency.Length != 3
            ? Result<Money, Error>.Failure(Error.Validation($"Moneda inválida: '{currency}'. Se espera ISO 4217."))
            : Result<Money, Error>.Success(new Money(amount, currency));

    public static Money Zero(string currency) => new(0m, currency);

    public Result<Money, Error> Add(Money other) =>
        Currency != other.Currency
            ? Result<Money, Error>.Failure(Error.Validation($"Monedas distintas: {Currency} vs {other.Currency}."))
            : Result<Money, Error>.Success(this with { Amount = Amount + other.Amount });
}
