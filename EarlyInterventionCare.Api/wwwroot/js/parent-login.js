"use strict";

// Frontend demonstration only: no SMS, API calls, tokens or persistent phone/code storage.
(() => {
  const phoneForm = document.querySelector('#parent-form');
  const otpForm = document.querySelector('#parent-otp-form');
  const complete = document.querySelector('#parent-preview-complete');
  const phone = document.querySelector('#parent-phone');
  const otp = document.querySelector('#parent-otp');
  const phoneError = document.querySelector('#phone-error');
  const otpError = document.querySelector('#parent-otp-error');
  const resend = document.querySelector('#parent-resend');
  const expiryLabel = document.querySelector('#parent-otp-expiry');
  const demoCode = document.querySelector('#parent-demo-code');
  let currentCode = '';
  let expiresAt = 0;
  let resendAt = 0;
  let timer = null;

  function clearChallenge() {
    clearInterval(timer);
    timer = null;
    currentCode = '';
    expiresAt = 0;
    resendAt = 0;
    otpForm.reset();
    otpError.hidden = true;
    otpError.textContent = '';
    otp.removeAttribute('aria-invalid');
    demoCode.textContent = '';
    document.querySelector('#parent-masked-phone').textContent = '';
    expiryLabel.textContent = '';
    resend.disabled = true;
  }

  function resetPreview() {
    clearChallenge();
    phoneForm.reset();
    phoneError.hidden = true;
    phone.removeAttribute('aria-invalid');
    phoneForm.hidden = false;
    otpForm.hidden = true;
    complete.hidden = true;
  }

  function updateClock() {
    const remaining = Math.max(0, Math.ceil((expiresAt - Date.now()) / 1000));
    const cooldown = Math.max(0, Math.ceil((resendAt - Date.now()) / 1000));
    expiryLabel.textContent = remaining > 0
      ? `示範碼有效時間：${Math.floor(remaining / 60)} 分 ${String(remaining % 60).padStart(2, '0')} 秒`
      : '示範碼已過期，請重新取得。';
    resend.disabled = cooldown > 0;
    resend.textContent = cooldown > 0 ? `重新取得（${cooldown} 秒）` : '重新取得示範碼';
    if (remaining === 0) {
      currentCode = '';
      demoCode.textContent = '已過期';
      if (cooldown === 0) { clearInterval(timer); timer = null; }
    }
  }

  function issueDemoCode() {
    clearInterval(timer);
    const value = crypto.getRandomValues(new Uint32Array(1))[0];
    const previousCode = currentCode;
    currentCode = String(value % 1000000).padStart(6, '0');
    if (currentCode === previousCode) currentCode = String((Number(currentCode) + 1) % 1000000).padStart(6, '0');
    expiresAt = Date.now() + 5 * 60 * 1000;
    resendAt = Date.now() + 60 * 1000;
    otpForm.reset();
    otpError.hidden = true;
    otp.removeAttribute('aria-invalid');
    demoCode.textContent = currentCode;
    updateClock();
    timer = setInterval(updateClock, 1000);
  }

  phone.addEventListener('input', () => {
    phoneError.hidden = true;
    phone.removeAttribute('aria-invalid');
  });
  otp.addEventListener('input', () => {
    otpError.hidden = true;
    otp.removeAttribute('aria-invalid');
  });
  phoneForm.addEventListener('submit', event => {
    event.preventDefault();
    const number = phone.value.replace(/[\s-]/g, '');
    if (!/^09\d{8}$/.test(number)) {
      phoneError.hidden = false;
      phone.setAttribute('aria-invalid', 'true');
      phone.focus();
      return;
    }
    document.querySelector('#parent-masked-phone').textContent = `${number.slice(0, 4)}-***-${number.slice(-3)}`;
    phoneForm.hidden = true;
    otpForm.hidden = false;
    issueDemoCode();
    otp.focus();
  });
  otpForm.addEventListener('submit', event => {
    event.preventDefault();
    const code = otp.value.trim();
    let error = '';
    if (Date.now() >= expiresAt || !currentCode) error = '示範碼已過期，請重新取得。';
    else if (!/^\d{6}$/.test(code)) error = '請輸入 6 位數驗證碼。';
    else if (code !== currentCode) error = '示範碼不正確，請確認畫面上的 6 位數示範碼。';
    if (error) {
      otpError.textContent = error;
      otpError.hidden = false;
      otp.setAttribute('aria-invalid', 'true');
      otp.focus();
      return;
    }
    clearChallenge();
    phoneForm.reset();
    otpForm.hidden = true;
    complete.hidden = false;
    document.querySelector('#parent-complete-title').focus();
  });
  document.querySelector('#parent-change-phone').addEventListener('click', () => { resetPreview(); phone.focus(); });
  document.querySelector('#parent-restart').addEventListener('click', () => { resetPreview(); phone.focus(); });
  resend.addEventListener('click', () => {
    if (Date.now() < resendAt) return;
    issueDemoCode();
    otp.focus();
  });
  document.addEventListener('login-role-change', resetPreview);
  window.addEventListener('pagehide', resetPreview);
})();
