namespace AlTamam.Contracts.Dtos;

public sealed record MovementChange(IReadOnlyList<string> PersonIds, string SourceSiteId,
    string EffectiveDate, int Type, string? DestinationSiteId = null, string DestinationName = "", string Reason = "");
public sealed record MovementPerson(string PersonId, string Name, string ServiceNumberA, long Version,
    string AssignmentId, string AssignmentFrom, string SourceSiteName);
public sealed record MovementPreview(MovementChange Change, IReadOnlyList<MovementPerson> Persons,
    string DestinationLabel, string PreviewHash, string ConfirmationToken, int ConfirmationDelaySeconds);
public sealed record CommitMovement(MovementChange Change, string PreviewHash, string ConfirmationToken);
public sealed record MovementResult(IReadOnlyList<string> MovementIds, int Count, string EffectiveDate, int Type);
public sealed record MovementArchiveQuery(string Search = "", int? Type = null, string? SiteId = null,
    string? FromDate = null, string? ToDate = null, string? PersonId = null, int Offset = 0, int Limit = 50);
public sealed record MovementArchiveRow(string MovementId, string PersonId, string PersonName,
    string ServiceNumberA, int Type, string SourceSiteId, string SourceSiteName, string? DestinationSiteId,
    string DestinationName, string Reason, string EffectiveDate, string CommittedAtUtc, string ActorUserId);
public sealed record MovementArchivePage(IReadOnlyList<MovementArchiveRow> Items, long Total);
public sealed record MovementKey(string MovementId);
