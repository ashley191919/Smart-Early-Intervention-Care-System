-- Shared contract v1.1 / 2026-10-05 / REVIEW DRAFT ONLY.
-- No database selection, destructive statements, seed data or migration execution.
-- Implement through the single agreed EF Core migration owner after review.
-- Application supplies UUIDs and UTC timestamps; connection timezone must be UTC.
-- External user / consent IDs have no foreign keys until those modules agree.
-- Service transactions enforce state transitions, published-version immutability,
-- nonempty grant scope, one ACTIVE grant per task, and required consent for production.

CREATE TABLE cases (
    case_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    case_code VARCHAR(64) NOT NULL,
    child_name VARCHAR(100) NOT NULL,
    birth_date DATE NOT NULL,
    sex ENUM('MALE', 'FEMALE', 'UNKNOWN') NOT NULL,
    case_status ENUM('NEW', 'TO_CONTACT', 'APPOINTED', 'WAITING_FORM', 'FORM_COMPLETED', 'COMPLETED') NOT NULL DEFAULT 'NEW',
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (case_id),
    UNIQUE KEY uq_cases_code (case_code)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE questionnaires (
    questionnaire_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    questionnaire_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    title VARCHAR(200) NOT NULL,
    created_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (questionnaire_id),
    UNIQUE KEY uq_questionnaires_code (questionnaire_code)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE questionnaire_versions (
    questionnaire_version_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    questionnaire_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    respondent_role ENUM('PARENT', 'TEACHER') NOT NULL,
    version_number VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    version_status ENUM('DRAFT', 'PUBLISHED', 'RETIRED') NOT NULL DEFAULT 'DRAFT',
    definition_snapshot JSON NOT NULL,
    scoring_definition JSON NULL,
    created_at_utc DATETIME(6) NOT NULL,
    published_at_utc DATETIME(6) NULL,
    PRIMARY KEY (questionnaire_version_id),
    UNIQUE KEY uq_versions_number (questionnaire_id, respondent_role, version_number),
    UNIQUE KEY uq_versions_type (questionnaire_version_id, questionnaire_id, respondent_role),
    CONSTRAINT fk_versions_questionnaire FOREIGN KEY (questionnaire_id)
        REFERENCES questionnaires (questionnaire_id) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE case_questionnaires (
    task_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    case_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    questionnaire_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    questionnaire_version_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    respondent_role ENUM('PARENT', 'TEACHER') NOT NULL,
    assignment_round INT UNSIGNED NOT NULL DEFAULT 1,
    is_required BOOLEAN NOT NULL DEFAULT TRUE,
    task_status ENUM('PENDING', 'IN_PROGRESS', 'SUBMITTED', 'CANCELLED') NOT NULL DEFAULT 'PENDING',
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    submitted_at_utc DATETIME(6) NULL,
    cancelled_at_utc DATETIME(6) NULL,
    PRIMARY KEY (task_id),
    UNIQUE KEY uq_tasks_assignment (case_id, questionnaire_id, respondent_role, assignment_round),
    UNIQUE KEY uq_tasks_scope (task_id, case_id, respondent_role),
    UNIQUE KEY uq_tasks_version (task_id, questionnaire_version_id),
    CONSTRAINT fk_tasks_case FOREIGN KEY (case_id)
        REFERENCES cases (case_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_tasks_version_type FOREIGN KEY (questionnaire_version_id, questionnaire_id, respondent_role)
        REFERENCES questionnaire_versions (questionnaire_version_id, questionnaire_id, respondent_role)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE teacher_grants (
    grant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    case_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    -- Match the full ENUM definition on task scope for composite foreign keys.
    -- The service only accepts TEACHER here and in teacher_grant_tasks.
    respondent_role ENUM('PARENT', 'TEACHER') NOT NULL DEFAULT 'TEACHER',
    code_hash BINARY(32) NOT NULL,
    grant_status ENUM('ACTIVE', 'USED', 'REVOKED') NOT NULL DEFAULT 'ACTIVE',
    issued_by_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    consent_reference_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    is_development BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc DATETIME(6) NOT NULL,
    used_at_utc DATETIME(6) NULL,
    revoked_at_utc DATETIME(6) NULL,
    PRIMARY KEY (grant_id),
    UNIQUE KEY uq_grants_code_hash (code_hash),
    UNIQUE KEY uq_grants_scope (grant_id, case_id, respondent_role),
    KEY ix_grants_case_status (case_id, grant_status),
    CONSTRAINT fk_grants_case FOREIGN KEY (case_id)
        REFERENCES cases (case_id) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE teacher_grant_tasks (
    grant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    task_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    case_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    respondent_role ENUM('PARENT', 'TEACHER') NOT NULL DEFAULT 'TEACHER',
    PRIMARY KEY (grant_id, task_id),
    KEY ix_grant_tasks_task_scope (task_id, case_id, respondent_role),
    CONSTRAINT fk_grant_tasks_grant_scope FOREIGN KEY (grant_id, case_id, respondent_role)
        REFERENCES teacher_grants (grant_id, case_id, respondent_role)
        ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_grant_tasks_task_scope FOREIGN KEY (task_id, case_id, respondent_role)
        REFERENCES case_questionnaires (task_id, case_id, respondent_role)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE teacher_sessions (
    session_hash BINARY(32) NOT NULL,
    grant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    session_status ENUM('ACTIVE', 'REVOKED') NOT NULL DEFAULT 'ACTIVE',
    created_at_utc DATETIME(6) NOT NULL,
    last_seen_at_utc DATETIME(6) NOT NULL,
    session_expires_at_utc DATETIME(6) NOT NULL,
    revoked_at_utc DATETIME(6) NULL,
    PRIMARY KEY (session_hash),
    KEY ix_sessions_grant_status (grant_id, session_status),
    CONSTRAINT fk_sessions_grant FOREIGN KEY (grant_id)
        REFERENCES teacher_grants (grant_id) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE questionnaire_drafts (
    task_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    questionnaire_version_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    respondent_name VARCHAR(100) NULL,
    filled_on DATE NULL,
    answers_json JSON NOT NULL,
    observation TEXT NULL,
    revision BIGINT UNSIGNED NOT NULL,
    last_saved_by_grant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (task_id),
    CONSTRAINT fk_drafts_task_version FOREIGN KEY (task_id, questionnaire_version_id)
        REFERENCES case_questionnaires (task_id, questionnaire_version_id)
        ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_drafts_grant_scope FOREIGN KEY (last_saved_by_grant_id, task_id)
        REFERENCES teacher_grant_tasks (grant_id, task_id)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE questionnaire_responses (
    response_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    task_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    questionnaire_version_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    respondent_name VARCHAR(100) NOT NULL,
    filled_on DATE NOT NULL,
    answers_json JSON NOT NULL,
    observation TEXT NULL,
    submitted_by_grant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    idempotency_key CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    payload_hash BINARY(32) NOT NULL,
    submitted_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (response_id),
    UNIQUE KEY uq_responses_task (task_id),
    UNIQUE KEY uq_responses_grant_key (submitted_by_grant_id, idempotency_key),
    CONSTRAINT fk_responses_task_version FOREIGN KEY (task_id, questionnaire_version_id)
        REFERENCES case_questionnaires (task_id, questionnaire_version_id)
        ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_responses_grant_scope FOREIGN KEY (submitted_by_grant_id, task_id)
        REFERENCES teacher_grant_tasks (grant_id, task_id)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Parent drafts/responses use NULL grant IDs and their authenticated case permission.
-- Teacher writes require a non-NULL grant ID verified by the service transaction.
-- Idempotency for parents needs an authenticated-user scope in the later identity contract.
-- A receipt-only replay channel after USED is intentionally not implemented by these tables.
-- No clinical scoring, contacts, case history, consent tables or full audit schema included.

-- v1.1 includes drafts with optimistic revision checks enforced by the service.
-- Medical staff only edit exported Excel offline; no import or response update in v1.1.
-- Original questionnaire_responses remain immutable after submission.
