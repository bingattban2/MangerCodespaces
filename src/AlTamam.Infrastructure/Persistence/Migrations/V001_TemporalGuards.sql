-- Phase 02 / Step 03: half-open [ValidFromLocalDate, ValidToLocalDate) business dates.
-- Additive to the original V001; no historical rows, dates or technical IDs rewritten.
-- NULL ValidToLocalDate denotes +infinity. Adjacent intervals sharing one boundary are legal.
-- Executed in the same atomic SQLCipher initialization transaction as 02.02 guards.
-- Existing invalid/overlapping V001 records MUST fail preflight without mutation.

CREATE TRIGGER IF NOT EXISTS TR_0203_identity_Interval_Insert
BEFORE INSERT ON PersonIdentityHistory
BEGIN
  SELECT RAISE(ABORT, 'Invalid identity half-open business dates')
  WHERE length(NEW.ValidFromLocalDate) <> 10
    OR NEW.ValidFromLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
    OR date(NEW.ValidFromLocalDate,'+0 days') IS NULL
    OR date(NEW.ValidFromLocalDate,'+0 days') <> NEW.ValidFromLocalDate
    OR (NEW.ValidToLocalDate IS NOT NULL AND
        (length(NEW.ValidToLocalDate) <> 10
         OR NEW.ValidToLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
         OR date(NEW.ValidToLocalDate,'+0 days') IS NULL
         OR date(NEW.ValidToLocalDate,'+0 days') <> NEW.ValidToLocalDate
         OR NEW.ValidToLocalDate <= NEW.ValidFromLocalDate));
  SELECT RAISE(ABORT, 'Overlapping identity effective dates')
  WHERE EXISTS (
    SELECT 1 FROM PersonIdentityHistory oldRow
    WHERE oldRow.PersonId=NEW.PersonId
      AND (oldRow.ValidToLocalDate IS NULL OR NEW.ValidFromLocalDate < oldRow.ValidToLocalDate)
      AND (NEW.ValidToLocalDate IS NULL OR oldRow.ValidFromLocalDate < NEW.ValidToLocalDate)
  );
END;

CREATE TRIGGER IF NOT EXISTS TR_0203_identity_Interval_Update
BEFORE UPDATE OF ValidFromLocalDate, ValidToLocalDate ON PersonIdentityHistory
BEGIN
  SELECT RAISE(ABORT, 'Invalid identity half-open business dates')
  WHERE length(NEW.ValidFromLocalDate) <> 10
    OR NEW.ValidFromLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
    OR date(NEW.ValidFromLocalDate,'+0 days') IS NULL
    OR date(NEW.ValidFromLocalDate,'+0 days') <> NEW.ValidFromLocalDate
    OR (NEW.ValidToLocalDate IS NOT NULL AND
        (length(NEW.ValidToLocalDate) <> 10
         OR NEW.ValidToLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
         OR date(NEW.ValidToLocalDate,'+0 days') IS NULL
         OR date(NEW.ValidToLocalDate,'+0 days') <> NEW.ValidToLocalDate
         OR NEW.ValidToLocalDate <= NEW.ValidFromLocalDate));
  SELECT RAISE(ABORT, 'Overlapping identity effective dates')
  WHERE EXISTS (
    SELECT 1 FROM PersonIdentityHistory oldRow
    WHERE oldRow.PersonId=NEW.PersonId AND oldRow.HistoryId <> OLD.HistoryId
      AND (oldRow.ValidToLocalDate IS NULL OR NEW.ValidFromLocalDate < oldRow.ValidToLocalDate)
      AND (NEW.ValidToLocalDate IS NULL OR oldRow.ValidFromLocalDate < NEW.ValidToLocalDate)
  );
END;

