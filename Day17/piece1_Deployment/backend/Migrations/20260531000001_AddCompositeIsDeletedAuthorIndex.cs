using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotesApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeIsDeletedAuthorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the simple Author index added in the previous migration.
            migrationBuilder.DropIndex(
                name: "IX_Quotes_Author",
                table: "Quotes");

            // Replace with a composite (IsDeleted, Author) index.
            // WHERE IsDeleted=0 ORDER BY Author can now be satisfied entirely from the
            // index: SQLite seeks the IsDeleted=0 range and reads Authors in sorted order
            // without a separate filter pass or temp B-Tree sort.
            migrationBuilder.CreateIndex(
                name: "IX_Quotes_IsDeleted_Author",
                table: "Quotes",
                columns: new[] { "IsDeleted", "Author" });
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotes_IsDeleted_Author",
                table: "Quotes");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Author",
                table: "Quotes",
                column: "Author");
        }
    }
}
