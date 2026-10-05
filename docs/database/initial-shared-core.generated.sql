CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;

ALTER DATABASE CHARACTER SET utf8mb4;

CREATE TABLE `cases` (
    `case_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `case_code` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `child_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `birth_date` date NOT NULL,
    `sex` ENUM('MALE', 'FEMALE', 'UNKNOWN') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `case_status` ENUM('NEW', 'TO_CONTACT', 'APPOINTED', 'WAITING_FORM', 'FORM_COMPLETED', 'COMPLETED') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'NEW',
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    CONSTRAINT `PK_cases` PRIMARY KEY (`case_id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `questionnaires` (
    `questionnaire_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `questionnaire_code` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `title` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `created_at_utc` datetime(6) NOT NULL,
    CONSTRAINT `PK_questionnaires` PRIMARY KEY (`questionnaire_id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `teacher_grants` (
    `grant_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `case_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `respondent_role` ENUM('PARENT', 'TEACHER') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'TEACHER',
    `code_hash` binary(32) NOT NULL,
    `grant_status` ENUM('ACTIVE', 'USED', 'REVOKED') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'ACTIVE',
    `issued_by_user_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `consent_reference_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `is_development` tinyint(1) NOT NULL DEFAULT FALSE,
    `created_at_utc` datetime(6) NOT NULL,
    `used_at_utc` datetime(6) NULL,
    `revoked_at_utc` datetime(6) NULL,
    CONSTRAINT `PK_teacher_grants` PRIMARY KEY (`grant_id`),
    CONSTRAINT `uq_grants_scope` UNIQUE (`grant_id`, `case_id`, `respondent_role`),
    CONSTRAINT `fk_grants_case` FOREIGN KEY (`case_id`) REFERENCES `cases` (`case_id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `questionnaire_versions` (
    `questionnaire_version_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `questionnaire_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `respondent_role` ENUM('PARENT', 'TEACHER') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `version_number` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `version_status` ENUM('DRAFT', 'PUBLISHED', 'RETIRED') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'DRAFT',
    `definition_snapshot` json NOT NULL,
    `scoring_definition` json NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `published_at_utc` datetime(6) NULL,
    CONSTRAINT `PK_questionnaire_versions` PRIMARY KEY (`questionnaire_version_id`),
    CONSTRAINT `uq_versions_type` UNIQUE (`questionnaire_version_id`, `questionnaire_id`, `respondent_role`),
    CONSTRAINT `fk_versions_questionnaire` FOREIGN KEY (`questionnaire_id`) REFERENCES `questionnaires` (`questionnaire_id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `teacher_sessions` (
    `session_hash` binary(32) NOT NULL,
    `grant_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `session_status` ENUM('ACTIVE', 'REVOKED') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'ACTIVE',
    `created_at_utc` datetime(6) NOT NULL,
    `last_seen_at_utc` datetime(6) NOT NULL,
    `session_expires_at_utc` datetime(6) NOT NULL,
    `revoked_at_utc` datetime(6) NULL,
    CONSTRAINT `PK_teacher_sessions` PRIMARY KEY (`session_hash`),
    CONSTRAINT `fk_sessions_grant` FOREIGN KEY (`grant_id`) REFERENCES `teacher_grants` (`grant_id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `case_questionnaires` (
    `task_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `case_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `questionnaire_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `questionnaire_version_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `respondent_role` ENUM('PARENT', 'TEACHER') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `assignment_round` int unsigned NOT NULL DEFAULT 1,
    `is_required` tinyint(1) NOT NULL DEFAULT TRUE,
    `task_status` ENUM('PENDING', 'IN_PROGRESS', 'SUBMITTED', 'CANCELLED') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING',
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `submitted_at_utc` datetime(6) NULL,
    `cancelled_at_utc` datetime(6) NULL,
    CONSTRAINT `PK_case_questionnaires` PRIMARY KEY (`task_id`),
    CONSTRAINT `uq_tasks_scope` UNIQUE (`task_id`, `case_id`, `respondent_role`),
    CONSTRAINT `uq_tasks_version` UNIQUE (`task_id`, `questionnaire_version_id`),
    CONSTRAINT `fk_tasks_case` FOREIGN KEY (`case_id`) REFERENCES `cases` (`case_id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_tasks_version_type` FOREIGN KEY (`questionnaire_version_id`, `questionnaire_id`, `respondent_role`) REFERENCES `questionnaire_versions` (`questionnaire_version_id`, `questionnaire_id`, `respondent_role`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `teacher_grant_tasks` (
    `grant_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `task_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `case_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `respondent_role` ENUM('PARENT', 'TEACHER') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'TEACHER',
    CONSTRAINT `PK_teacher_grant_tasks` PRIMARY KEY (`grant_id`, `task_id`),
    CONSTRAINT `fk_grant_tasks_grant_scope` FOREIGN KEY (`grant_id`, `case_id`, `respondent_role`) REFERENCES `teacher_grants` (`grant_id`, `case_id`, `respondent_role`) ON DELETE RESTRICT,
    CONSTRAINT `fk_grant_tasks_task_scope` FOREIGN KEY (`task_id`, `case_id`, `respondent_role`) REFERENCES `case_questionnaires` (`task_id`, `case_id`, `respondent_role`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `questionnaire_drafts` (
    `task_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `questionnaire_version_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `respondent_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `filled_on` date NULL,
    `answers_json` json NOT NULL,
    `observation` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `revision` bigint unsigned NOT NULL,
    `last_saved_by_grant_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    CONSTRAINT `PK_questionnaire_drafts` PRIMARY KEY (`task_id`),
    CONSTRAINT `fk_drafts_grant_scope` FOREIGN KEY (`last_saved_by_grant_id`, `task_id`) REFERENCES `teacher_grant_tasks` (`grant_id`, `task_id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_drafts_task_version` FOREIGN KEY (`task_id`, `questionnaire_version_id`) REFERENCES `case_questionnaires` (`task_id`, `questionnaire_version_id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `questionnaire_responses` (
    `response_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `task_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `questionnaire_version_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `respondent_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `filled_on` date NOT NULL,
    `answers_json` json NOT NULL,
    `observation` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `submitted_by_grant_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `idempotency_key` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `payload_hash` binary(32) NOT NULL,
    `submitted_at_utc` datetime(6) NOT NULL,
    CONSTRAINT `PK_questionnaire_responses` PRIMARY KEY (`response_id`),
    CONSTRAINT `fk_responses_grant_scope` FOREIGN KEY (`submitted_by_grant_id`, `task_id`) REFERENCES `teacher_grant_tasks` (`grant_id`, `task_id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_responses_task_version` FOREIGN KEY (`task_id`, `questionnaire_version_id`) REFERENCES `case_questionnaires` (`task_id`, `questionnaire_version_id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_case_questionnaires_questionnaire_version_id_questionnaire_i~` ON `case_questionnaires` (`questionnaire_version_id`, `questionnaire_id`, `respondent_role`);

CREATE UNIQUE INDEX `uq_tasks_assignment` ON `case_questionnaires` (`case_id`, `questionnaire_id`, `respondent_role`, `assignment_round`);

CREATE UNIQUE INDEX `uq_cases_code` ON `cases` (`case_code`);

CREATE INDEX `IX_questionnaire_drafts_last_saved_by_grant_id_task_id` ON `questionnaire_drafts` (`last_saved_by_grant_id`, `task_id`);

CREATE INDEX `IX_questionnaire_drafts_task_id_questionnaire_version_id` ON `questionnaire_drafts` (`task_id`, `questionnaire_version_id`);

CREATE INDEX `IX_questionnaire_responses_submitted_by_grant_id_task_id` ON `questionnaire_responses` (`submitted_by_grant_id`, `task_id`);

CREATE INDEX `IX_questionnaire_responses_task_id_questionnaire_version_id` ON `questionnaire_responses` (`task_id`, `questionnaire_version_id`);

CREATE UNIQUE INDEX `uq_responses_grant_key` ON `questionnaire_responses` (`submitted_by_grant_id`, `idempotency_key`);

CREATE UNIQUE INDEX `uq_responses_task` ON `questionnaire_responses` (`task_id`);

CREATE UNIQUE INDEX `uq_versions_number` ON `questionnaire_versions` (`questionnaire_id`, `respondent_role`, `version_number`);

CREATE UNIQUE INDEX `uq_questionnaires_code` ON `questionnaires` (`questionnaire_code`);

CREATE INDEX `ix_grant_tasks_task_scope` ON `teacher_grant_tasks` (`task_id`, `case_id`, `respondent_role`);

CREATE INDEX `IX_teacher_grant_tasks_grant_id_case_id_respondent_role` ON `teacher_grant_tasks` (`grant_id`, `case_id`, `respondent_role`);

CREATE INDEX `ix_grants_case_status` ON `teacher_grants` (`case_id`, `grant_status`);

CREATE UNIQUE INDEX `uq_grants_code_hash` ON `teacher_grants` (`code_hash`);

CREATE INDEX `ix_sessions_grant_status` ON `teacher_sessions` (`grant_id`, `session_status`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261005152359_InitialSharedCore', '8.0.13');

COMMIT;
