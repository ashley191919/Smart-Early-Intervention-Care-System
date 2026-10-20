(() => {
    "use strict";

    // Presentation only. The API must still authorize the permission and Case scope.
    function refresh() {
        const auth = window.StaffAuth;
        const state = auth?.getPermissionState() ?? "pending";
        document.querySelectorAll("[data-staff-permission]").forEach((control) => {
            const allowed = auth?.hasPermission(control.dataset.staffPermission) === true;
            const hint = document.getElementById(control.dataset.permissionHint);
            if (control.dataset.permissionMode === "hide") control.hidden = !allowed;
            // Inputs opt in with disabled in HTML to avoid a flash of enabled controls.
            if ("disabled" in control) control.disabled = !allowed;
            control.setAttribute("aria-disabled", String(!allowed));
            if (hint) {
                hint.textContent = allowed ? "" : state === "pending" ? "正在確認操作權限…" :
                    state === "ready" || state === "forbidden" ? "您沒有此操作的權限。" : "目前無法確認操作權限，請重新登入後再試。";
                hint.hidden = allowed;
            }
        });
    }

    // Disable link/keyboard and form actions too; never infer permission from a role name.
    function guard(event) {
        const control = event.target.closest("[data-staff-permission]");
        if (control && window.StaffAuth?.hasPermission(control.dataset.staffPermission) !== true) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }
    document.addEventListener("click", guard, true);
    document.addEventListener("change", guard, true);
    document.addEventListener("submit", guard, true);
    document.addEventListener("staff-authenticated", refresh);
    document.addEventListener("staff-permissions-changed", refresh);
    refresh();
})();
