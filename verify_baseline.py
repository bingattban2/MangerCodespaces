#!/usr/bin/env python3
"""NON-PRODUCTION VERIFICATION. Validates a canonical ZIP + independent SQLite fixture.
Does NOT exercise SQLCipher native encryption, C# business services or Windows UI.
No production repository or user database is changed. Python 3.10+ standard library only.
"""
import argparse,csv,hashlib,json,os,re,sqlite3,tempfile,zipfile,datetime
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--source',required=True,help='MangerCodespaces-main.zip (read only)')
parser.add_argument('--trace',required=True,help='AlTamam_Phase00_Step01_Traceability.csv (read only)')
parser.add_argument('--output',required=True,help='External JSON results')
args=parser.parse_args()
source=Path(args.source).resolve(); trace=Path(args.trace).resolve(); output=Path(args.output).resolve()
sha=lambda x:hashlib.sha256(Path(x).read_bytes()).hexdigest()
src_sha=sha(source); trace_sha=sha(trace)
checks=[]; gaps=[]
def check(name,condition,actual=None):
    checks.append({'name':name,'result':'PASS' if condition else 'FAIL','observation':actual})
def gap(name,observed,implication,phase):
    gaps.append({'name':name,'observed':observed,'implication':implication,'future_owning_phase':phase})
def reject(conn,sql,params=(),expect='IntegrityError'):
    try:
        with conn:
            conn.execute(sql,params)
    except sqlite3.IntegrityError:
        return expect=='IntegrityError'
    return False
