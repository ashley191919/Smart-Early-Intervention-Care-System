import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Descriptor-only checks. Never inspect process.env, application settings or sockets.
const fields = ['profile', 'environment', 'dotnetEnvironment', 'frontendOrigin', 'apiOrigin', 'apiBaseUrl', 'staff', 'requiredSettings'];
const staffFields = ['enabled', 'trustedOrigins', 'cookieName', 'secure', 'httpOnly', 'sameSite', 'path', 'domain', 'credentials', 'csrfEnabled', 'csrfHeader'];
const settingFields = ['jwtIssuerConfigured', 'jwtAudienceConfigured', 'jwtSigningKeyConfigured', 'databaseConnectionConfigured'];
const shape = (x, keys) => x !== null && typeof x === 'object' && !Array.isArray(x)
  && Object.keys(x).length === keys.length && keys.every(k => Object.hasOwn(x, k));
const origin = value => {
  if (typeof value !== 'string' || value !== value.trim()) return null;
  // Reject credentials, paths, queries, encoded hosts and URL parser normalization.
  if (!/^https?:\/\/(?:localhost|127\.0\.0\.1)(?::[1-9][0-9]{0,4})?$/.test(value)) return null;
  try {
    const u = new URL(value);
    const port = u.port ? Number(u.port) : u.protocol === 'https:' ? 443 : 80;
    return port <= 65535 ? { url: u, port } : null;
  } catch { return null; }
};

