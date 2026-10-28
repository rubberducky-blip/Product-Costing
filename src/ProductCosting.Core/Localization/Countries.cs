namespace ProductCosting.Core.Localization;

/// <summary>Countries for the supplier form, stored by ISO code and shown in the app language.</summary>
public static class Countries
{
    public const string Default = "GR";

    public sealed record Country(string Code, string El, string En)
    {
        public string Name => Strings.Language == "en" ? En : El;
    }

    public static IReadOnlyList<Country> All { get; } =
    [
        new("GR", "Ελλάδα", "Greece"),
        new("CY", "Κύπρος", "Cyprus"),
        new("AL", "Αλβανία", "Albania"),
        new("DZ", "Αλγερία", "Algeria"),
        new("AR", "Αργεντινή", "Argentina"),
        new("AM", "Αρμενία", "Armenia"),
        new("AU", "Αυστραλία", "Australia"),
        new("AT", "Αυστρία", "Austria"),
        new("AZ", "Αζερμπαϊτζάν", "Azerbaijan"),
        new("BD", "Μπανγκλαντές", "Bangladesh"),
        new("BY", "Λευκορωσία", "Belarus"),
        new("BE", "Βέλγιο", "Belgium"),
        new("BA", "Βοσνία-Ερζεγοβίνη", "Bosnia and Herzegovina"),
        new("BR", "Βραζιλία", "Brazil"),
        new("BG", "Βουλγαρία", "Bulgaria"),
        new("KH", "Καμπότζη", "Cambodia"),
        new("CA", "Καναδάς", "Canada"),
        new("CL", "Χιλή", "Chile"),
        new("CN", "Κίνα", "China"),
        new("CO", "Κολομβία", "Colombia"),
        new("HR", "Κροατία", "Croatia"),
        new("CZ", "Τσεχία", "Czechia"),
        new("DK", "Δανία", "Denmark"),
        new("EG", "Αίγυπτος", "Egypt"),
        new("EE", "Εσθονία", "Estonia"),
        new("ET", "Αιθιοπία", "Ethiopia"),
        new("FI", "Φινλανδία", "Finland"),
        new("FR", "Γαλλία", "France"),
        new("GE", "Γεωργία", "Georgia"),
        new("DE", "Γερμανία", "Germany"),
        new("HK", "Χονγκ Κονγκ", "Hong Kong"),
        new("HU", "Ουγγαρία", "Hungary"),
        new("IS", "Ισλανδία", "Iceland"),
        new("IN", "Ινδία", "India"),
        new("ID", "Ινδονησία", "Indonesia"),
        new("IR", "Ιράν", "Iran"),
        new("IQ", "Ιράκ", "Iraq"),
        new("IE", "Ιρλανδία", "Ireland"),
        new("IL", "Ισραήλ", "Israel"),
        new("IT", "Ιταλία", "Italy"),
        new("JP", "Ιαπωνία", "Japan"),
        new("JO", "Ιορδανία", "Jordan"),
        new("KZ", "Καζακστάν", "Kazakhstan"),
        new("KE", "Κένυα", "Kenya"),
        new("XK", "Κοσσυφοπέδιο", "Kosovo"),
        new("KW", "Κουβέιτ", "Kuwait"),
        new("LV", "Λετονία", "Latvia"),
        new("LB", "Λίβανος", "Lebanon"),
        new("LY", "Λιβύη", "Libya"),
        new("LT", "Λιθουανία", "Lithuania"),
        new("LU", "Λουξεμβούργο", "Luxembourg"),
        new("MY", "Μαλαισία", "Malaysia"),
        new("MT", "Μάλτα", "Malta"),
        new("MX", "Μεξικό", "Mexico"),
        new("MD", "Μολδαβία", "Moldova"),
        new("MC", "Μονακό", "Monaco"),
        new("ME", "Μαυροβούνιο", "Montenegro"),
        new("MA", "Μαρόκο", "Morocco"),
        new("NL", "Ολλανδία", "Netherlands"),
        new("NZ", "Νέα Ζηλανδία", "New Zealand"),
        new("NG", "Νιγηρία", "Nigeria"),
        new("MK", "Βόρεια Μακεδονία", "North Macedonia"),
        new("NO", "Νορβηγία", "Norway"),
        new("PK", "Πακιστάν", "Pakistan"),
        new("PE", "Περού", "Peru"),
        new("PH", "Φιλιππίνες", "Philippines"),
        new("PL", "Πολωνία", "Poland"),
        new("PT", "Πορτογαλία", "Portugal"),
        new("QA", "Κατάρ", "Qatar"),
        new("RO", "Ρουμανία", "Romania"),
        new("RU", "Ρωσία", "Russia"),
        new("SA", "Σαουδική Αραβία", "Saudi Arabia"),
        new("RS", "Σερβία", "Serbia"),
        new("SG", "Σιγκαπούρη", "Singapore"),
        new("SK", "Σλοβακία", "Slovakia"),
        new("SI", "Σλοβενία", "Slovenia"),
        new("ZA", "Νότια Αφρική", "South Africa"),
        new("KR", "Νότια Κορέα", "South Korea"),
        new("ES", "Ισπανία", "Spain"),
        new("LK", "Σρι Λάνκα", "Sri Lanka"),
        new("SE", "Σουηδία", "Sweden"),
        new("CH", "Ελβετία", "Switzerland"),
        new("TW", "Ταϊβάν", "Taiwan"),
        new("TH", "Ταϊλάνδη", "Thailand"),
        new("TN", "Τυνησία", "Tunisia"),
        new("TR", "Τουρκία", "Türkiye"),
        new("UA", "Ουκρανία", "Ukraine"),
        new("AE", "Ηνωμένα Αραβικά Εμιράτα", "United Arab Emirates"),
        new("GB", "Ηνωμένο Βασίλειο", "United Kingdom"),
        new("US", "ΗΠΑ", "United States"),
        new("UZ", "Ουζμπεκιστάν", "Uzbekistan"),
        new("VN", "Βιετνάμ", "Vietnam"),
    ];

    static readonly Dictionary<string, Country> ByCode = All.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    public static Country? Find(string? code) => code is null ? null : ByCode.GetValueOrDefault(code);

    /// <summary>Name in the app language; text that isn't a known code is shown as it is.</summary>
    public static string Name(string? code) => string.IsNullOrWhiteSpace(code) ? "" : Find(code)?.Name ?? code;

    /// <summary>Both names, for search.</summary>
    public static string SearchText(string? code) => Find(code) is { } c ? $"{c.El} {c.En}" : code ?? "";
}