with tempfile.TemporaryDirectory(prefix='altamam_phase00_v2_') as td:
    temp=Path(td)
    with zipfile.ZipFile(source) as z:
        # Safety: no traversal/symlinks (trusted source archive still validated).
        for e in z.infolist():
            dest=(temp/e.filename).resolve()
            if not dest.is_relative_to(temp): raise ValueError('Zip path traversal')
            if (e.external_attr >> 16)&0o170000==0o120000: raise ValueError('Archive symlink')
        z.extractall(temp)
    project=temp/'MangerCodespaces-main'
    ui=project/'src/AlTamam.WebUI'
    manifest=json.loads((ui/'screen-manifest.json').read_text(encoding='utf-8'))
    screens=manifest['screens']; screen_keys={x['routeKey'] for x in screens}
    check('Screen manifest has exactly 69 unique keys',len(screens)==69 and len(screen_keys)==69, len(screens))
    check('All canonical paths exist',all((ui/x['localAssetPath']).is_file() for x in screens))
    check('All 69 route keys use PascalCase',all(re.fullmatch(r'[A-Z][a-zA-Z0-9]*',x['routeKey']) for x in screens))
    check('Original WebUI entry point preserved',(ui/'index.html').is_file())
    inline=[]
    for page in [ui/'index.html']+[ui/x['localAssetPath'] for x in screens]:
        html=page.read_text(encoding='utf-8-sig')
        if re.search(r'<script\b[^>]*>(?!\s*</script>)\s*[^\s<]',html,re.I|re.S): inline.append((str(page.relative_to(ui)),'script body'))
        if re.search(r'\s(onclick|onchange|onload|onsubmit|oninput)\s*=',html,re.I): inline.append((str(page.relative_to(ui)),'inline handler'))
        if re.search(r'\sstyle\s*=',html,re.I): inline.append((str(page.relative_to(ui)),'inline style'))
    check('70 HTML pages contain no inline event handler/style/script',not inline,inline[:6])
    with trace.open(newline='',encoding='utf-8-sig') as f: rows=list(csv.DictReader(f))
    ids=[r['ID'] for r in rows]
    check('60 unique granular requirements in external trace',len(rows)==60 and len(set(ids))==60,len(ids))
    check('All trace routes resolve',all(set(filter(None,r['canonical_screen_route_keys'].split(','))).issubset(screen_keys) for r in rows))
    check('All 60 trace items have planned verification',all(r['external_verification_future'].strip() for r in rows))
    check('All 60 trace items identify planned services and phase',all(r['planned_service'] and r['owning_phase'].isdigit() for r in rows))
    project_files=list((project/'src').glob('AlTamam.*/*.csproj'))
    check('Four production .NET project files',len(project_files)==4,len(project_files))
    check('All four projects target net10.0',all('<TargetFramework>net10.0</TargetFramework>' in p.read_text() for p in project_files))
    check('No desktop host project yet (Phase 04)',not (project/'src/AlTamam.Desktop').exists())
    schema=(project/'src/AlTamam.Infrastructure/Persistence/Migrations/V001_InitialSchema.sql').read_text()
    dbfile=temp/'disposable_fixture.sqlite'; conn=sqlite3.connect(dbfile)
    conn.execute('PRAGMA foreign_keys=ON')
    conn.executescript(schema)
    objs={name for name, in conn.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")}
    check('V001 creates 26 actual SQLite tables',len(objs)==26,len(objs))
    check('V001 initializes empty Users',conn.execute('SELECT COUNT(*) FROM Users').fetchone()[0]==0)
    check('V001 initializes empty Persons',conn.execute('SELECT COUNT(*) FROM Persons').fetchone()[0]==0)
    check('V001 seeds protected default site as metadata',conn.execute("SELECT COUNT(*) FROM Sites WHERE SiteId='SITE-UNSPECIFIED' AND IsSystemSite=1 AND VisibleName='غير محدد'").fetchone()[0]==1)
    check('V001 seeds 4 system statuses',conn.execute('SELECT COUNT(*) FROM AttendanceStatusDefinitions WHERE IsSystemSeeded=1').fetchone()[0]==4)
    check('V001 seeds 4 protected columns',conn.execute('SELECT COUNT(*) FROM CustomFieldDefinitions WHERE IsSystemProtected=1').fetchone()[0]==4)
    check('Default backup is scheduled 16:00',conn.execute("SELECT SettingValue FROM AppSettings WHERE SettingKey='AutoBackupTimeLocal'").fetchone()[0]=='16:00')
    # Actual persistent SQL rows, entirely fictional identifiers in throwaway DB.
    with conn:
        conn.execute("INSERT INTO Persons(PersonId,CurrentVisibleName,NormalizedName,CurrentServiceNumberA,NormalizedServiceNumberA,FirstRecordedLocalDate,FirstRecordedAtUtc) VALUES(?,?,?,?,?,?,?)",('P-TEST-01','فرد اختبار','فرد اختبار','T001','T001','2026-01-01','2026-01-01T00:00:00Z'))
        conn.execute("INSERT INTO PersonAssignments(AssignmentId,PersonId,SiteId,ValidFromLocalDate,ValidToLocalDate,CreatedAtUtc) VALUES (?,?,?,?,?,?)",('A-TEST-01','P-TEST-01','SITE-UNSPECIFIED','2026-01-01','2026-02-01','2026-01-01T00:00:00Z'))
    conn.close();conn=sqlite3.connect(dbfile);conn.execute('PRAGMA foreign_keys=ON')
    check('Real person and assignment survive database reopen',conn.execute('SELECT COUNT(*) FROM Persons').fetchone()[0]==1 and conn.execute('SELECT COUNT(*) FROM PersonAssignments').fetchone()[0]==1)
    add_assign="INSERT INTO PersonAssignments(AssignmentId,PersonId,SiteId,ValidFromLocalDate,ValidToLocalDate,CreatedAtUtc) VALUES (?,?,?,?,?,?)"
    check('Reject overlapping assignment via SQL trigger',reject(conn,add_assign,('A-OVERLAP','P-TEST-01','SITE-UNSPECIFIED','2026-01-15',None,'2026-01-15T00:00:00Z')) and conn.execute('SELECT COUNT(*) FROM PersonAssignments').fetchone()[0]==1)
    check('Reject invalid assignment dates',reject(conn,add_assign,('A-INVALID','P-TEST-01','SITE-UNSPECIFIED','2026-02-03','2026-02-01','2026-02-01T00:00:00Z')))
    check('Reject unknown site with foreign key enabled',reject(conn,add_assign,('A-UNKNOWN','P-TEST-01','DOES-NOT-EXIST','2026-03-01',None,'2026-03-01T00:00:00Z')))
    person_insert="INSERT INTO Persons(PersonId,CurrentVisibleName,NormalizedName,CurrentServiceNumberA,NormalizedServiceNumberA,FirstRecordedLocalDate,FirstRecordedAtUtc) VALUES (?,?,?,?,?,?,?)"
    check('Reject duplicate active service number',reject(conn,person_insert,('P-TEST-DUP','فرد اختبار جديد','فرد اختبار جديد','T001','T001','2026-01-01','2026-01-01T00:00:00Z')))
    with conn:
        conn.execute(add_assign,('A-ADJACENT','P-TEST-01','SITE-UNSPECIFIED','2026-02-01','2026-03-01','2026-02-01T00:00:00Z'))
    check('Half-open contiguous assignments permitted',conn.execute('SELECT COUNT(*) FROM PersonAssignments').fetchone()[0]==2)
    check('Overlapping assignment UPDATE rejected',reject(conn,"UPDATE PersonAssignments SET ValidFromLocalDate='2026-01-31' WHERE AssignmentId='A-ADJACENT'"))
    check('Assignments unchanged after rejected UPDATE',conn.execute("SELECT ValidFromLocalDate FROM PersonAssignments WHERE AssignmentId='A-ADJACENT'").fetchone()[0]=='2026-02-01')
    # Transaction rollback; re-open is required to prove durable absence.
    try:
        conn.execute('BEGIN')
        conn.execute(person_insert,('P-ROLLBACK','فرد للرجوع','فرد للرجوع','T002','T002','2026-01-01','2026-01-01T00:00:00Z'))
        conn.execute(person_insert,('P-ROLLBACK2','فرد ثاني','فرد ثاني','T001','T001','2026-01-01','2026-01-01T00:00:00Z'))
        conn.commit()
    except sqlite3.IntegrityError: conn.rollback()
    conn.close();conn=sqlite3.connect(dbfile);conn.execute('PRAGMA foreign_keys=ON')
    check('Multi-insert rollback persisted after restart',conn.execute("SELECT COUNT(*) FROM Persons WHERE PersonId LIKE 'P-ROLLBACK%'").fetchone()[0]==0)
    # Check idempotency primitives on real schema, NOT full C# action replay guarantee.
    with conn:
        conn.execute("INSERT INTO AttendanceRecords(PersonId,AttendanceLocalDate,StatusId,StatusNameSnapshot,StatusSymbolSnapshot,StatusColorSnapshot,AssignmentIdAtEffectiveDate,SiteIdAtEffectiveDate,SiteNameSnapshot,Source,CreatedByUserId,CommittedAtUtc,IdempotencyKey) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",('P-TEST-01','2026-01-10','STS-PRESENT','موجود','م','#38BDF8','A-TEST-01','SITE-UNSPECIFIED','غير محدد','manual','USR-TEST','2026-01-10T10:00:00Z','REQ-0001'))
    check('Duplicate attendance request idempotency key rejected',reject(conn,"INSERT INTO AttendanceRecords(PersonId,AttendanceLocalDate,StatusId,StatusNameSnapshot,StatusSymbolSnapshot,StatusColorSnapshot,AssignmentIdAtEffectiveDate,SiteIdAtEffectiveDate,SiteNameSnapshot,Source,CreatedByUserId,CommittedAtUtc,IdempotencyKey) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",('P-TEST-01','2026-01-11','STS-PRESENT','موجود','م','#38BDF8','A-TEST-01','SITE-UNSPECIFIED','غير محدد','manual','USR-TEST','2026-01-11T10:00:00Z','REQ-0001')))
    check('Only one committed attendance for same person/date',reject(conn,"INSERT INTO AttendanceRecords(PersonId,AttendanceLocalDate,StatusId,StatusNameSnapshot,StatusSymbolSnapshot,StatusColorSnapshot,AssignmentIdAtEffectiveDate,SiteIdAtEffectiveDate,SiteNameSnapshot,Source,CreatedByUserId,CommittedAtUtc,IdempotencyKey) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",('P-TEST-01','2026-01-10','STS-ABSENT','غياب','غ','#EF4444','A-TEST-01','SITE-UNSPECIFIED','غير محدد','manual','USR-TEST','2026-01-10T10:00:00Z','REQ-0002')))
    check('Committed attendance read returns exactly one row',conn.execute("SELECT COUNT(*) FROM AttendanceRecords WHERE PersonId='P-TEST-01' AND AttendanceLocalDate='2026-01-10'").fetchone()[0]==1)
    # Discovery probes. These are not production feature acceptance tests, and never mutate the real project.
    audit_triggers=[x for x, in conn.execute("SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name='AuditEvents'")]
    if not audit_triggers:gap('No SQL-level append-only protection for AuditEvents','V001 defines no UPDATE/DELETE guard triggers on AuditEvents','Raw audit rows could be modified by a direct DB writer; later Phase 11 must establish invariants and test them','11')
    unique_audit=[(row[1],row[2]) for row in conn.execute('PRAGMA index_list(AuditEvents)')]
    if not any(conn.execute(f'PRAGMA index_info("{idx}")').fetchall() and {r[2] for r in conn.execute(f'PRAGMA index_info("{idx}")')}=={'OperationCode','CorrelationId'} for idx,unique in unique_audit if unique):
        gap('Audit (OperationCode, CorrelationId) has no SQL UNIQUE guarantee','SqliteAuditEventWriter performs SELECT then INSERT, V001 has no pairwise unique index','Atomic duplicate prevention must be verified when audit writer is integrated; audit ownership Phase 11','11')
    site_triggers=[row[0] for row in conn.execute("SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name='Sites'")]
    if not site_triggers:gap('Protected default site not guarded by SQL triggers','IsSystemSite=1 is metadata; schema does not stop direct UPDATE/DELETE of SITE-UNSPECIFIED','Verify C# protected-site policy and storage constraints in Phase 05','05')
    conn.close()
    check('Original archive SHA-256 unchanged after verification',sha(source)==src_sha)
    check('External trace CSV SHA-256 unchanged after verification',sha(trace)==trace_sha)

result={
    'title':'Phase 00 / Step 01 / Supplemental independent evidence (NOT phase acceptance)',
    'timestamp_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'source_archive':str(source),'source_zip_sha256':src_sha,
    'trace_csv_sha256':trace_sha,
    'execution_environment':'Python sqlite3 (ordinary unencrypted SQLite), isolated temporary DB',
    'passed':sum(x['result']=='PASS' for x in checks),'total':len(checks),'checks':checks,
    'recorded_deferred_gaps':gaps,
    'not_verified':['C# tests have not run in this environment (no dotnet SDK)','Native SQLCipher encrypted persistent database / wrong-key denial','WebView2 bridge; Windows UI not due until Phase 04','End-to-end action-level authorization and idempotency','Full Phase 00 acceptance run not due until all Phase 00 steps complete'],
    'step_00_01_acceptance':'BLOCKED — real C# runtime, SQLCipher and applicable case execution still required'
}
output.parent.mkdir(parents=True,exist_ok=True)
output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'passed':result['passed'],'total':result['total'],'deferred_gaps':len(gaps),'acceptance':result['step_00_01_acceptance']},ensure_ascii=False))
if any(x['result']=='FAIL' for x in checks):
    raise SystemExit(1)
