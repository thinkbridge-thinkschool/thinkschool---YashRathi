namespace OrderRefactorApi.Dtos
{
    public class OrderResponseDto
    {
        public int OrderId { get; set; }

        public decimal Total { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}