'use strict';
// Selectively adapted from medical-v53. Current pages and dependencies, not its obsolete UI.
const path = require('node:path');
const root = path.resolve(__dirname, '../../EarlyInterventionCare.Api/wwwroot');
const pages = ['staff-login', 'staff-dashboard', 'cases', 'case-detail', 'case-detail-empty', 'case-edit',
  'case-create', 'form-assign', 'response-detail', 'staff-forgot-password', 'staff-reset-password', 'invitation-success'];
const scripts = ['case-create', 'case-demo-data', 'case-detail', 'case-edit', 'cases', 'form-assign', 'invitation-success',
  'medical-api', 'medical-case-detail', 'medical-case-model', 'medical-case-source', 'medical-config', 'medical-demo-client',
  'medical-task-model', 'medical-task-view', 'response-detail', 'shared-demo', 'staff-auth', 'staff-dashboard',
  'staff-forgot-password', 'staff-header', 'staff-login', 'staff-permissions', 'staff-reset-password', 'system-demo-ui'];
const styles = ['auth-common', 'case-create', 'case-detail', 'case-edit', 'cases', 'form-assign', 'invitation-success',
  'response-detail', 'staff-dashboard', 'staff-forgot-password', 'staff-header', 'staff-login-theme', 'staff-login',
  'staff-reset-password', 'system-demo', 'system-interface'];
const assets = Object.freeze([...pages.map(x => x + '.html'), ...scripts.map(x => 'js/' + x + '.js'),
  ...styles.map(x => 'css/' + x + '.css'), 'images/hospital-logo.png'].sort());
module.exports = { root, assets };
