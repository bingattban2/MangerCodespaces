-- 02.02 — Idempotent additive relational guards. NOT a V001 rewrite or data migration.
-- Deployed to an already-V001 encrypted DB inside InitializeAsync transaction after preflight.
-- IDs are immutable; historical snapshots are append-only in content. Closing ValidTo remains
-- legal for future effective-interval logic. Full authorized reset drops the managed DB.
CREATE INDEX IF NOT EXISTS IX_PersonAssignments_SourceMovement
    ON PersonAssignments(SourceMovementId) WHERE SourceMovementId IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_AttendanceDrafts_Site
    ON AttendanceDrafts(SiteId) WHERE SiteId IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_AttendanceDrafts_Status
    ON AttendanceDrafts(TargetStatusId);
CREATE INDEX IF NOT EXISTS IX_AttendanceRevisions_Person_Date
    ON AttendanceRevisions(PersonId, TargetLocalDate, ExecutedAtUtc);
CREATE INDEX IF NOT EXISTS IX_UserRoleHistory_User_Date
    ON UserRoleHistory(UserId, ChangedAtUtc);
CREATE INDEX IF NOT EXISTS IX_AttendanceStatusNameHistory_Status_Date
    ON AttendanceStatusNameHistory(StatusId, ValidFromLocalDate, ValidToLocalDate);
CREATE INDEX IF NOT EXISTS IX_Users_LinkedPerson
    ON Users(LinkedPersonId) WHERE LinkedPersonId IS NOT NULL;

