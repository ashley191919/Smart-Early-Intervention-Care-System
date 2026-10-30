'use strict';
const button = document.querySelector('#check');
const status = document.querySelector('#status');
const responseArea = document.querySelector('#response');
button.addEventListener('click', async () => {
  button.disabled = true;
  status.textContent = '檢查中';
  responseArea.textContent = '等待實際回應';
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 7000);
  try {
    const response = await fetch('/health/local', {
      method: 'GET', credentials: 'omit', cache: 'no-store',
      redirect: 'error', signal: controller.signal,
      headers: { Accept: 'application/json' },
    });
    const body = await response.text();
    responseArea.textContent = `HTTP ${response.status}\n${body}`;
    const value = JSON.parse(body);
    if (!response.ok || value.status !== 'alive' || value.databaseChecked !== false) {
      status.textContent = '連線檢查失敗；未取得有效 API 存活回應。';
      return;
    }
    status.textContent = '已取得 API 實際存活回應；資料庫尚未檢查。';
  } catch {
    status.textContent = '連線檢查失敗或逾時。';
    // Do not retain a previous successful result as the current result.
    responseArea.textContent = '未取得可用回應；不得視為成功。';
  } finally {
    clearTimeout(timer);
    button.disabled = false;
  }
});