CREATE TRIGGER IF NOT EXISTS TR_0203_site_Interval_Insert
BEFORE INSERT ON SiteNameHistory
BEGIN
  SELECT RAISE(ABORT, 'Invalid site half-open business dates')
  WHERE length(NEW.ValidFromLocalDate) <> 10
    OR NEW.ValidFromLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
    OR date(NEW.ValidFromLocalDate,'+0 days') IS NULL
    OR date(NEW.ValidFromLocalDate,'+0 days') <> NEW.ValidFromLocalDate
    OR (NEW.ValidToLocalDate IS NOT NULL AND
        (length(NEW.ValidToLocalDate) <> 10
         OR NEW.ValidToLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
         OR date(NEW.ValidToLocalDate,'+0 days') IS NULL
         OR date(NEW.ValidToLocalDate,'+0 days') <> NEW.ValidToLocalDate
         OR NEW.ValidToLocalDate <= NEW.ValidFromLocalDate));
  SELECT RAISE(ABORT, 'Overlapping site effective dates')
  WHERE EXISTS (
    SELECT 1 FROM SiteNameHistory oldRow
    WHERE oldRow.SiteId=NEW.SiteId
      AND (oldRow.ValidToLocalDate IS NULL OR NEW.ValidFromLocalDate < oldRow.ValidToLocalDate)
      AND (NEW.ValidToLocalDate IS NULL OR oldRow.ValidFromLocalDate < NEW.ValidToLocalDate)
  );
END;

CREATE TRIGGER IF NOT EXISTS TR_0203_site_Interval_Update
BEFORE UPDATE OF ValidFromLocalDate, ValidToLocalDate ON SiteNameHistory
BEGIN
  SELECT RAISE(ABORT, 'Invalid site half-open business dates')
  WHERE length(NEW.ValidFromLocalDate) <> 10
    OR NEW.ValidFromLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
    OR date(NEW.ValidFromLocalDate,'+0 days') IS NULL
    OR date(NEW.ValidFromLocalDate,'+0 days') <> NEW.ValidFromLocalDate
    OR (NEW.ValidToLocalDate IS NOT NULL AND
        (length(NEW.ValidToLocalDate) <> 10
         OR NEW.ValidToLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
         OR date(NEW.ValidToLocalDate,'+0 days') IS NULL
         OR date(NEW.ValidToLocalDate,'+0 days') <> NEW.ValidToLocalDate
         OR NEW.ValidToLocalDate <= NEW.ValidFromLocalDate));
  SELECT RAISE(ABORT, 'Overlapping site effective dates')
  WHERE EXISTS (
    SELECT 1 FROM SiteNameHistory oldRow
    WHERE oldRow.SiteId=NEW.SiteId AND oldRow.HistoryId <> OLD.HistoryId
      AND (oldRow.ValidToLocalDate IS NULL OR NEW.ValidFromLocalDate < oldRow.ValidToLocalDate)
      AND (NEW.ValidToLocalDate IS NULL OR oldRow.ValidFromLocalDate < NEW.ValidToLocalDate)
  );
END;

CREATE TRIGGER IF NOT EXISTS TR_0203_status_Interval_Insert
BEFORE INSERT ON AttendanceStatusNameHistory
BEGIN
  SELECT RAISE(ABORT, 'Invalid status half-open business dates')
  WHERE length(NEW.ValidFromLocalDate) <> 10
    OR NEW.ValidFromLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
    OR date(NEW.ValidFromLocalDate,'+0 days') IS NULL
    OR date(NEW.ValidFromLocalDate,'+0 days') <> NEW.ValidFromLocalDate
    OR (NEW.ValidToLocalDate IS NOT NULL AND
        (length(NEW.ValidToLocalDate) <> 10
         OR NEW.ValidToLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
         OR date(NEW.ValidToLocalDate,'+0 days') IS NULL
         OR date(NEW.ValidToLocalDate,'+0 days') <> NEW.ValidToLocalDate
         OR NEW.ValidToLocalDate <= NEW.ValidFromLocalDate));
  SELECT RAISE(ABORT, 'Overlapping status effective dates')
  WHERE EXISTS (
    SELECT 1 FROM AttendanceStatusNameHistory oldRow
    WHERE oldRow.StatusId=NEW.StatusId
      AND (oldRow.ValidToLocalDate IS NULL OR NEW.ValidFromLocalDate < oldRow.ValidToLocalDate)
      AND (NEW.ValidToLocalDate IS NULL OR oldRow.ValidFromLocalDate < NEW.ValidToLocalDate)
  );
END;

