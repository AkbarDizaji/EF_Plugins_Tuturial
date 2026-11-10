namespace Demo01.Returning;

public class Booking
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoomId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}

public class BookingAudit
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int UserId { get; set; }
    public DateTime BookingEndedAt { get; set; }
    public DateTime DeletedAt { get; set; }
    public string Method { get; set; } = "";
}

/// <summary>What we need to remember about a deleted booking.</summary>
public record DeletedBooking(int BookingId, int UserId, DateTime EndsAt);

public record CleanupResult(IReadOnlyList<DeletedBooking> Audited, int DeletedCount);
