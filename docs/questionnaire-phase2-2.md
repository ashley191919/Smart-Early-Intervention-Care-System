# C Phase 2-2: Development draft and submission

This increment uses the existing `questionnaire_drafts`, `questionnaire_responses`,
`case_questionnaires`, TeacherGrant, TeacherGrantTask and TeacherSession mappings.
It does not change schema, migrations, Case Status, EXPIRED, Guardian or dashboard rules.

## Scope and integration boundary

All endpoints below remain under `/api/dev/teacher-workspace`, are hidden outside
Development, and require the existing HttpOnly teacher session cookie obtained from
the existing `verify` endpoint. A staff JWT is not sufficient for teacher answers.
The service accepts only IsDevelopment grants for the existing synthetic CaseId,
TEACHER tasks in that grant, and an unexpired session. No request can supply a caseId
or userId to broaden that scope.

TODO(11): agree the formal Case access, Guardian -> User -> Case relationship,
consent and answer-view authorization. No formal parent or medical answer API is exposed.
TODO(7): agree Case Status eligibility, required-form completion/round scope,
workflow notifications and dashboard contract. None are inferred here.

## APIs

### GET /api/dev/teacher-workspace/tasks/{taskId}/draft

Requires an ACTIVE grant/session and an editable task. A missing draft returns 200
with revision=0, answers=[], updatedAtUtc=null. An existing draft returns its persisted
partial answers, nullable name/date/observation, revision, taskStatus and saved UTC time.
Submitted tasks cannot read or overwrite draft contents through this endpoint.

### PUT /api/dev/teacher-workspace/tasks/{taskId}/draft

Requires `Content-Type: application/json`, `X-Teacher-Draft: 1` and the same-origin
teacher cookie. The same source/header protections used by development submission apply.
Maximum request size is 32 KiB.

```json
{
  "questionnaireVersionId": "30000000-0000-4000-8000-000000000001",
  "expectedRevision": 0,
  "respondentName": null,
  "filledOn": null,
  "answers": [{ "questionId": "SNAP_IV_Q01", "optionValue": "1" }],
  "observation": null
}
```

Use the assigned version/task UUIDs; the example version is only the existing fixture.
The request replaces the current partial draft, rather than patching individual answers.
Omitted/null answers mean an empty draft; missing answers are allowed, but duplicate,
unknown question IDs and invalid option values return 400. Optional name/date may be
empty, but supplied values follow the existing submit limits and date validation.

First save requires expectedRevision=0 and produces revision=1. Subsequent saves require
the latest revision and increment it by one. Stale revisions return 409 DRAFT_CONFLICT
without overwriting saved content. Revision is serialized as an unsigned JSON integer;
clients must not round large revision values.
After a successful commit only, PENDING becomes IN_PROGRESS. Saving does not create
a response or consume the grant. A replacement development grant for the same editable
task can read the existing task draft. The originating grant metadata remains stored.

### POST /api/dev/teacher-workspace/tasks/{taskId}/submit

The existing URL, WorkspaceSubmission payload, `Idempotency-Key` UUID and
`X-Teacher-Submission: 1` are preserved. Submit requires the full explicit payload;
it does not silently fill missing answers from the draft. Draft and Submit now use
one shared validator extracted from the previous TeacherWorkspaceSubmission code.

Grant -> UUID-ordered task locks, validation, response insertion, SUBMITTED and, after
all grant tasks submit, USED/session revocation are handled in one transaction. Case
Status is never changed. Existing draft rows remain stored but are no longer editable.
The canonical payload hash is unchanged; old same-key retries remain compatible.
USED grants retain only the submitting session's receipt access, up to 15 minutes
and never beyond its original expiry. Logout clears that access, and revocation denies it.

### GET /api/dev/teacher-workspace/tasks/{taskId}/receipt

Returns only responseId, taskId, questionnaireVersionId, submittedAtUtc, grantStatus
and replayed=false. It never returns answers, name, filledOn, observation, hashes,
idempotency keys or scoring. Requires the same grant and task membership, plus either
an ACTIVE session or the existing bounded USED receipt window. No response for that
grant/task returns 404 RESPONSE_NOT_FOUND. This is a receipt query, not an answer-view API.

## Implementation and verification

`TeacherQuestionnaireWorkflowService` owns the shared draft/submit/receipt behavior;
`ITeacherQuestionnaireStore` separates transaction/storage concerns. The EF adapter
uses the existing MySQL tables and revision concurrency token. The previous workspace
service now delegates submission to this engine, retaining the API shape and best-effort
post-commit audit behavior. No second validator or response table is introduced.

Offline harness: `tests/QuestionnairePhase22Harness.csproj`. Tests exercise the actual
workflow service with a transactional fake store, partial/full SNAP validation, revisions,
authorization scope, idempotency/hash compatibility, immutability, multiple tasks,
receipt privacy/expiry, rollback and overlapping calls. The EF model check is also offline.
They do not establish actual MySQL locking, restart durability or HTTP cookie behavior.

```powershell
dotnet run --project tests/QuestionnairePhase22Harness.csproj
```

Manual development acceptance after user approval: verify an existing fixture grant;
GET empty Draft; PUT partial Draft; GET it again; PUT with stale revision and expect 409;
submit a full payload; inspect the receipt; confirm Draft writes are denied and retry
the identical submit key/payload returns the same responseId. These calls write business
data, so they were not automatically executed by this implementation task.

The existing teacher page in `mode=authorized` now loads and saves through the Draft
endpoints with its browser-managed teacher session cookie. A successful PUT updates
the revision and saved timestamp; a 409 preserves current input and requires explicitly
reloading the latest draft. The unauthenticated preview remains page-local and labelled
as a preview. Submit payload and receipt retry behavior are unchanged.

Offline UI checks (Node.js; browser smoke additionally requires Microsoft Edge on Windows,
or `TEACHER_TEST_BROWSER` pointing to a compatible Chromium browser):

```powershell
node --test tests/TeacherDraftClient.test.cjs
node tests/TeacherDraftBrowserSmoke.cjs
```

The browser test serves the actual UI with a synthetic loopback HTTP fixture, including
a synthetic HttpOnly session cookie. It checks partial save, refresh restoration,
conflict/reload, failed save, unchanged submission payload and the 1440 x 900 viewport.
It does not start ASP.NET Core or access MySQL, and does not prove actual DB persistence.
