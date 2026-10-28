using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.Tests;

public class CostingTests
{
    [Theory]
    [InlineData(1.12, 0.05, RoundingMode.Nearest, 1.10)]
    [InlineData(1.13, 0.05, RoundingMode.Nearest, 1.15)]
    [InlineData(1.125, 0.05, RoundingMode.Nearest, 1.15)]
    [InlineData(1.11, 0.05, RoundingMode.Up, 1.15)]
    [InlineData(1.15, 0.05, RoundingMode.Up, 1.15)]
    [InlineData(1.14, 0.05, RoundingMode.Down, 1.10)]
    [InlineData(7.3, 0.5, RoundingMode.Nearest, 7.5)]
    [InlineData(7.3, 0, RoundingMode.Nearest, 7.3)]
    public void RoundPrice(double value, double step, RoundingMode mode, double expected) =>
        Assert.Equal((decimal)expected, Costing.RoundPrice((decimal)value, (decimal)step, mode));

    [Fact]
    public void SourceCost_DiscountThenShippingThenQuantity()
    {
        var s = new ItemSource { PurchasePrice = 100, PurchaseQty = 10, SupplierId = 1 };
        var sup = new Supplier { Id = 1, DiscountPct = 10, ShippingPct = 14 };
        var c = Costing.ComputeSource(s, sup, 1.1m);
        Assert.Equal(90m, c.Net);
        Assert.Equal(12.6m, c.Shipping);
        Assert.Equal(10.26m, c.UnitCost);
    }

    [Fact]
    public void SourceCost_ItemOverridesSupplierDefaults()
    {
        var s = new ItemSource { PurchasePrice = 100, PurchaseQty = 1, SupplierId = 1, DiscountPct = 0, ShippingPct = 5 };
        var c = Costing.ComputeSource(s, new Supplier { Id = 1, DiscountPct = 10, ShippingPct = 14 }, 1.1m);
        Assert.Equal(105m, c.UnitCost);
    }

    [Fact]
    public void Dollars_AreConvertedFirst_AndFollowSupplier()
    {
        var sup = new Supplier { Id = 1, Currency = Currency.Usd, DiscountPct = 50 };
        var c = Costing.ComputeSource(new ItemSource { PurchasePrice = 110, SupplierId = 1 }, sup, 1.1m);
        Assert.Equal(Currency.Usd, c.Currency);
        Assert.Equal(100m, c.ListPriceEur);
        Assert.Equal(50m, c.UnitCost);
        // explicit euro on the item wins over the supplier's dollars
        var e = Costing.ComputeSource(new ItemSource { PurchasePrice = 110, SupplierId = 1, Currency = Currency.Eur }, sup, 1.1m);
        Assert.Equal(55m, e.UnitCost);
    }

    [Fact]
    public void WeightedSuppliers()
    {
        var a = new Supplier { Id = 1 };
        var b = new Supplier { Id = 2 };
        var sources = new List<ItemSource>
        {
            new() { SupplierId = 1, PurchasePrice = 10, Weight = 2 },
            new() { SupplierId = 2, PurchasePrice = 20, Weight = 3 },
        };
        var bc = Costing.ComputeBought(sources, id => id == 1 ? a : b, 1.1m);
        Assert.Equal(0.4m, bc.Sources[0].Share);
        Assert.Equal(16m, bc.UnitCost); // 40% × 10 + 60% × 20
    }

    [Fact]
    public void UnitConversion_ThroughPieces()
    {
        var m = new MaterialInfo { Unit = QtyUnit.Pcs, Cost = 10, PieceLengthCm = 200, PieceAreaCm2 = 5000, PieceWeightKg = 2 };
        Assert.Equal(0.05m, Costing.CostPerUnit(m, QtyUnit.Cm));
        Assert.Equal(0.002m, Costing.CostPerUnit(m, QtyUnit.Cm2));
        Assert.Equal(5m, Costing.CostPerUnit(m, QtyUnit.Kg));
        var cm = new MaterialInfo { Unit = QtyUnit.Cm, Cost = 0.05m, PieceLengthCm = 200 };
        Assert.Equal(10m, Costing.CostPerUnit(cm, QtyUnit.Pcs));
        Assert.Null(Costing.CostPerUnit(cm, QtyUnit.Kg));
    }

    [Fact]
    public void ProductCost_MaterialsLaborOverhead_WithWarnings()
    {
        var mats = new Dictionary<long, MaterialInfo>
        {
            [1] = new() { Id = 1, Unit = QtyUnit.Cm, Cost = 0.1m },
        };
        var lines = new[]
        {
            new RecipeLine { MaterialId = 1, Quantity = 30, Unit = QtyUnit.Cm },
            new RecipeLine { MaterialId = 1, Quantity = 1, Unit = QtyUnit.Kg },
            new RecipeLine { MaterialId = 99, Quantity = 1 },
        };
        var pc = Costing.ComputeProduct(lines, id => mats.GetValueOrDefault(id), 1800, 20, 10);
        Assert.Equal(3m, pc.Materials);
        Assert.Equal(10m, pc.Labor);
        Assert.Equal(5m, pc.Overhead);
        Assert.Equal(18m, pc.Total);
        Assert.True(pc.Warnings.HasFlag(CostWarning.NoConversion));
        Assert.True(pc.Warnings.HasFlag(CostWarning.MissingMaterial));
    }

    [Theory]
    [InlineData(4.37, 10.00, 0.05, RoundingMode.Nearest)]
    [InlineData(3.33, 7.15, 0.05, RoundingMode.Up)]
    [InlineData(12.9, 25.5, 0.5, RoundingMode.Down)]
    public void MarkupForPrice_HitsTarget(double cost, double target, double step, RoundingMode mode)
    {
        var m = Costing.MarkupForPrice((decimal)cost, (decimal)target, (decimal)step, mode);
        Assert.NotNull(m);
        Assert.Equal((decimal)target, Costing.Price((decimal)cost, m!.Value, (decimal)step, mode).Price);
    }

    [Fact]
    public void Normalize_FoldsGreekAccentsAndCase()
    {
        Assert.Equal(TextNorm.Normalize("ΜΠΛΕ Ύφασμα"), TextNorm.Normalize("μπλε υφασμα"));
        Assert.Equal("σοσ", TextNorm.Normalize("Σός"));
    }

    [Fact]
    public void NaturalSort()
    {
        var list = new List<string> { "A10", "a2", "A1", "B" };
        list.Sort(NaturalComparer.Instance);
        Assert.Equal(["A1", "a2", "A10", "B"], list);
    }

    [Fact]
    public void Duration() => Assert.Equal("2′ 05″", Costing.FormatDuration(125));
}
