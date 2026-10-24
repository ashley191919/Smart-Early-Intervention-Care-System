'use strict';
// Consumer-only synthetic data. Not a seed, persisted response or production authorization fixture.
const { records } = require('./MedicalCaseFixtures.cjs');
const caseId = records[0].caseId;
const tasks = ['PENDING', 'IN_PROGRESS', 'SUBMITTED', 'CANCELLED'].map((taskStatus, index) => ({
  taskId: '50000000-0000-4000-8000-' + String(index + 1).padStart(12, '0'), caseId,
  questionnaireId: '30000000-0000-4000-8000-000000000001',
  questionnaireVersionId: '40000000-0000-4000-8000-000000000001',
  questionnaireTitle: '虛構問卷 ' + (index + 1), versionNumber: '1.0.0',
  respondentRole: index % 2 ? 'TEACHER' : 'PARENT', assignmentRound: index + 1, isRequired: index < 3,
  taskStatus, createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-24T23:30:00Z',
  submittedAtUtc: taskStatus === 'SUBMITTED' ? '2026-10-24T23:30:00Z' : null
}));
// Explicit server response example, never used to implement the production completion algorithm.
const completion = { caseId, totalTaskCount: 4, requiredTaskCount: 3, requiredSubmittedCount: 1,
  requiredPendingCount: 1, requiredInProgressCount: 1, allRequiredSubmitted: false };
module.exports = { caseId, tasks, completion };
