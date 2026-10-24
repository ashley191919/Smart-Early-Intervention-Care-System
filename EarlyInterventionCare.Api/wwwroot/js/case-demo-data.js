(function (root) {
    // Existing synthetic Case list only. No questionnaire answers or real records.
    const records = [
        { id: "DEMO-001", name: "王○安", status: "in-progress", priority: "high", contactName: "王○美", updatedAt: "2026-10-06" },
        { id: "DEMO-002", name: "陳○恩", status: "in-progress", priority: "normal", contactName: "陳○芬", updatedAt: "2026-10-05" },
        { id: "DEMO-003", name: "林○宇", status: "pending-contact", priority: "normal", contactName: "林○婷", updatedAt: "2026-10-04" },
        { id: "DEMO-004", name: "張○庭", status: "in-progress", priority: "high", contactName: "張○華", updatedAt: "2026-10-03" },
        { id: "DEMO-005", name: "李○安", status: "completed", priority: "normal", contactName: "李○玲", updatedAt: "2026-10-02" },
        { id: "DEMO-006", name: "黃○恩", status: "in-progress", priority: "normal", contactName: "黃○珍", updatedAt: "2026-10-01" },
        { id: "DEMO-007", name: "吳○辰", status: "pending-contact", priority: "high", contactName: "吳○萱", updatedAt: "2026-09-30" },
        { id: "DEMO-008", name: "劉○晴", status: "in-progress", priority: "normal", contactName: "劉○雅", updatedAt: "2026-09-29" },
        { id: "DEMO-009", name: "蔡○睿", status: "completed", priority: "normal", contactName: "蔡○慧", updatedAt: "2026-09-28" },
        { id: "DEMO-010", name: "楊○樂", status: "completed", priority: "normal", contactName: "楊○君", updatedAt: "2026-09-27" },
        { id: "DEMO-011", name: "王○涵", status: "pending-contact", priority: "normal", contactName: "王○芳", updatedAt: "2026-09-26" },
        { id: "DEMO-012", name: "陳○佑", status: "in-progress", priority: "high", contactName: "陳○蓉", updatedAt: "2026-09-25" },
        { id: "DEMO-013", name: "林○希", status: "completed", priority: "normal", contactName: "林○如", updatedAt: "2026-09-24" },
        { id: "DEMO-014", name: "張○軒", status: "in-progress", priority: "normal", contactName: "張○惠", updatedAt: "2026-09-23" },
        { id: "DEMO-015", name: "李○妍", status: "completed", priority: "high", contactName: "李○真", updatedAt: "2026-09-22" },
        { id: "DEMO-016", name: "黃○皓", status: "completed", priority: "normal", contactName: "黃○宜", updatedAt: "2026-09-21" },
        { id: "DEMO-017", name: "吳○彤", status: "pending-contact", priority: "normal", contactName: "吳○雯", updatedAt: "2026-09-20" },
        { id: "DEMO-018", name: "劉○宸", status: "in-progress", priority: "normal", contactName: "劉○敏", updatedAt: "2026-09-19" },
        { id: "DEMO-019", name: "蔡○綺", status: "completed", priority: "normal", contactName: "蔡○靜", updatedAt: "2026-09-18" },
        { id: "DEMO-020", name: "楊○哲", status: "completed", priority: "normal", contactName: "楊○怡", updatedAt: "2026-09-17" },
        { id: "DEMO-021", name: "王○寧", status: "completed", priority: "normal", contactName: "王○琪", updatedAt: "2026-09-16" },
        { id: "DEMO-022", name: "陳○霖", status: "completed", priority: "normal", contactName: "陳○瑄", updatedAt: "2026-09-15" },
        { id: "DEMO-023", name: "林○柔", status: "completed", priority: "normal", contactName: "林○菁", updatedAt: "2026-09-14" },
        { id: "DEMO-024", name: "林○樂", status: "pending-contact", priority: "normal", contactName: "林○婷", updatedAt: "2026-10-06" }
    ];
    if (typeof module === "object" && module.exports) module.exports = records;
    else root.MedicalCaseDemo = Object.freeze(records.map(Object.freeze));
})(typeof window === "undefined" ? globalThis : window);
