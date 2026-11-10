namespace Demo02.QueryComplexity;

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "";
    public List<OrderItem> Items { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<OrderHistory> History { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}

public class OrderHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public DateTime ChangedAt { get; set; }
    public string Status { get; set; } = "";
}
