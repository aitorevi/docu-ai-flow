namespace InvoiceProcessor.Domain.Dispatch;

public readonly record struct Quarter(int Year, int Number)
{
    public IReadOnlyList<int> Months => Number switch
    {
        1 => [1, 2, 3], 2 => [4, 5, 6], 3 => [7, 8, 9], 4 => [10, 11, 12],
        _ => throw new ArgumentOutOfRangeException(nameof(Number), Number, "El trimestre debe ser 1-4.")
    };

    public static Quarter Real(DateOnly date) => new(date.Year, (date.Month - 1) / 3 + 1);

    public (DateOnly Start, DateOnly End) ExcelSourceRange()
    {
        var lastMonth = Number * 3;
        var end = new DateOnly(Year, lastMonth, DateTime.DaysInMonth(Year, lastMonth));
        var start = Number == 1 ? new DateOnly(Year - 1, 10, 1) : new DateOnly(Year, 1, 1);
        return (start, end);
    }

    public Quarter? ExcelQuarterFor(DateOnly invoiceDate)
    {
        var (start, end) = ExcelSourceRange();
        return invoiceDate >= start && invoiceDate <= end ? this : null;
    }

    public override string ToString() => $"{Year}-Q{Number}";
}
