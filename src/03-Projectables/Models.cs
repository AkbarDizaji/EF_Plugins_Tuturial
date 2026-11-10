using EntityFrameworkCore.Projectables;

namespace Demo03.Projectables;

// Rounding assumptions (identical in C# and SQL, so totals match to the last digit):
//   • UnitPrice is decimal(18,2); Quantity is int.
//   • DiscountRate and TaxRate are decimal(4,2) — whole percentages such as 0.10 or 0.20.
//   • Total = Subtotal × (1 − DiscountRate) × (1 + TaxRate) is EXACT at 6 decimal places.
//     No intermediate rounding: 2 + 2 + 2 decimal places never exceed SQL Server's 6-place minimum scale.
//   • Rounding to cents (MidpointRounding.AwayFromZero) happens only when displaying a total.

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

/// <summary>BEFORE: an ordinary C# calculated property. Works in memory; EF Core cannot look inside it.</summary>
public class PlainOrder
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public decimal DiscountRate { get; set; }
    public decimal TaxRate { get; set; }
    public List<OrderItem> Items { get; set; } = [];

    public decimal Total =>
        Items.Sum(i => i.UnitPrice * i.Quantity) * (1 - DiscountRate) * (1 + TaxRate);
}

/// <summary>AFTER: the same formula, marked [Projectable]. Still works in memory — and now also in SQL.</summary>
public class Order
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public decimal DiscountRate { get; set; }
    public decimal TaxRate { get; set; }
    public List<OrderItem> Items { get; set; } = [];

    [Projectable]
    public decimal Subtotal => Items.Sum(i => i.UnitPrice * i.Quantity);

    [Projectable]
    public decimal Total => Subtotal * (1 - DiscountRate) * (1 + TaxRate);
}

public record OrderTotal(int Id, string Number, decimal Total);
