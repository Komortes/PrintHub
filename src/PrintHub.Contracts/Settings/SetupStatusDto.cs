using PrintHub.Contracts.Printers;

namespace PrintHub.Contracts.Settings;

public sealed record SetupStatusDto(
    bool IsOnboardingRequired,
    bool HasApiKey,
    bool HasDefaultPrinter,
    IReadOnlyCollection<PrinterDto> Printers);
