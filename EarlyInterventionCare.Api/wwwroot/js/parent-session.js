"use strict";
// Receives a server-issued parent login response. No OTP generation or locally minted JWT.
(function (root) {
 const key = "earlycare.parent.accessToken";
 function signIn(response) {
  if (!response || typeof response.accessToken !== "string" || !response.accessToken.trim() || response.role !== "PARENT") throw new Error("缺少有效的家長登入結果。");
  root.sessionStorage.setItem(key, response.accessToken);
  root.location.assign("/parent-workspace.html?mode=account");
 }
 root.EarlyCareParentSession = Object.freeze({ key, signIn, token: () => root.sessionStorage.getItem(key), signOut: () => root.sessionStorage.removeItem(key) });
})(window);
