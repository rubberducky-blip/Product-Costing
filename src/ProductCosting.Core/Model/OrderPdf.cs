using System.Globalization;

namespace ProductCosting.Core.Model;

/// <summary>Columns of an order PDF.</summary>
[Flags]
public enum OrderPdfColumns
{
    None = 0,
    Number = 1,
    SupplierCode = 2,
    OurCode = 4,
    Name = 8,
    Variant = 16,
    Quantity = 32,
    UnitPrice = 64,
    Value = 128,
    Default = Number | SupplierCode | OurCode | Name | Variant | Quantity,
}

/// <summary>What the top of an order PDF shows: the supplier's details, ours (with the logo), both or neither.</summary>
public enum OrderPdfHeader { Supplier = 0, Company = 1, Both = 2, None = 3 }

/// <summary>How order PDFs come out. Set in Settings; a supplier can have its own, used for its orders.</summary>
public sealed record OrderPdfOptions(OrderPdfColumns Columns, string Language, int FontSize, OrderPdfHeader Header)
{
    public static OrderPdfOptions Default { get; } = new(OrderPdfColumns.Default, "el", 9, OrderPdfHeader.Supplier);

    public static int[] FontSizes { get; } = [8, 9, 10, 11, 12];

    /// <summary>"cols=63;lang=el;font=9;header=0" (stored in the settings and on suppliers).</summary>
    public string Serialize() => string.Create(CultureInfo.InvariantCulture,
        $"cols={(int)Columns};lang={Language};font={FontSize};header={(int)Header}");

    /// <summary>Null for an empty value (a supplier without its own options).</summary>
    public static OrderPdfOptions? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var o = Default;
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var v = kv[1].Trim();
            int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n);
            o = kv[0].Trim() switch
            {
                "cols" => o with { Columns = (OrderPdfColumns)n },
                "lang" => o with { Language = v == "en" ? "en" : "el" },
                "font" => o with { FontSize = Math.Clamp(n, 7, 14) },
                "header" => o with { Header = Enum.IsDefined((OrderPdfHeader)n) ? (OrderPdfHeader)n : OrderPdfHeader.Supplier },
                _ => o,
            };
        }
        return o;
    }
}

/// <summary>Our company, for the top of order PDFs.</summary>
/// <remarks>English PDFs use <see cref="NameEn"/> and <see cref="AddressEn"/> (when filled in) and leave out the tax office.</remarks>
public sealed record CompanyInfo(string Name, string Address, string VatNumber, string TaxOffice, string Phone, string Email,
    string NameEn = "", string AddressEn = "")
{
    public static CompanyInfo Empty { get; } = new("", "", "", "", "", "");
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name + Address + VatNumber + TaxOffice + Phone + Email + NameEn + AddressEn);

    /// <summary>The details as an order PDF in that language shows them.</summary>
    public CompanyInfo For(string lang) => lang != "en" ? this : this with
    {
        Name = string.IsNullOrWhiteSpace(NameEn) ? Name : NameEn,
        Address = string.IsNullOrWhiteSpace(AddressEn) ? Address : AddressEn,
        TaxOffice = "",
    };
}
