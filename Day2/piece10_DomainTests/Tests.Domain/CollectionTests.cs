using FluentAssertions;
using QuotesApi.Domain;
using Xunit;

namespace Tests.Domain;

public class CollectionTests
{
    [Fact]
    public void Create_EmptyName_Throws()
    {
        var act = () => Collection.Create("", "owner-1");

        act.Should().Throw<ArgumentException>()
           .WithMessage("*between 3 and 80*");
    }

    [Fact]
    public void Create_NameOver80Chars_Throws()
    {
        var act = () => Collection.Create(new string('x', 81), "owner-1");

        act.Should().Throw<ArgumentException>()
           .WithMessage("*between 3 and 80*");
    }

    [Fact]
    public void AddItem_51stItem_Throws()
    {
        var collection = Collection.Create("My Collection", "owner-1");
        for (var i = 1; i <= 50; i++) collection.AddItem(i);

        var act = () => collection.AddItem(51);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*more than 50 items*");
    }

    [Fact]
    public void AddItem_DuplicateQuoteId_Throws()
    {
        var collection = Collection.Create("My Collection", "owner-1");
        collection.AddItem(1);

        var act = () => collection.AddItem(1);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*already in this collection*");
    }

    [Fact]
    public void RemoveItem_NonExistentId_Throws()
    {
        var collection = Collection.Create("My Collection", "owner-1");

        var act = () => collection.RemoveItem(99);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*not in this collection*");
    }

    [Fact]
    public void AddThenRemove_LeavesZeroItems()
    {
        var collection = Collection.Create("My Collection", "owner-1");
        collection.AddItem(1);
        collection.RemoveItem(1);

        collection.Items.Should().BeEmpty();
    }
}
