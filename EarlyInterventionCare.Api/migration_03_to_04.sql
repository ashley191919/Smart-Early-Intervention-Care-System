START TRANSACTION;

CREATE TABLE `organizations` (
    `organization_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `code` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `name` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `status` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'ACTIVE',
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    CONSTRAINT `PK_organizations` PRIMARY KEY (`organization_id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `permissions` (
    `permission_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `description` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PK_permissions` PRIMARY KEY (`permission_id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `roles` (
    `role_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `description` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PK_roles` PRIMARY KEY (`role_id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `role_permissions` (
    `role_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `permission_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CONSTRAINT `PK_role_permissions` PRIMARY KEY (`role_id`, `permission_id`),
    CONSTRAINT `fk_role_permissions_permission` FOREIGN KEY (`permission_id`) REFERENCES `permissions` (`permission_id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_role_permissions_role` FOREIGN KEY (`role_id`) REFERENCES `roles` (`role_id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `users` (
    `user_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `username` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `password_hash` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `organization_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `role_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `phone` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `status` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'ACTIVE',
    `last_login_at_utc` datetime(6) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    CONSTRAINT `PK_users` PRIMARY KEY (`user_id`),
    CONSTRAINT `fk_users_organization` FOREIGN KEY (`organization_id`) REFERENCES `organizations` (`organization_id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_users_role` FOREIGN KEY (`role_id`) REFERENCES `roles` (`role_id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `roles` (`role_id`, `description`, `name`)
VALUES ('10000000-0000-4000-8000-000000000001', '系統管理員', 'ADMIN'),
('10000000-0000-4000-8000-000000000002', '個案管理師', 'CASE_MANAGER'),
('10000000-0000-4000-8000-000000000003', '醫療人員', 'MEDICAL_STAFF'),
('10000000-0000-4000-8000-000000000004', '家長', 'PARENT'),
('10000000-0000-4000-8000-000000000005', '教師', 'TEACHER');

CREATE INDEX `IX_teacher_grants_issued_by_user_id` ON `teacher_grants` (`issued_by_user_id`);

CREATE UNIQUE INDEX `uq_organizations_code` ON `organizations` (`code`);

CREATE INDEX `ix_role_permissions_permission` ON `role_permissions` (`permission_id`);

CREATE INDEX `ix_users_organization` ON `users` (`organization_id`);

CREATE INDEX `ix_users_role` ON `users` (`role_id`);

CREATE UNIQUE INDEX `uq_users_username` ON `users` (`username`);

ALTER TABLE `teacher_grants` ADD CONSTRAINT `fk_grants_issued_by_user` FOREIGN KEY (`issued_by_user_id`) REFERENCES `users` (`user_id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261006074424_IntegrateIdentityAndAuthorization', '8.0.13');

COMMIT;

