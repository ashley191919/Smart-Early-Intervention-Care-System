# Questionnaire Phase 2-1

This increment reuses Questionnaire, QuestionnaireVersion and QuestionnaireTask (case_questionnaires).
No migrations, case-status changes, teacher-grant changes or database seed execution are included.

## Access boundary

All three endpoints require a Bearer JWT. Each request reloads the current account and role;
the account must be ACTIVE and its current role must be ADMIN or CASE_MANAGER.
Case operations are limited to Development and the existing synthetic case
`10000000-0000-4000-8000-000000000001`. All other cases and production case operations
return 403 CASE_ACCESS_NOT_INTEGRATED. This is a temporary fail-closed boundary, not
a completed organization/case authorization rule. Replace it with the shared case-access
service when that contract is available; do not simply remove the check.

The API does not create the fixture or a questionnaire/version. If no catalog data exists,
the list is empty. If the allowed fixture case does not exist, case operations return 404.

## APIs

- GET /api/questionnaires: all questionnaire kinds with only PUBLISHED versions in `versions`.
  A kind without a published version has an empty `versions` array. No answers or definition JSON are returned.
- POST /api/cases/{caseId}/tasks: 201 with taskId, caseId, taskStatus=PENDING.
- GET /api/cases/{caseId}/tasks: array of task summaries, including historical/submitted tasks;
  includes role, round, required flag, version and UTC timestamps, never draft/response answers.

Assignment body (use a version UUID returned by the catalog):

```json
{
  "questionnaireVersionId": "30000000-0000-4000-8000-000000000001",
  "respondentRole": "TEACHER",
  "assignmentRound": 2,
  "isRequired": true
}
```

The UUID shown is the development SNAP-IV version, not an assertion that it exists locally.
Round 2 is illustrative; any existing identical assignment returns 409, including submitted/cancelled ones.
The unique scope is case + questionnaire kind + role + round, even when a different version is requested.
New task UUIDs and UTC timestamps are generated on the server. `isRequired=false` is supported.

Errors: 400 invalid payload or role mismatch; 401 missing/invalid JWT or inactive/missing account;
403 unsupported current role or unavailable case scope; 404 case/version not found;
409 unpublished version or duplicate assignment; 503 database temporarily unavailable.
Malformed GUID route values do not match the endpoint (404).

## Verification

`tests/QuestionnairePhase21Harness.csproj` is an offline executable test harness with a fake store.
It covers assignment validation, same-kind version duplicates, separate roles/rounds,
PENDING/UTC generation, optional tasks, submitted-task preservation and duplicate-insert results.
It also checks JWT metadata and the fail-closed case boundary. It does not test a live MySQL
connection, actual unique-key races or the HTTP authentication pipeline.

```powershell
dotnet run --project tests/QuestionnairePhase21Harness.csproj
```

For manual Swagger verification after restarting the API: authorize with your own JWT,
read the catalog, assign an unused round to the existing fixture, and read its tasks.
Assignment writes one task when you invoke it; no assignment is executed by this implementation task.
