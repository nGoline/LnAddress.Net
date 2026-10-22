namespace LnAddress.Net.Helpers;

/// <summary>
/// Labels for the invoices LnAddress creates on Core Lightning. CLN requires a unique label per invoice; the label
/// also keeps the LUD-12 comment, since the description must stay the LNURL metadata.
/// </summary>
public static class ClnInvoiceLabel
{
    // Tells LnAddress invoices apart from other invoices on the node. The recommended clnrest rune only allows
    // creating invoices with this prefix
    public const string Prefix = "lnaddress-";

    public static string Create(string username, string? comment)
    {
        var label = $"{Prefix}{username}-{Guid.NewGuid():N}";
        return comment is null ? label : $"{label}: {comment}";
    }

    public static bool IsLnAddress(string? label) => label?.StartsWith(Prefix, StringComparison.Ordinal) ?? false;
}
