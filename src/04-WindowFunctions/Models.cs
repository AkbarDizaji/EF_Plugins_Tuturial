namespace Demo04.WindowFunctions;

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Transaction
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Kind { get; set; } = "";   // Deposit, Purchase, Withdrawal, ...
    public decimal Amount { get; set; }      // positive = money in, negative = money out
}

/// <summary>One line of an account statement.</summary>
public record StatementLine(int Id, DateTime CreatedAt, string Kind, decimal Amount, decimal Balance);

/// <summary>A page of the statement plus how many rows SQL Server had to send to produce it.</summary>
public record StatementPage(IReadOnlyList<StatementLine> Lines, int RowsTransferred);