export function check(config) {
  const rows = [];
  const add = (status, code, message) => rows.push({ status, code, message });
  const rule = (ok, code, message) => add(ok ? 'PASS' : 'FAIL', code, message);
  // Diagnostics never include supplied keys, values or exception text.
  if (!shape(config, fields)) {
    add('FAIL', 'DESCRIPTOR_SCHEMA', '設定描述欄位不完整或含不允許欄位；請勿提供私人設定。');
    return rows;
  }
  const health = config.profile === 'health-probe';
  const staff = config.profile === 'staff-same-origin';
  rule(health || staff, 'PROFILE', '僅支援既有 health 測試或待驗收的同源醫療設定。');
  rule(['Development', 'IsolatedTest'].includes(config.environment)
    && config.dotnetEnvironment === config.environment, 'ENVIRONMENT', '兩個環境名稱必須一致，且僅限本機測試環境。');
  const front = origin(config.frontendOrigin);
  const api = origin(config.apiOrigin);
  rule(!!front && !!api, 'LOCAL_ORIGINS', 'Origin 必須為明確的 localhost／127.0.0.1 HTTP(S) 來源，不含路徑或憑證。');
  rule(!!front && !!api && ![3306, 33326].includes(front.port) && ![3306, 33326].includes(api.port),
    'PORTS', 'Web Port 必須合法，且不可使用資料庫 Port；未探測占用。');
  if (!shape(config.requiredSettings, settingFields)
      || !Object.values(config.requiredSettings).every(x => typeof x === 'boolean')) {
    add('FAIL', 'SETTINGS_SCHEMA', '必要設定只能填寫已配置與否的布林值，不可填秘密值。');
  } else {
    rule(Object.values(config.requiredSettings).every(Boolean), 'REQUIRED_SETTINGS',
      'JWT issuer／audience／簽章與資料庫設定須聲明已配置；未讀取或驗證實際值。');
  }
  if (health) {
    rule(config.environment === 'IsolatedTest', 'HEALTH_ENVIRONMENT', 'Health 測試必須使用 IsolatedTest。');
    rule(config.frontendOrigin === 'http://127.0.0.1:5187'
      && config.apiOrigin === 'http://127.0.0.1:5186' && config.apiBaseUrl === '/health/local',
    'HEALTH_ROUTE', '重用 LocalHostProbe 固定 5187 → 5186 GET /health/local；不可作為業務代理。');
    rule(config.staff === null, 'HEALTH_NO_AUTH', 'Health profile 不可宣告醫療 Cookie 登入。');
    add('WARN', 'HEALTH_ONLY', '僅檢查 health 配置；databaseChecked=false，不證明登入或資料庫可用。');
  }
  if (staff) {
    rule(config.environment === 'Development', 'STAFF_ENVIRONMENT', '目前 IsolatedTest 停用醫療 Cookie；此候選配置僅限 Development。');
    rule(!!front && !!api && front.url.protocol === 'https:' && api.url.protocol === 'https:',
      'HTTPS', '醫療瀏覽器入口與 API 必須為 HTTPS；本工具不配置反向代理。');
    rule(!!front && !!api && front.url.origin === api.url.origin, 'SAME_ORIGIN',
      '現有醫療 Client 必須同源；不同主機、Scheme 或 Port 不受此配置支援。');
    rule(typeof config.apiBaseUrl === 'string'
      && (config.apiBaseUrl === '' || /^\/(?:[A-Za-z0-9_-]+\/)*[A-Za-z0-9_-]+$/.test(config.apiBaseUrl)),
    'BASE_URL', 'API Base URL 僅接受空字串或同源路徑前綴，不接受 URL、查詢或路徑穿越。');
    if (!shape(config.staff, staffFields)) {
      add('FAIL', 'STAFF_SCHEMA', '醫療 Cookie／CSRF 描述欄位不完整或含額外欄位。');
    } else {
      const s = config.staff;
      rule(s.enabled === true, 'STAFF_ENABLED', '醫療 profile 必須明確啟用 Cookie 設定。');
      rule(Array.isArray(s.trustedOrigins) && s.trustedOrigins.length > 0
        && s.trustedOrigins.every(x => origin(x)?.url.protocol === 'https:')
        && !!front && s.trustedOrigins.some(x => origin(x)?.url.origin === front.url.origin),
      'TRUSTED_ORIGINS', '可信來源須為本機 HTTPS Origin，且包含前端來源；不得使用萬用字元。');
      rule(s.cookieName === '__Host-earlycare-staff' && s.secure === true && s.httpOnly === true
        && s.sameSite === 'Strict' && s.path === '/' && s.domain === null,
      'COOKIE', 'Cookie 必須沿用 __Host 名稱、Secure、HttpOnly、Strict、根路徑與無 Domain。');
      rule(s.credentials === 'same-origin', 'CREDENTIALS', '沿用現有 same-origin 認證傳遞，不擴大跨來源存取。');
      rule(s.csrfEnabled === true && s.csrfHeader === 'X-EarlyCare-CSRF', 'CSRF',
        '寫入操作須保留 CSRF 與既有固定標頭；不能以 Cookie 或 Origin 取代。');
    }
    add('WARN', 'ROUTING_UNVERIFIED', '前綴路由、TLS 憑證、反向代理與實際 Cookie／CSRF 行為尚須另行核准驗收。');
    add('WARN', 'INTEGRATION_BLOCKED', 'Guardian／Consent／醫療 Scope 與共同 Schema 尚待整合；此配置不可啟用正式問卷入口。');
  }
  add('WARN', 'STATIC_ONLY', '僅為設定聲明的離線檢查，不證明埠可用、API／登入／資料庫／Docker／院內部署成功。');
  return rows;
}

export const format = rows => rows.map(r => `${r.status} ${r.code}: ${r.message}`).join('\n');

// CLI accepts only bundled synthetic descriptors, never arbitrary file paths.
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const name = args.length === 1 && ['health', 'staff'].includes(args[0]) ? args[0] : null;
  if (!name) {
    console.error('FAIL USAGE: node tools/DeploymentReadiness/check.mjs health|staff（僅合成設定）');
    process.exitCode = 1;
  } else {
    try {
      const rows = check(JSON.parse(readFileSync(new URL(`./${name}.example.json`, import.meta.url), 'utf8')));
      console.log(format(rows));
      process.exitCode = rows.some(r => r.status === 'FAIL') ? 1 : 0;
    } catch {
      console.error('FAIL INPUT: 無法讀取合成設定；不輸出原始內容或例外。');
      process.exitCode = 1;
    }
  }
}
