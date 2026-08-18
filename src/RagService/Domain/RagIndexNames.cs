using RagService.Contracts;

namespace RagService.Domain;

public static class RagIndexNames
{
    public const string Salon = "kb-salon";
    public const string Marketing = "kb-marketing";

    public static string For(RagDomain domain) => domain switch
    {
        RagDomain.Salon => Salon,
        RagDomain.Marketing => Marketing,
        _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, "Only salon|marketing indexes are allowed.")
    };

    public static bool TryParseDomain(string? value, out RagDomain domain)
    {
        domain = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out domain)
               && (domain == RagDomain.Salon || domain == RagDomain.Marketing);
    }
}