-- Do not rewrite parent technical identity even without child references.
CREATE TRIGGER IF NOT EXISTS TR_0202_PersonId_Immutable
BEFORE UPDATE OF PersonId ON Persons
WHEN NEW.PersonId <> OLD.PersonId
BEGIN SELECT RAISE(ABORT, 'Immutable PersonId'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_SiteId_Immutable
BEFORE UPDATE OF SiteId ON Sites
WHEN NEW.SiteId <> OLD.SiteId
BEGIN SELECT RAISE(ABORT, 'Immutable SiteId'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_StatusId_Immutable
BEFORE UPDATE OF StatusId ON AttendanceStatusDefinitions
WHEN NEW.StatusId <> OLD.StatusId
BEGIN SELECT RAISE(ABORT, 'Immutable StatusId'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_AssignmentId_Immutable
BEFORE UPDATE OF AssignmentId ON PersonAssignments
WHEN NEW.AssignmentId <> OLD.AssignmentId
BEGIN SELECT RAISE(ABORT, 'Immutable AssignmentId'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_MovementId_Immutable
BEFORE UPDATE OF MovementId ON PersonnelMovements
WHEN NEW.MovementId <> OLD.MovementId
BEGIN SELECT RAISE(ABORT, 'Immutable MovementId'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_RecordId_Immutable
BEFORE UPDATE OF RecordId, PersonId, AttendanceLocalDate ON AttendanceRecords
WHEN NEW.RecordId <> OLD.RecordId OR NEW.PersonId <> OLD.PersonId
    OR NEW.AttendanceLocalDate <> OLD.AttendanceLocalDate
BEGIN SELECT RAISE(ABORT, 'Immutable attendance identity'); END;

-- Once committed, an historical record may be retained or bounded, never deleted or relabeled.
CREATE TRIGGER IF NOT EXISTS TR_0202_IdentityHistory_NoDelete
BEFORE DELETE ON PersonIdentityHistory
BEGIN SELECT RAISE(ABORT, 'Identity history cannot be deleted'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_IdentityHistory_ImmutableSnapshots
BEFORE UPDATE OF HistoryId, PersonId, NameSnapshot, ServiceNumberASnapshot,
                 ValidFromLocalDate, ChangedByUserId, RecordedAtUtc, Reason
ON PersonIdentityHistory
BEGIN SELECT RAISE(ABORT, 'Identity snapshot immutable'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_SiteHistory_NoDelete
BEFORE DELETE ON SiteNameHistory
BEGIN SELECT RAISE(ABORT, 'Site name history cannot be deleted'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_SiteHistory_ImmutableSnapshots
BEFORE UPDATE OF HistoryId, SiteId, VisibleNameSnapshot, ValidFromLocalDate,
                 ChangedByUserId, RecordedAtUtc ON SiteNameHistory
BEGIN SELECT RAISE(ABORT, 'Site snapshot immutable'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_StatusHistory_NoDelete
BEFORE DELETE ON AttendanceStatusNameHistory
BEGIN SELECT RAISE(ABORT, 'Status name history cannot be deleted'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_StatusHistory_ImmutableSnapshots
BEFORE UPDATE OF HistoryId, StatusId, VisibleNameSnapshot, ShortSymbolSnapshot,
                 ColorSnapshot, ValidFromLocalDate, RecordedAtUtc
ON AttendanceStatusNameHistory
BEGIN SELECT RAISE(ABORT, 'Status snapshot immutable'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_Attendance_NoDelete
BEFORE DELETE ON AttendanceRecords
BEGIN SELECT RAISE(ABORT, 'Committed daily attendance cannot be deleted'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_AttendanceRevisions_Immutable
BEFORE UPDATE ON AttendanceRevisions
BEGIN SELECT RAISE(ABORT, 'Attendance revision immutable'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_AttendanceRevisions_NoDelete
BEFORE DELETE ON AttendanceRevisions
BEGIN SELECT RAISE(ABORT, 'Attendance revision cannot be deleted'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_Movements_Immutable
BEFORE UPDATE ON PersonnelMovements
BEGIN SELECT RAISE(ABORT, 'Personnel movement immutable'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_Movements_NoDelete
BEFORE DELETE ON PersonnelMovements
BEGIN SELECT RAISE(ABORT, 'Personnel movement cannot be deleted'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_Audit_Immutable
BEFORE UPDATE ON AuditEvents
BEGIN SELECT RAISE(ABORT, 'Original audit event immutable'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_Audit_NoDelete
BEFORE DELETE ON AuditEvents
BEGIN SELECT RAISE(ABORT, 'Original audit event cannot be deleted'); END;

-- Link optional movement reference to the existing immutable MovementId, and verify
-- the movement belongs to the SAME PersonId as the assignment (not a label).
CREATE TRIGGER IF NOT EXISTS TR_0202_AssignmentMovement_Insert
BEFORE INSERT ON PersonAssignments
WHEN NEW.SourceMovementId IS NOT NULL AND NOT EXISTS (
  SELECT 1 FROM PersonnelMovements m
  WHERE m.MovementId = NEW.SourceMovementId AND m.PersonId = NEW.PersonId)
BEGIN SELECT RAISE(ABORT, 'Unknown or mismatched SourceMovementId'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_AssignmentMovement_Update
BEFORE UPDATE OF PersonId, SourceMovementId ON PersonAssignments
WHEN NEW.SourceMovementId IS NOT NULL AND NOT EXISTS (
  SELECT 1 FROM PersonnelMovements m
  WHERE m.MovementId = NEW.SourceMovementId AND m.PersonId = NEW.PersonId)
BEGIN SELECT RAISE(ABORT, 'Unknown or mismatched SourceMovementId'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_AssignmentPerson_Immutable
BEFORE UPDATE OF PersonId ON PersonAssignments
WHEN NEW.PersonId <> OLD.PersonId
BEGIN SELECT RAISE(ABORT, 'Immutable assignment person'); END;

-- Drafts are mutable, but their relational references must exist.
CREATE TRIGGER IF NOT EXISTS TR_0202_DraftRefs_Insert
BEFORE INSERT ON AttendanceDrafts
WHEN NOT EXISTS (SELECT 1 FROM AttendanceStatusDefinitions WHERE StatusId=NEW.TargetStatusId)
  OR (NEW.SiteId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM Sites WHERE SiteId=NEW.SiteId))
BEGIN SELECT RAISE(ABORT, 'Unknown draft site or status identifier'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_DraftRefs_Update
BEFORE UPDATE OF SiteId, TargetStatusId ON AttendanceDrafts
WHEN NOT EXISTS (SELECT 1 FROM AttendanceStatusDefinitions WHERE StatusId=NEW.TargetStatusId)
  OR (NEW.SiteId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM Sites WHERE SiteId=NEW.SiteId))
BEGIN SELECT RAISE(ABORT, 'Unknown draft site or status identifier'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_DraftStatus_RestrictDelete
BEFORE DELETE ON AttendanceStatusDefinitions
WHEN EXISTS(SELECT 1 FROM AttendanceDrafts WHERE TargetStatusId=OLD.StatusId)
BEGIN SELECT RAISE(ABORT, 'Status referenced by draft'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_DraftSite_RestrictDelete
BEFORE DELETE ON Sites
WHEN EXISTS(SELECT 1 FROM AttendanceDrafts WHERE SiteId=OLD.SiteId)
BEGIN SELECT RAISE(ABORT, 'Site referenced by draft'); END;

-- The retained AttendanceRecords already has FK-to-Assignment and FK-to-Site.
-- Enforce that the three immutable references describe the SAME person's assignment.
CREATE TRIGGER IF NOT EXISTS TR_0202_AttendanceAssignment_Insert
BEFORE INSERT ON AttendanceRecords
WHEN NOT EXISTS (
 SELECT 1 FROM PersonAssignments p
 WHERE p.AssignmentId=NEW.AssignmentIdAtEffectiveDate
   AND p.PersonId=NEW.PersonId AND p.SiteId=NEW.SiteIdAtEffectiveDate)
BEGIN SELECT RAISE(ABORT, 'Attendance assignment/person/site mismatch'); END;
CREATE TRIGGER IF NOT EXISTS TR_0202_AttendanceAssignment_Update
BEFORE UPDATE OF AssignmentIdAtEffectiveDate, PersonId, SiteIdAtEffectiveDate ON AttendanceRecords
WHEN NOT EXISTS (
 SELECT 1 FROM PersonAssignments p
 WHERE p.AssignmentId=NEW.AssignmentIdAtEffectiveDate
   AND p.PersonId=NEW.PersonId AND p.SiteId=NEW.SiteIdAtEffectiveDate)
BEGIN SELECT RAISE(ABORT, 'Attendance assignment/person/site mismatch'); END;
