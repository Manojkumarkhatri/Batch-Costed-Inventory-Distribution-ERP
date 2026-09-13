using FluentAssertions;
using ShopApp.Domain.Logic;
using Xunit;

namespace ShopApp.Tests;

public class FefoTests
{
    private static readonly DateTime Today = new(2026, 8, 26);

    private static List<AvailableBatch> Sample() => new()
    {
        // received first, expires last: plain FIFO would wrongly pick this
        new(1, "B-001", 100m, new DateTime(2026, 12, 31), new DateTime(2026, 1, 1), 200m),
        // received later, expires soonest: FEFO must pick this
        new(2, "B-002", 40m,  new DateTime(2026, 9, 10),  new DateTime(2026, 6, 1), 210m),
        new(3, "B-003", 60m,  new DateTime(2026, 10, 15), new DateTime(2026, 7, 1), 205m),
    };

    [Fact]
    public void Takes_soonest_expiry_first()
    {
        var r = FefoAllocator.Allocate(Sample(), 30m, Today);
        r.Allocations.Should().ContainSingle();
        r.Allocations[0].BatchNo.Should().Be("B-002");
    }

    [Fact]
    public void Spills_into_next_expiring_batch()
    {
        var r = FefoAllocator.Allocate(Sample(), 50m, Today);
        r.Allocations.Select(a => a.BatchNo).Should().Equal("B-002", "B-003");
        r.Allocations.Sum(a => a.Qty).Should().Be(50m);
    }

    [Fact]
    public void Reports_shortfall_when_stock_is_insufficient()
    {
        var r = FefoAllocator.Allocate(Sample(), 500m, Today);
        r.IsComplete.Should().BeFalse();
        r.Shortfall.Should().Be(300m);
    }

    [Fact]
    public void Never_allocates_expired_stock()
    {
        var batches = new List<AvailableBatch>
        {
            new(9,  "OLD",  100m, new DateTime(2026, 8, 1),  new DateTime(2026, 1, 1), 100m),
            new(10, "GOOD", 100m, new DateTime(2026, 11, 1), new DateTime(2026, 2, 1), 100m),
        };
        var r = FefoAllocator.Allocate(batches, 50m, Today);
        r.Allocations.Should().ContainSingle().Which.BatchNo.Should().Be("GOOD");
    }

    [Fact]
    public void Dated_stock_goes_before_never_expiring_stock()
    {
        var batches = new List<AvailableBatch>
        {
            new(11, "DRY",   100m, null,                      new DateTime(2026, 1, 1), 100m),
            new(12, "DATED", 100m, new DateTime(2026, 9, 30),  new DateTime(2026, 5, 1), 100m),
        };
        var r = FefoAllocator.Allocate(batches, 50m, Today);
        r.Allocations.Should().ContainSingle().Which.BatchNo.Should().Be("DATED");
    }

    [Fact]
    public void Each_allocation_carries_its_own_batch_cost()
    {
        var r = FefoAllocator.Allocate(Sample(), 50m, Today);
        r.Allocations[0].CostPrice.Should().Be(210m);
        r.Allocations[1].CostPrice.Should().Be(205m);
    }
}
