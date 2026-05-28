namespace EFCoreDemo.Models;

public sealed class ProductSummaryDto
{
    public int    Id       { get; init; }
    public string Name     { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}
