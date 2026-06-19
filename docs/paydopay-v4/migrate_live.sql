-- ============================================
-- Paylira v4 - Canlı DB Migration Script
-- Hedef: havalev3_havale2 @ 51.178.199.190
-- ============================================

-- 1. Laravel Sanctum
CREATE TABLE IF NOT EXISTS `personal_access_tokens` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `tokenable_type` VARCHAR(255) NOT NULL,
    `tokenable_id` BIGINT UNSIGNED NOT NULL,
    `name` VARCHAR(255) NOT NULL,
    `token` VARCHAR(64) NOT NULL,
    `abilities` TEXT NULL,
    `last_used_at` TIMESTAMP NULL,
    `expires_at` TIMESTAMP NULL,
    `created_at` TIMESTAMP NULL,
    `updated_at` TIMESTAMP NULL,
    UNIQUE INDEX `personal_access_tokens_token_unique` (`token`),
    INDEX `personal_access_tokens_tokenable_type_tokenable_id_index` (`tokenable_type`, `tokenable_id`)
);

-- 2. Users tablosu değişiklikleri
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `created_by` INT(11) NULL AFTER `auto_mode_change`;
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `merchant_group_id` INT UNSIGNED NULL AFTER `firm_id`;

-- 3. MerchantUser tablosu değişiklikleri
ALTER TABLE `merchantUser` ADD COLUMN IF NOT EXISTS `group_id` INT UNSIGNED NULL AFTER `id`;
ALTER TABLE `merchantUser` ADD COLUMN IF NOT EXISTS `deliveryCommission` DECIMAL(10,2) NOT NULL DEFAULT 0 AFTER `withdrawCommission`;

-- 4. Aracılar
CREATE TABLE IF NOT EXISTS `new_intermediaries` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL,
    `type` TINYINT NOT NULL,
    `status` TINYINT NOT NULL DEFAULT 1,
    `balance` DECIMAL(15,2) NOT NULL DEFAULT 0,
    `created_at` TIMESTAMP NULL
);

CREATE TABLE IF NOT EXISTS `new_intermediary_merchant` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `intermediary_id` INT UNSIGNED NOT NULL,
    `merchant_id` INT UNSIGNED NOT NULL,
    `commission_rate` DECIMAL(5,2) NOT NULL DEFAULT 0,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL
);

CREATE TABLE IF NOT EXISTS `new_intermediary_team` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `intermediary_id` INT UNSIGNED NOT NULL,
    `team_id` INT UNSIGNED NOT NULL,
    `commission_rate` DECIMAL(5,2) NOT NULL DEFAULT 0,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL
);

-- 5. Fon Depoları
CREATE TABLE IF NOT EXISTS `fund_storages` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL,
    `type` TINYINT NOT NULL,
    `wallet_address` VARCHAR(255) NULL,
    `balance` DECIMAL(15,2) NOT NULL DEFAULT 0,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL,
    `updated_at` TIMESTAMP NULL
);

-- 6. Merchant Grupları
CREATE TABLE IF NOT EXISTS `merchant_groups` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL
);

-- 7. Merchant Ödemeleri
CREATE TABLE IF NOT EXISTS `merchant_payments` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `merchant_id` INT UNSIGNED NOT NULL,
    `payment_type` TINYINT NOT NULL,
    `amount` DECIMAL(15,2) NOT NULL,
    `delivery_commission_rate` DECIMAL(5,2) NOT NULL DEFAULT 0,
    `delivery_commission_amount` DECIMAL(15,2) NOT NULL DEFAULT 0,
    `crypto_quantity` DECIMAL(15,6) NULL,
    `crypto_rate` DECIMAL(15,2) NULL,
    `tx_link` VARCHAR(500) NULL,
    `description` TEXT NULL,
    `created_by` INT UNSIGNED NULL,
    `created_at` TIMESTAMP NULL
);

-- 8. Günlük Kasa Snapshot
CREATE TABLE IF NOT EXISTS `daily_case_snapshots` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `snapshot_date` DATE NOT NULL,
    `entity_type` VARCHAR(20) NOT NULL,
    `entity_id` INT UNSIGNED NULL,
    `entity_name` VARCHAR(255) NOT NULL,
    `amount` DECIMAL(15,2) NOT NULL DEFAULT 0,
    `details` JSON NULL,
    `created_at` TIMESTAMP NULL,
    UNIQUE INDEX `daily_snapshot_unique` (`snapshot_date`, `entity_type`, `entity_id`),
    INDEX `idx_snapshot_date` (`snapshot_date`)
);

-- 9. Paylira Ortaklar
CREATE TABLE IF NOT EXISTS `paylira_partners` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL,
    `share_percent` DECIMAL(5,2) NOT NULL,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL
);

INSERT INTO `paylira_partners` (`name`, `share_percent`, `status`, `created_at`) VALUES
('Heav', 50.00, 1, NOW()),
('Bien', 50.00, 1, NOW());

-- 10. Ortak Ödemeleri
CREATE TABLE IF NOT EXISTS `paylira_partner_payments` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `partner_id` INT UNSIGNED NOT NULL,
    `payment_type` TINYINT NOT NULL,
    `amount` DECIMAL(15,2) NOT NULL,
    `crypto_quantity` DECIMAL(15,6) NULL,
    `crypto_rate` DECIMAL(15,2) NULL,
    `tx_link` VARCHAR(500) NULL,
    `description` TEXT NULL,
    `created_by` INT UNSIGNED NULL,
    `created_at` TIMESTAMP NULL
);

-- 11. Takım Ödemeleri
CREATE TABLE IF NOT EXISTS `team_payments` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `team_id` INT UNSIGNED NOT NULL,
    `payment_type` TINYINT NOT NULL,
    `amount` DECIMAL(15,2) NOT NULL,
    `crypto_quantity` DECIMAL(15,6) NULL,
    `crypto_rate` DECIMAL(15,2) NULL,
    `tx_link` VARCHAR(500) NULL,
    `fund_storage_id` INT UNSIGNED NULL,
    `description` TEXT NULL,
    `created_by` INT UNSIGNED NULL,
    `created_at` TIMESTAMP NULL
);

-- 12. Aracı Ödemeleri
CREATE TABLE IF NOT EXISTS `intermediary_payments` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `intermediary_id` INT UNSIGNED NOT NULL,
    `payment_type` TINYINT NOT NULL,
    `amount` DECIMAL(15,2) NOT NULL,
    `crypto_quantity` DECIMAL(15,6) NULL,
    `crypto_rate` DECIMAL(15,2) NULL,
    `tx_link` VARCHAR(500) NULL,
    `team_id` INT UNSIGNED NULL,
    `description` TEXT NULL,
    `created_by` INT UNSIGNED NULL,
    `created_at` TIMESTAMP NULL
);

-- ============================================
-- Tamamlandı!
-- ============================================
