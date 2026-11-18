namespace Inventory.Domain;

public sealed class StockMovement
{
    private readonly List<StockMovementLine> _lines = [];

    private StockMovement() { }

    public StockMovement(
        StockMovementType type,
        string createdByUserId,
        DateTimeOffset createdAt,
        IEnumerable<StockMovementLine> lines)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        ArgumentNullException.ThrowIfNull(lines);

        var productIds = new HashSet<int>();
        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line);
            if (!productIds.Add(line.ProductId))
            {
                throw new ArgumentException("Each product may appear only once per movement.", nameof(lines));
            }

            _lines.Add(line);
        }

        if (_lines.Count == 0)
        {
            throw new ArgumentException("A movement must have at least one line.", nameof(lines));
        }

        Type = type;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public int Id { get; private set; }
    public StockMovementType Type { get; private set; }
    public string CreatedByUserId { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<StockMovementLine> Lines => _lines.AsReadOnly();
}
