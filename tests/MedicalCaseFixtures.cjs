'use strict';
// Test-owned synthetic Case adapter. Never shipped from wwwroot or connected to a DB.
const { MedicalApiError } = require('../EarlyInterventionCare.Api/wwwroot/js/medical-api.js');
const records = Array.from({ length: 12 }, (_, index) => ({
  caseId: '20000000-0000-4000-8000-' + String(index + 1).padStart(12, '0'),
  caseCode: 'CASE-TEST-' + String(index + 1).padStart(3, '0'), childName: '測試幼兒' + String(index + 1),
  caseStatus: ['NEW', 'TO_CONTACT', 'APPOINTED', 'WAITING_FORM', 'FORM_COMPLETED', 'COMPLETED'][index % 6],
  birthDate: '2021-05-12', sex: 'UNKNOWN', nationalId: 'A123456789', address: '新北市測試區虛構路123號',
  revision: index, createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: '2026-10-' + String(24 - index).padStart(2, '0') + 'T01:00:00Z'
}));
class FakeCaseAdapter {
  constructor() { this.mode = 'api'; this.calls = []; this.status = 0; this.wrongId = false; this.records = structuredClone(records); }
  async list(page, pageSize) {
    this.calls.push({ method: 'list', page, pageSize }); this.check();
    return structuredClone(this.records.slice((page - 1) * pageSize, page * pageSize));
  }
  async detail(caseId) {
    this.calls.push({ method: 'detail', caseId }); this.check();
    const record = this.records.find(row => row.caseId === caseId);
    if (!record) throw new MedicalApiError(404, 'CASE_NOT_FOUND');
    return structuredClone(this.wrongId ? this.records.find(row => row.caseId !== caseId) : record);
  }
  check() { if (this.status) throw new MedicalApiError(this.status, 'SYNTHETIC_FAILURE'); }
}
module.exports = { records, FakeCaseAdapter };