CREATE TRIGGER IF NOT EXISTS TR_0203_status_Interval_Update
BEFORE UPDATE OF ValidFromLocalDate, ValidToLocalDate ON AttendanceStatusNameHistory
BEGIN
  SELECT RAISE(ABORT, 'Invalid status half-open business dates')
  WHERE length(NEW.ValidFromLocalDate) <> 10
    OR NEW.ValidFromLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
    OR date(NEW.ValidFromLocalDate,'+0 days') IS NULL
    OR date(NEW.ValidFromLocalDate,'+0 days') <> NEW.ValidFromLocalDate
    OR (NEW.ValidToLocalDate IS NOT NULL AND
        (length(NEW.ValidToLocalDate) <> 10
         OR NEW.ValidToLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
         OR date(NEW.ValidToLocalDate,'+0 days') IS NULL
         OR date(NEW.ValidToLocalDate,'+0 days') <> NEW.ValidToLocalDate
         OR NEW.ValidToLocalDate <= NEW.ValidFromLocalDate));
  SELECT RAISE(ABORT, 'Overlapping status effective dates')
  WHERE EXISTS (
    SELECT 1 FROM AttendanceStatusNameHistory oldRow
    WHERE oldRow.StatusId=NEW.StatusId AND oldRow.HistoryId <> OLD.HistoryId
      AND (oldRow.ValidToLocalDate IS NULL OR NEW.ValidFromLocalDate < oldRow.ValidToLocalDate)
      AND (NEW.ValidToLocalDate IS NULL OR oldRow.ValidFromLocalDate < NEW.ValidToLocalDate)
  );
END;

CREATE TRIGGER IF NOT EXISTS TR_0203_assignment_Interval_Insert
BEFORE INSERT ON PersonAssignments
BEGIN
  SELECT RAISE(ABORT, 'Invalid assignment half-open business dates')
  WHERE length(NEW.ValidFromLocalDate) <> 10
    OR NEW.ValidFromLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
    OR date(NEW.ValidFromLocalDate,'+0 days') IS NULL
    OR date(NEW.ValidFromLocalDate,'+0 days') <> NEW.ValidFromLocalDate
    OR (NEW.ValidToLocalDate IS NOT NULL AND
        (length(NEW.ValidToLocalDate) <> 10
         OR NEW.ValidToLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
         OR date(NEW.ValidToLocalDate,'+0 days') IS NULL
         OR date(NEW.ValidToLocalDate,'+0 days') <> NEW.ValidToLocalDate
         OR NEW.ValidToLocalDate <= NEW.ValidFromLocalDate));
  SELECT RAISE(ABORT, 'Overlapping assignment effective dates')
  WHERE EXISTS (
    SELECT 1 FROM PersonAssignments oldRow
    WHERE oldRow.PersonId=NEW.PersonId
      AND (oldRow.ValidToLocalDate IS NULL OR NEW.ValidFromLocalDate < oldRow.ValidToLocalDate)
      AND (NEW.ValidToLocalDate IS NULL OR oldRow.ValidFromLocalDate < NEW.ValidToLocalDate)
  );
END;

CREATE TRIGGER IF NOT EXISTS TR_0203_assignment_Interval_Update
BEFORE UPDATE OF ValidFromLocalDate, ValidToLocalDate ON PersonAssignments
BEGIN
  SELECT RAISE(ABORT, 'Invalid assignment half-open business dates')
  WHERE length(NEW.ValidFromLocalDate) <> 10
    OR NEW.ValidFromLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
    OR date(NEW.ValidFromLocalDate,'+0 days') IS NULL
    OR date(NEW.ValidFromLocalDate,'+0 days') <> NEW.ValidFromLocalDate
    OR (NEW.ValidToLocalDate IS NOT NULL AND
        (length(NEW.ValidToLocalDate) <> 10
         OR NEW.ValidToLocalDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
         OR date(NEW.ValidToLocalDate,'+0 days') IS NULL
         OR date(NEW.ValidToLocalDate,'+0 days') <> NEW.ValidToLocalDate
         OR NEW.ValidToLocalDate <= NEW.ValidFromLocalDate));
  SELECT RAISE(ABORT, 'Overlapping assignment effective dates')
  WHERE EXISTS (
    SELECT 1 FROM PersonAssignments oldRow
    WHERE oldRow.PersonId=NEW.PersonId AND oldRow.AssignmentId <> OLD.AssignmentId
      AND (oldRow.ValidToLocalDate IS NULL OR NEW.ValidFromLocalDate < oldRow.ValidToLocalDate)
      AND (NEW.ValidToLocalDate IS NULL OR oldRow.ValidFromLocalDate < NEW.ValidToLocalDate)
  );
END;
